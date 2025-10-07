using Dota2Dispenser.Database;
using Dota2Dispenser.Database.Models;
using Dota2Dispenser.Person;
using Dota2Dispenser.Shared.Consts;
using Dota2Dispenser.Steam;
using Microsoft.Extensions.Options;
using SteamKitDota2;
using SteamKitDota2.More;

namespace Dota2Dispenser.Match;

// Мы не хотим влиять на систему одновременно с одного матча или с одного аккаунта.
class SynchroRp
{
    public AccountModel Account { get; }
    public ulong? WatchableGameId { get; }
    public TaskCompletionSource Tsc { get; }

    public SynchroRp(AccountModel account, ulong? watchableGameId, TaskCompletionSource tsc)
    {
        this.Account = account;
        this.WatchableGameId = watchableGameId;
        this.Tsc = tsc;
    }
}

/// <summary>
/// Опрашивает цели через RichPresence
/// </summary>
public class RpMovement
{
    // вотчбл айди кстати и так 0, но ладно
    private static readonly string[] IgnoreStatuses =
    {
        "#DOTA_RP_INIT", "#DOTA_RP_IDLE", "#DOTA_RP_SPECTATING", "#DOTA_RP_FINDING_MATCH",
        "#DOTA_RP_GAME_IN_PROGRESS_CUSTOM"
    };

    private readonly MatchTracker _matchTracker;
    private readonly TargetsContainer _targetsContainer;
    private readonly SteamService _steam;
    private readonly Databaser _databaser;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<RpMovement> _logger;

    private readonly List<SynchroRp> _processingAccounts = new();

    private readonly TimeSpan _updateDelayTime;

    private bool _isRunning = false;

    public RpMovement(MatchTracker matchTracker, TargetsContainer targetsContainer, SteamService steam,
        Databaser databaser, IHostApplicationLifetime lifetime, ILogger<RpMovement> logger,
        IOptions<AppOptions> options)
    {
        _matchTracker = matchTracker;
        _targetsContainer = targetsContainer;
        _steam = steam;
        _databaser = databaser;
        _lifetime = lifetime;
        _logger = logger;
        _updateDelayTime = options.Value.RpUpdateDelayTime;

        steam.DotaPersonaReceived += DotaPersonaReceived;
    }

    public void Init()
    {
        if (_isRunning)
            return;

        _logger.LogInformation("Запускаем...");
        _isRunning = true;

        Task.Run(LoopAsync);
    }

    private void DotaPersonaReceived(SteamDota.DotaPersonaStateCallback obj)
    {
        ulong targetId = obj.friendId.ConvertToUInt64();

        AccountModel? account = _targetsContainer.FindAccount(targetId);
        if (account == null)
            return;

        Task.Run(async () =>
        {
            try
            {
                await ExecuteRpProcessingAsync(account, obj.richPresence);
            }
            catch (Exception e)
            {
                _logger.LogCritical(e, $"{nameof(DotaPersonaReceived)}.{nameof(ExecuteRpProcessingAsync)} Exception");
            }
        });
    }

    async Task ExecuteRpProcessingAsync(AccountModel target, DotaRichPresenceInfo? rpInfo)
    {
        TaskCompletionSource tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var help = new SynchroRp(target, rpInfo?.watchableGameId, tcs);

        Task[] existingTasks;
        lock (_processingAccounts)
        {
            existingTasks = _processingAccounts
                .Where(pa => pa.Account == help.Account || pa.WatchableGameId == help.WatchableGameId)
                .Select(pa => pa.Tsc.Task).ToArray();

            _processingAccounts.Add(help);
        }

        await Task.WhenAll(existingTasks);

        try
        {
            await ProcessRichPresenceAsync(target, rpInfo);
        }
        finally
        {
            tcs.SetResult();
            lock (_processingAccounts)
            {
                _processingAccounts.Remove(help);
            }
        }
    }

