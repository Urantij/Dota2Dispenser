using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dota2Dispenser.Database;
using Dota2Dispenser.Database.Models;
using Dota2Dispenser.Person;
using Dota2Dispenser.Shared.Consts;
using Dota2Dispenser.Steam;
using Microsoft.Extensions.Options;
using SteamKit2;
using SteamKitDota2;
using SteamKitDota2.More;

namespace Dota2Dispenser.Match;

// Мы не хотим влиять на систему одновременно с одного матча или с одного аккаунта.
class SynchroRp
{
    public readonly AccountModel account;
    public readonly ulong? watchableGameId;
    public readonly TaskCompletionSource tsc;

    public SynchroRp(AccountModel account, ulong? watchableGameId, TaskCompletionSource tsc)
    {
        this.account = account;
        this.watchableGameId = watchableGameId;
        this.tsc = tsc;
    }
}

/// <summary>
/// Опрашивает цели через RichPresence
/// </summary>
public class RpMovement
{
    // вотчбл айди кстати и так 0, но ладно
    private static readonly string[] ignoreStatuses = { "#DOTA_RP_INIT", "#DOTA_RP_IDLE", "#DOTA_RP_SPECTATING", "#DOTA_RP_FINDING_MATCH", "#DOTA_RP_GAME_IN_PROGRESS_CUSTOM" };

    private readonly MatchTracker _matchTracker;
    private readonly TargetsContainer _targetsContainer;
    private readonly SteamService _steam;
    private readonly Databaser _databaser;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<RpMovement> _logger;

    private readonly List<SynchroRp> processingAccounts = new();

    private readonly TimeSpan updateDelayTime;

    private bool isRunning = false;

    public RpMovement(MatchTracker matchTracker, TargetsContainer targetsContainer, SteamService steam, Databaser databaser, IHostApplicationLifetime lifetime, ILogger<RpMovement> logger, IOptions<AppOptions> options)
    {
        _matchTracker = matchTracker;
        _targetsContainer = targetsContainer;
        _steam = steam;
        _databaser = databaser;
        _lifetime = lifetime;
        _logger = logger;
        updateDelayTime = options.Value.RpUpdateDelayTime;

        steam.DotaPersonaReceived += DotaPersonaReceived;
    }

    public void Init()
    {
        if (isRunning)
            return;

        _logger.LogInformation("Запускаем...");
        isRunning = true;

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
        lock (processingAccounts)
        {
            existingTasks = processingAccounts.Where(pa => pa.account == help.account || pa.watchableGameId == help.watchableGameId).Select(pa => pa.tsc.Task).ToArray();

            processingAccounts.Add(help);
        }

        await Task.WhenAll(existingTasks);

        try
        {
            await ProcessRichPresenceAsync(target, rpInfo);
        }
        finally
        {
            tcs.SetResult();
            lock (processingAccounts)
            {
                processingAccounts.Remove(help);
            }
        }
    }

    private async Task LoopAsync()
    {
        while (isRunning && !_lifetime.ApplicationStopping.IsCancellationRequested)
        {
            if (!_steam.client.IsConnected || !_steam.LoggedIn)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), _lifetime.ApplicationStopping);
                }
                catch { return; }
                continue;
            }

            AccountModel[] targets = _targetsContainer.GetTargets();
            if (targets.Length == 0)
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
                SteamDota.RichPresenceInfoCallback rp_response = await _steam.dota.RequestRichPresence(targets.Select(t => t.SteamID).ToArray());

                foreach (AccountModel target in targets)
                {
                    DotaRichPresenceInfo? rpInfo = rp_response.response.rich_presence
                    .Where(rp => rp.steamid_user == target.SteamID)
                    .Select(rp => rp.rich_presence_kv.Length > 0 ? new DotaRichPresenceInfo(rp.rich_presence_kv) : null)
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
                await Task.Delay(updateDelayTime, _lifetime.ApplicationStopping);
            }
            catch { return; }
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

        if (rpInfo != null && rpInfo.watchableGameId != null && rpInfo.watchableGameId != 0 && !ignoreStatuses.Contains(rpInfo.status))
        {
            // Игрок находится в игре, за которой мы хотим следить.
            currentMatch = _matchTracker.FindLiveMatchByLobbyId(rpInfo.watchableGameId.Value);

            if (currentMatch != null)
            {
                // Этот матч уже есть, всё в поряде чоколаде.
                if (!currentMatch.playing.Contains(target))
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
                    if (!currentMatch.playing.Contains(target))
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

                    currentMatch = new TrackedMatch(match, false);
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
            oldMatch.playing.Remove(target);

            if (oldMatch.playing.Count == 0)
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

        bool exist = tracked.parties.Any(p => p.Contains(accountId));
        if (exist)
            return;

        tracked.parties.Add(party_Members);
    }

    private Task UpdateMatchRpStatusAsync(TrackedMatch tracked, DotaRichPresenceInfo rpInfo, bool updateDb)
    {
        // На данный момент я не уврен, что возможно получить матч без этой информации.
        // Поэтому обновления статуса уже найденного матча должно не работать никогда.
        // Не знаю, зачем я это добавил.
        // TODO xdd?
        // Имортал драфт не парсится, приходит нулл.
        if (tracked.match.RichPresenceLobbyType != null)
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
            _logger.LogError(e, $"{nameof(UpdateMatchRpStatusAsync)} Не удалось пропарсить рп ({{status}})", rpInfo.status);
            return Task.CompletedTask;
        }

        if (updateDb)
            return _databaser.UpdateMatchAsync(tracked.match, () => tracked.match.RichPresenceLobbyType = rpStatus);

        return Task.CompletedTask;
    }
}
