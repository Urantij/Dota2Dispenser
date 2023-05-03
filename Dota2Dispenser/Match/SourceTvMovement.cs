using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dota2Dispenser.Database;
using Dota2Dispenser.Database.Models;
using Dota2Dispenser.Steam;
using Microsoft.Extensions.Options;
using SteamKit2;
using SteamKitDota2;

namespace Dota2Dispenser.Match;

public class SourceTvMovement
{
    private readonly MatchTracker _matchTracker;
    private readonly SteamService _steam;
    private readonly Databaser _databaser;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<SourceTvMovement> _logger;

    private readonly TimeSpan updateDelayTime;

    private bool isRunning = false;

    public SourceTvMovement(MatchTracker matchTracker, SteamService steam, Databaser databaser, IHostApplicationLifetime lifetime, ILogger<SourceTvMovement> logger, IOptions<AppOptions> options)
    {
        _matchTracker = matchTracker;
        _steam = steam;
        _databaser = databaser;
        _lifetime = lifetime;
        _logger = logger;

        updateDelayTime = options.Value.SourceTvUpdateDelayTime;
    }

    public void Init()
    {
        if (isRunning)
            return;

        _logger.LogInformation("Запускаем...");
        isRunning = true;

        Task.Run(LoopAsync);
    }

    private async Task LoopAsync()
    {
        while (isRunning && !_lifetime.ApplicationStopping.IsCancellationRequested)
        {
            if (!_steam.dota.Ready)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), _lifetime.ApplicationStopping);
                }
                catch { return; }
                continue;
            }

            var matches = _matchTracker.GetLiveMatchesForSourceTv();
            if (matches.Length == 0)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), _lifetime.ApplicationStopping);
                }
                catch { return; }
                continue;
            }

            try
            {
                ulong[] stvTargets = matches.Select(m => m.match.WatchableGameId).ToArray();

                SteamDota.SourceTvGamesCallback stv = await _steam.dota.RequestSpecificSourceTvGames(stvTargets);

                foreach (var tracked in matches)
                {
                    SteamKit2.GC.Dota.Internal.CSourceTVGameSmall? source = stv.response.game_list.FirstOrDefault(g => g.lobby_id == tracked.match.WatchableGameId);

                    await ProcessAsync(tracked, source);
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"{nameof(LoopAsync)} Exception");
            }

            try
            {
                await Task.Delay(updateDelayTime, _lifetime.ApplicationStopping);
            }
            catch { return; }
        }
    }

    async Task ProcessAsync(TrackedMatch tracked, SteamKit2.GC.Dota.Internal.CSourceTVGameSmall? source)
    {
        if (source == null)
        {
            // TODO Подумать, что делать.
            return;
        }

        if (tracked.match.TvInfo == null)
        {
            await _databaser.UpdateMatchAsync(tracked.match, () =>
            {
                tracked.match.TvInfo = new SourceMatchInfo(source.match_id, source.lobby_type, source.game_mode, source.ShouldSerializeaverage_mmr() ? source.average_mmr : null);
                //tracked.match.GameDate = DateTime.UtcNow - TimeSpan.FromSeconds(source.game_time);
                //проверка на наличие, тому шо я не знаю, всегда ли есть активейт тайм
                //tracked.match.GameDate = source.ShouldSerializeactivate_time() ? DateTimeOffset.FromUnixTimeSeconds(source.activate_time).UtcDateTime : DateTime.UtcNow - TimeSpan.FromSeconds(source.game_time);
                //в итоге оказалось, активейт тайм вообще непонятно что за хуйню выдаёт, там иногда буквально дата в будущем
                // tracked.match.GameDate = DateTime.UtcNow;
                tracked.match.Players = source.players.Select(sourcePlayer =>
                {
                    SteamID steamId = new(sourcePlayer.account_id, EUniverse.Public, EAccountType.Individual);

                    PlayerModel player = new(tracked.match.Id, steamId.ConvertToUInt64(), sourcePlayer.hero_id);

                    player.PartyIndex = tracked.parties.FindIndex(p => p.Contains(player.SteamId));

                    return player;
                }).ToArray();
            });
            tracked.gotAllHeroes = tracked.match.Players!.All(p => p.HeroId != 0);
        }
        else if (!tracked.gotAllHeroes)
        {
            // Тут может случиться так, что ничего не изменилось на самом деле.
            // Но логику мне писать лень, пусть будет так.
            await _databaser.UpdateMatchAsync(tracked.match, () =>
            {
                for (int i = 0; i < tracked.match.Players!.Count; i++)
                {
                    tracked.match.Players.ElementAt(i).HeroId = source.players[i].hero_id;
                }
            });
            tracked.gotAllHeroes = tracked.match.Players!.All(p => p.HeroId != 0);

            if (tracked.gotAllHeroes)
            {
                _logger.LogInformation("Получили всех героев {matchId} ({note})", tracked.match.Id, tracked.CreateNote());
            }
        }
    }
}