    private async Task LoopAsync()
    {
        while (_isRunning && !_lifetime.ApplicationStopping.IsCancellationRequested)
        {
            if (!_steam.Client.IsConnected || !_steam.LoggedIn)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), _lifetime.ApplicationStopping);
                }
                catch
                {
                    return;
                }

                continue;
            }

            AccountModel[] targets = _targetsContainer.GetTargets();
            if (targets.Length == 0)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), _lifetime.ApplicationStopping);
                }
                catch
                {
                    return;
                }

                continue;
            }

            try
            {
                SteamDota.RichPresenceInfoCallback rp_response =
                    await _steam.Dota.RequestRichPresence(targets.Select(t => t.SteamID).ToArray());

                foreach (AccountModel target in targets)
                {
                    DotaRichPresenceInfo? rpInfo = rp_response.response.rich_presence
                        .Where(rp => rp.steamid_user == target.SteamID)
                        // тут не уверен
                        .Select(DotaRichPresenceInfo.FromRichPresence)
                        .FirstOrDefault(); // Single?

                    await ExecuteRpProcessingAsync(target, rpInfo);
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"{nameof(LoopAsync)} Exception");
            }

            try
            {
                await Task.Delay(_updateDelayTime, _lifetime.ApplicationStopping);
            }
            catch
            {
                return;
            }
        }
    }

    private async Task ProcessRichPresenceAsync(AccountModel target, DotaRichPresenceInfo? rpInfo)
    {
        // Значит так.
        // Игрок может быть в матче, который мы хотим отслеживать, или нет.
        // И есть матчи, которые мы уже отслеживаем.
        // Значит, если игрок находится в матче, который не отслеживается, начать его отслеживать.
        // Если игрок находится в отслеживаемом матче, но его нет в списке отслеживаемых аккаунтов, добавить его туда.
        // Если есть отслеживаемый матч с этим игроком, а он теперь не в матче, или другом матче, и у этого отслеживаемого матча нет других игроков, отметить его как дед.
        // Изменение матчей происходит по ходу дела, поэтому один матч может быть убит, и в этом же цикле реснут.
        // Но я хочу жизнь проще. К тому же, сценарий редкий.

        TrackedMatch? oldMatch = _matchTracker.GetLastMatchByAccount(target);

        _logger.LogDebug("Статус: \"{status}\" ({lobbyId})", rpInfo?.status ?? "null", rpInfo?.watchableGameId);

        TrackedMatch? currentMatch;

        if (rpInfo != null && rpInfo.watchableGameId != null && rpInfo.watchableGameId != 0 &&
            !IgnoreStatuses.Contains(rpInfo.status))
        {
            // Игрок находится в игре, за которой мы хотим следить.
            currentMatch = _matchTracker.FindLiveMatchByLobbyId(rpInfo.watchableGameId.Value);

            if (currentMatch != null)
            {
                // Этот матч уже есть, всё в поряде чоколаде.
                if (!currentMatch.Playing.Contains(target))
                {
                    currentMatch.AddPlayer(target);
                    UpdateParties(currentMatch, target.SteamID, rpInfo.party_Members);
                }

                await UpdateMatchRpStatusAsync(currentMatch, rpInfo, true);
            }
            else
            {
                currentMatch = _matchTracker.FindDeadMatchByLobbyId(rpInfo.watchableGameId.Value);

                if (currentMatch != null)
                {
                    if (!currentMatch.Playing.Contains(target))
                    {
                        currentMatch.AddPlayer(target);
                        UpdateParties(currentMatch, target.SteamID, rpInfo.party_Members);
                    }

                    await UpdateMatchRpStatusAsync(currentMatch, rpInfo, true);

                    _matchTracker.ResurrectMatch(currentMatch);
                }
                else
                {
                    // Мы не следим, а нужно бы.

                    MatchModel match = new(rpInfo.watchableGameId.Value, DateTime.UtcNow)
                    {
                        MatchResult = MatchResult.None
                    };

                    currentMatch = new TrackedMatch(match, false, null);
                    currentMatch.AddPlayer(target);
                    UpdateParties(currentMatch, target.SteamID, rpInfo.party_Members);
                    await UpdateMatchRpStatusAsync(currentMatch, rpInfo, false);

                    await _databaser.AddMatchAsync(match);
                    _matchTracker.AddMatch(currentMatch);
                }
            }
        }
        else
        {
            currentMatch = null;
        }

        if (oldMatch != null && oldMatch != currentMatch)
        {
            oldMatch.Playing.Remove(target);

            if (oldMatch.Playing.Count == 0)
            {
                // Никого нет, чтобы продолжать следить через рп, убиваем.
                await _matchTracker.KillMatchAsync(oldMatch);
            }
        }
    }

    private void UpdateParties(TrackedMatch tracked, ulong accountId, ulong[]? party_Members)
    {
        if (party_Members == null)
            return;

        bool exist = tracked.Parties.Any(p => p.Contains(accountId));
        if (exist)
            return;

        tracked.Parties.Add(party_Members);
    }

    private Task UpdateMatchRpStatusAsync(TrackedMatch tracked, DotaRichPresenceInfo rpInfo, bool updateDb)
    {
        // На данный момент я не уврен, что возможно получить матч без этой информации.
        // Поэтому обновления статуса уже найденного матча должно не работать никогда.
        // Не знаю, зачем я это добавил.
        // TODO xdd?
        // Имортал драфт не парсится, приходит нулл.
        if (tracked.Match.RichPresenceLobbyType != null)
            return Task.CompletedTask;

        string rpStatus;
        try
        {
            // TODO это можно было вынести в стимкит?
            if (rpInfo.status == "#DOTA_RP_WAIT_FOR_PLAYERS_TO_LOAD")
            {
                rpStatus = RpStatusHelper.Parse_DOTA_RP_WAIT_FOR_PLAYERS_TO_LOAD(rpInfo.raw).LobbyType;
            }
            else if (rpInfo.status == "#DOTA_RP_HERO_SELECTION")
            {
                rpStatus = RpStatusHelper.Parse_DOTA_RP_HERO_SELECTION(rpInfo.raw).LobbyType;
            }
            else if (rpInfo.status == "#DOTA_RP_STRATEGY_TIME")
            {
                rpStatus = RpStatusHelper.Parse_DOTA_RP_STRATEGY_TIME(rpInfo.raw).LobbyType;
            }
            else if (rpInfo.status == "#DOTA_RP_PLAYING_AS")
            {
                rpStatus = RpStatusHelper.Parse_DOTA_RP_PLAYING_AS(rpInfo.raw).LobbyType;
            }
            else return Task.CompletedTask;
        }
        catch (Exception e)
        {
            _logger.LogError(e, $"{nameof(UpdateMatchRpStatusAsync)} Не удалось пропарсить рп ({{status}})",
                rpInfo.status);
            return Task.CompletedTask;
        }

        if (updateDb)
            return _databaser.UpdateMatchAsync(tracked.Match, () => tracked.Match.RichPresenceLobbyType = rpStatus);

        return Task.CompletedTask;
    }
}