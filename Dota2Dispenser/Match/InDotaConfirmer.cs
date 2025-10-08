using Dota2Dispenser.Database;
using Dota2Dispenser.Database.Models;
using Dota2Dispenser.Shared.Consts;
using Dota2Dispenser.Steam;
using Microsoft.Extensions.Options;
using SteamKit2;
using SteamKit2.GC.Dota.Internal;
using SteamKitDota2;

namespace Dota2Dispenser.Match;

/// <summary>
/// Если ебаный апи дота опять сдох в харче, попробуем по возможности достать средствами дота клиента.
/// </summary>
public class InDotaConfirmer
{
    private readonly SteamService _steam;
    private readonly MatchTracker _matchTracker;
    private readonly Databaser _databaser;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<InDotaConfirmer> _logger;

    private readonly TimeSpan _loopCheckDelay = TimeSpan.FromSeconds(1);
    private readonly TimeSpan _cooldown;

    public InDotaConfirmer(SteamService steam, MatchTracker matchTracker, Databaser databaser,
        IHostApplicationLifetime lifetime, IOptions<AppOptions> options, ILogger<InDotaConfirmer> logger)
    {
        _steam = steam;
        _matchTracker = matchTracker;
        _databaser = databaser;
        _lifetime = lifetime;
        _logger = logger;
        _cooldown = options.Value.InDotaCheckCooldown;
    }

    public void Init()
    {
        _logger.LogInformation("Запускаем...");

        Task.Run(LoopAsync);
    }

    private async Task LoopAsync()
    {
        while (!_lifetime.ApplicationStopping.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_loopCheckDelay, _lifetime.ApplicationStopping);
            }
            catch
            {
                return;
            }

            // мне впадлу хуйню какую то делать как в вебконфирмере...
            TrackedMatch[] deads = GetMatchesToCheck();

            if (deads.Length == 0)
                continue;

            TrackedMatch? toCheck = deads.FirstOrDefault(d => d.LastInDotaCheckAttempt == null);

            if (toCheck == null)
            {
                toCheck = deads.OrderBy(d => d.LastInDotaCheckAttempt!.Value).First();
            }

            await CheckMatchAsync(toCheck);

