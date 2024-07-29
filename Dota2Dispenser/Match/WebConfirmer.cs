using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dota2Dispenser;
using Dota2Dispenser.Database;
using Dota2Dispenser.NoSteam;
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
    private readonly OpenDotaService _openDota;
    private readonly MatchTracker _matchTracker;
    private readonly Databaser _databaser;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<WebConfirmer> _logger;

    /// <summary>
    /// Пауза между запросами к апи.
    /// </summary>
    readonly TimeSpan updateDelayTime;

    bool isRunning = false;

    public WebConfirmer(OpenDotaService openDota, MatchTracker matchTracker, Databaser databaser,
        IHostApplicationLifetime lifetime, ILogger<WebConfirmer> logger, IOptions<AppOptions> options)
    {
        this._openDota = openDota;
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

    private async Task CheckMatchAsync(TrackedMatch tracked)
    {
        if (tracked.match.TvInfo == null)
            return;

        OpenMatch openMatch;
        try
        {
            openMatch = await _openDota.DoAsync(tracked.match.TvInfo.MatchId, _lifetime.ApplicationStopping);
        }
        catch (MatchNotFoundException)
        {
            _logger.LogWarning("Матч не найден {id} ({sourceId})", tracked.match.Id, tracked.match.TvInfo.MatchId);
            return;
        }
        catch (Exception e)
        {
            _logger.LogError(e, $"{nameof(CheckMatchAsync)} {nameof(_openDota.DoAsync)} Exception");
            return;
        }

        _matchTracker.RemoveDeadMatch(tracked);

        await _databaser.UpdateMatchAsync(tracked.match, () =>
        {
            tracked.match.GameDate = DateTimeOffset.FromUnixTimeSeconds(openMatch.StartTime).UtcDateTime;
            tracked.match.MatchResult = MatchResult.Finished;
            tracked.match.DetailsInfo =
                new Database.Models.DetailsMatchInfo(openMatch.RadiantWin, TimeSpan.FromSeconds(openMatch.Duration));

            if (tracked.match.Players?.Count == openMatch.Players.Length)
            {
                foreach (var player in tracked.match.Players)
                {
                    OpenPlayer? detailed = openMatch.Players
                        .Where(p => p.AccountId != null && p.AccountId != 4294967295)
                        .FirstOrDefault(p =>
                            new SteamID((uint)p.AccountId!.Value, EUniverse.Public, EAccountType.Individual)
                                .ConvertToUInt64() == player.SteamId);

                    if (detailed == null)
                    {
                        // Такое может быть, если человечек скрыл профиль. Увы.

                        if (player.HeroId != 0)
                        {
                            // Почти всегда герой будет, так что используем.
                            detailed = openMatch.Players.FirstOrDefault(p => p.HeroId == player.HeroId);
                        }
                    }

                    if (detailed == null)
                    {
                        _logger.LogWarning("Не удалось найти детали для {id}", player.Id);
                        continue;
                    }

                    if (player.HeroId == 0)
                        player.HeroId = detailed.HeroId;
                    player.LeaverStatus = detailed.LeaverStatus;
                    player.PlayerSlot = detailed.PlayerSlot;
                    player.TeamNumber = detailed.IsRadiant switch
                    {
                        true => 0,
                        false => 1,
                        null => 3
                    };
                    player.TeamSlot = Array.IndexOf(openMatch.Players, detailed) -
                                      (detailed.IsRadiant == false ? 5 : 0);
                }
            }
            else
            {
                // Чтобы это случилось, бот должен быть выключен до того, как пройдёт пара минут с начала матча.
                // Маловероятно, всё равно.
                tracked.match.Players = openMatch.Players.Select(p => new Database.Models.PlayerModel()
                {
                    Match = tracked.match,
                    PartyIndex = -2,
                    LeaverStatus = p.LeaverStatus,
                    HeroId = p.HeroId,
                    SteamId = new SteamID(HelpMe(p.AccountId), EUniverse.Public, EAccountType.Individual)
                        .ConvertToUInt64(),
                    PlayerSlot = p.PlayerSlot,
                    TeamNumber = p.IsRadiant switch
                    {
                        true => 0,
                        false => 1,
                        null => 3
                    },
                    TeamSlot = Array.IndexOf(openMatch.Players, p) - (p.IsRadiant == false ? 5 : 0)
                }).ToArray();
            }
        });

        if (!tracked.gotAllHeroes)
        {
            _logger.LogDebug("Добили героев.");
        }

        _logger.LogInformation("Закрыли {matchId} ({note})", tracked.match.Id, tracked.CreateNote());
    }

    private async Task RemoveMatchAsync(TrackedMatch tracked, string reason)
    {
        _matchTracker.RemoveDeadMatch(tracked);
        await _databaser.UpdateMatchAsync(tracked.match, () => tracked.match.MatchResult = MatchResult.Broken);

        _logger.LogInformation("Сломался {matchId} ({note}) {reason}", tracked.match.Id, tracked.CreateNote(), reason);
    }

    private uint HelpMe(ulong? id)
    {
        if (id == null)
            return 0;

        return (uint)id.Value;
    }
}