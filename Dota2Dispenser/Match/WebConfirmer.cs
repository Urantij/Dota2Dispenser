using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dota2Dispenser;
using Dota2Dispenser.Database;
using Dota2Dispenser.Shared.Consts;
using Dota2Dispenser.Steam;
using Microsoft.Extensions.Options;
using SteamKit2;
using SteamKitDota2.Web;

namespace Dota2Dispenser.Match;

/// <summary>
/// Берёт мертвые матчи из матчтрекера и чекает их через веб апи.
/// Также очень старые мертвые матчи удаляет.
/// </summary>
public class WebConfirmer
{
    private readonly DotaApiService _dotaApi;
    private readonly MatchTracker _matchTracker;
    private readonly Databaser _databaser;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<WebConfirmer> _logger;

    /// <summary>
    /// Как долго может барахтаться игра, прежде чем её назовут брокен и выкинут.
    /// </summary>
    readonly TimeSpan updateDelayTime;
    bool isRunning = false;

    public WebConfirmer(DotaApiService dotaApi, MatchTracker matchTracker, Databaser databaser, IHostApplicationLifetime lifetime, ILogger<WebConfirmer> logger, IOptions<AppOptions> options)
    {
        this._dotaApi = dotaApi;
        this._matchTracker = matchTracker;
        this._databaser = databaser;
        this._lifetime = lifetime;
        this._logger = logger;
        this.updateDelayTime = options.Value.WebConfirmerUpdateDelayTime;
    }

    public void Init()
    {
        _logger.LogInformation("Запускаем...");
        isRunning = true;

        Task.Run(LoopAsync);
    }

    async Task LoopAsync()
    {
        // Смысл в том, что коллекция на той стороне может меняться.
        // Элементы пропадать из всех частей, добавляться новые в конец.
        // Поэтому мы берём список матчей, идём от начала и до конца по нему.
        // После каждой проверки матча берём коллекцию ещё раз и сохраняем. Если итема нет в обновлённой версии, значит его не проверяем.
        // Когда наша проходка заканчивается, берём матчи с той стороны, и кладём в себя новые матчи, которые были добавлены в конец.
        // Если их нет, просто берём всё и как бы начинаем цикл заново.
        TrackedMatch[] startQueue = _matchTracker.GetDeadMatchesArray();
        TrackedMatch[] updatedArray = startQueue;

        while (isRunning && !_lifetime.ApplicationStopping.IsCancellationRequested)
        {
            try
            {
                if (startQueue.Length == 0)
                {
                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(1), _lifetime.ApplicationStopping);
                    }
                    catch { return; }

                    startQueue = _matchTracker.GetDeadMatchesArray();
                    updatedArray = startQueue;
                    continue;
                }

                foreach (TrackedMatch item in startQueue)
                {
                    if (updatedArray.Contains(item))
                    {
                        await CheckMatchAsync(item);

                        try
                        {
                            await Task.Delay(updateDelayTime, _lifetime.ApplicationStopping);
                        }
                        catch { return; }
                        updatedArray = _matchTracker.GetDeadMatchesArray();
                    }
                }

                // Теперь новые смотрим.
                // Для этого находим самый возможно последний элемент из проверенной коллекции.
                // И все элементы после этого проверенного будут новыми.
                int lastCheckedIndex = Array.FindLastIndex(updatedArray, upM => startQueue.Contains(upM));
                if (lastCheckedIndex == -1)
                {
                    // Никаких новых нет, просто начинаем цикл с нуля. Мы уже подождали в цикле выше, так что сидим чилим.
                    startQueue = _matchTracker.GetDeadMatchesArray();
                    updatedArray = startQueue;
                    continue;
                }

                // Значит, может быть есть новые.
                TrackedMatch[] newChecks = updatedArray.Skip(lastCheckedIndex + 1).ToArray();
                if (newChecks.Length > 0)
                {
                    // Есть что-то новое. 
                    // Они проверятся, и если будут ещё новые, проверятся они, а иначе всё начнётся заново.
                    startQueue = newChecks;
                    updatedArray = startQueue;
                }
                else
                {
                    // Нового нет, просто начинаем заново.
                    startQueue = _matchTracker.GetDeadMatchesArray();
                    updatedArray = startQueue;
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"{nameof(LoopAsync)} Exception");
                try
                {
                    await Task.Delay(updateDelayTime, _lifetime.ApplicationStopping);
                }
                catch { return; }
            }
        }
    }

    async Task CheckMatchAsync(TrackedMatch tracked)
    {
        if (tracked.match.TvInfo == null)
            return;

        DotaApi.MatchDetails details;
        try
        {
            details = await _dotaApi.api.GetMatchDetails(tracked.match.TvInfo.MatchId);
        }
        catch (WebAPIRequestException webEx) when (webEx.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable)
        {
            _logger.LogWarning($"{nameof(CheckMatchAsync)} {nameof(_dotaApi.api.GetMatchDetails)} ServiceUnavailable");
            return;
        }
        catch (Exception e)
        {
            _logger.LogError(e, $"{nameof(CheckMatchAsync)} {nameof(_dotaApi.api.GetMatchDetails)} Exception");
            return;
        }

        if (details.error != null)
        {
            if (details.error == DotaApi.MatchDetails.PracticeNotAvailableError)
            {
                await RemoveMatchAsync(tracked, "practice match");
                return;
            }
            else if (details.error != DotaApi.MatchDetails.MatchNotFoundError)
            {
                _logger.LogError($"{nameof(CheckMatchAsync)} Details error {{error}}", details.error);
            }
            else
            {
                _logger.LogDebug("Проверили {matchId}, всё ещё идёт ({note})", tracked.match.TvInfo.MatchId, tracked.CreateNote());
            }
            return;
        }

        // Я не уверен, возможно ли это.
        if (details.players == null)
        {
            _logger.LogCritical($"{nameof(details.players)} is null");
            return;
        }

        _matchTracker.RemoveDeadMatch(tracked);

        await _databaser.UpdateMatchAsync(tracked.match, () =>
        {
            tracked.match.GameDate = DateTimeOffset.FromUnixTimeSeconds(details.start_time).UtcDateTime;
            tracked.match.MatchResult = MatchResult.Finished;
            tracked.match.DetailsInfo = new Database.Models.DetailsMatchInfo(details.radiant_win, TimeSpan.FromSeconds(details.duration));

            for (int i = 0; i < tracked.match.Players!.Count; i++)
            {
                Database.Models.PlayerModel player = tracked.match.Players.ElementAt(i);
                DotaApi.MatchDetails.Player detailed = details.players[i];

                if (player.HeroId == 0)
                    player.HeroId = detailed.hero_id;
                player.LeaverStatus = detailed.leaver_status;
            }
        });

        if (!tracked.gotAllHeroes)
        {
            _logger.LogDebug("Добили героев.");
        }

        _logger.LogInformation("Закрыли {matchId} ({note})", tracked.match.Id, tracked.CreateNote());
    }

    async Task RemoveMatchAsync(TrackedMatch tracked, string reason)
    {
        _matchTracker.RemoveDeadMatch(tracked);
        await _databaser.UpdateMatchAsync(tracked.match, () => tracked.match.MatchResult = MatchResult.Broken);

        _logger.LogInformation("Сломался {matchId} ({note}) {reason}", tracked.match.Id, tracked.CreateNote(), reason);
    }
}