            try
            {
                await Task.Delay(_cooldown, _lifetime.ApplicationStopping);
            }
            catch
            {
                return;
            }
        }
    }

    private async Task CheckMatchAsync(TrackedMatch tracked)
    {
        // кстати тупо скопировал из вебкомфимера, ы.

        // эта проверка делается ранее, и тв инфо не может пропасть, но иначе идешка ноет
        if (tracked.Match.TvInfo == null)
            return;

        CMsgDOTAMatch? dotaMatch;
        try
        {
            AccountModel? toCheckTarget = tracked.WereSeen.FirstOrDefault(s => _steam.IsFriend(s.SteamID));

            if (toCheckTarget == null)
                throw new Exception(
                    $"Да как он блять не друг, если я только что смотрел и он друг нахуй был {tracked.CreateNote()}");

            SteamID steamId = new SteamID(toCheckTarget.SteamID);

            SteamDota.DotaPlayerHistoryCallback history = await _steam.Dota.RequestMatchHistory(steamId.AccountID);

            CMsgDOTAGetPlayerMatchHistoryResponse.Match? historyMatch =
                history.Response.matches.FirstOrDefault(m => m.match_id == tracked.Match.TvInfo.MatchId);

            if (historyMatch == null)
            {
                _logger.LogWarning("Матч не найден {id} ({sourceId})", tracked.Match.Id, tracked.Match.TvInfo.MatchId);
                tracked.LastInDotaCheckAttempt = DateTimeOffset.UtcNow;
                return;
            }

            SteamDota.MatchDetailsCallback response = await _steam.Dota.RequestMatchDetails(historyMatch.match_id);

            dotaMatch = response.Response.match;
        }
        catch (Exception e)
        {
            _logger.LogError(e, $"{nameof(CheckMatchAsync)} Exception");
            tracked.LastInDotaCheckAttempt = DateTimeOffset.UtcNow;
            return;
        }

        // TODO здесь везде всратая гонка сос стояний, но как будто бы насрать? ну обновит он матч дважды, кому не похуй?
        _matchTracker.RemoveDeadMatch(tracked);
        tracked.LastInDotaCheckAttempt = DateTimeOffset.UtcNow;

        await _databaser.UpdateMatchAsync(tracked.Match, () =>
        {
            bool? radiantWin = dotaMatch.match_outcome switch
            {
                EMatchOutcome.k_EMatchOutcome_RadVictory => true,
                EMatchOutcome.k_EMatchOutcome_DireVictory => false,
                _ => null
            };

            tracked.Match.GameDate = DateTimeOffset.FromUnixTimeSeconds(dotaMatch.starttime).UtcDateTime;
            tracked.Match.MatchResult = MatchResult.Finished;
            tracked.Match.DetailsInfo =
                new Database.Models.DetailsMatchInfo(radiantWin,
                    TimeSpan.FromSeconds(dotaMatch.duration));

            if (tracked.Match.Players?.Count == dotaMatch.players.Count)
            {
                foreach (var player in tracked.Match.Players)
                {
                    CMsgDOTAMatch.Player? detailed = dotaMatch.players
                        // я кстати хзызы что это за 4294967295. мог бы и коммент на будущее оставить, когда писал
                        // unit max наверное
                        .Where(p => p.account_id != 0 && p.account_id != 4294967295)
                        .FirstOrDefault(p =>
                            new SteamID(p.account_id, EUniverse.Public, EAccountType.Individual)
                                .ConvertToUInt64() == player.SteamId);

                    if (detailed == null)
                    {
                        // Такое может быть, если человечек скрыл профиль. Увы.

                        if (player.HeroId != 0)
                        {
                            // Почти всегда герой будет, так что используем.
                            detailed = dotaMatch.players.FirstOrDefault(p => p.hero_id == player.HeroId);
                        }
                    }

                    if (detailed == null)
                    {
                        _logger.LogWarning("Не удалось найти детали для {id}", player.Id);
                        continue;
                    }

                    if (player.HeroId == 0)
                        player.HeroId = detailed.hero_id;
                    player.LeaverStatus = detailed.ShouldSerializeleaver_status() ? (int)detailed.leaver_status : null;
                    player.PlayerSlot = (int)detailed.player_slot;
                    player.TeamNumber = MakeTeamNumber(detailed.team_number);
                    // TODO в модельке есть слот. Я просто не могу проверить, что там приходит
                    player.TeamSlot = dotaMatch.players.IndexOf(detailed) -
                                      (detailed.team_number == DOTA_GC_TEAM.DOTA_GC_TEAM_BAD_GUYS ? 5 : 0);
                }
            }
            else
            {
                // Чтобы это случилось, бот должен быть выключен до того, как пройдёт пара минут с начала матча.
                // Маловероятно, всё равно.
                tracked.Match.Players = dotaMatch.players.Select((p, index) => new Database.Models.PlayerModel()
                {
                    Match = tracked.Match,
                    PartyIndex = -2,
                    LeaverStatus = p.ShouldSerializeleaver_status() ? (int)p.leaver_status : null,
                    HeroId = p.hero_id,
                    SteamId = new SteamID(p.account_id, EUniverse.Public, EAccountType.Individual)
                        .ConvertToUInt64(),
                    PlayerSlot = p.ShouldSerializeplayer_slot() ? (int)p.player_slot : null,
                    TeamNumber = MakeTeamNumber(p.team_number),
                    // TODO в модельке есть слот. Я просто не могу проверить, что там приходит
                    // Солнир занят, а у нексуса нет матчей в иммортал драфте на дотабафе блять, потому что вальвы хуесосы.
                    // это нужно только для определения, кто на ком был. ну... потом как нить. у меня комп в 5 фпс работает, я не полезу в доту
                    TeamSlot = index - (p.team_number == DOTA_GC_TEAM.DOTA_GC_TEAM_BAD_GUYS ? 5 : 0)
                }).ToArray();
            }
        });

        if (!tracked.GotAllHeroes)
        {
            _logger.LogDebug("Добили героев.");
        }

        _logger.LogInformation("Закрыли {matchId} ({note})", tracked.Match.Id, tracked.CreateNote());
    }

    private int? MakeTeamNumber(DOTA_GC_TEAM team)
    {
        return team switch
        {
            DOTA_GC_TEAM.DOTA_GC_TEAM_GOOD_GUYS => 0,
            DOTA_GC_TEAM.DOTA_GC_TEAM_BAD_GUYS => 1,
            _ => null
        };
    }

    private TrackedMatch[] GetMatchesToCheck()
    {
        return _matchTracker.GetDeadMatchesMODS(list => list
            .Where(match => match.LastWebCheckAttempt != null)
            .Where(match => match.Match.TvInfo != null)
            .Where(match => match.WereSeen.Any(s => _steam.IsFriend(s.SteamID)))
        );
    }
}