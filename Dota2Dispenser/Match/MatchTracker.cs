using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dota2Dispenser.Database;
using Dota2Dispenser.Database.Models;
using Dota2Dispenser.Person;
using Dota2Dispenser.Shared.Consts;
using Microsoft.Extensions.Options;

namespace Dota2Dispenser.Match;

/// <summary>
/// Следит за играми в процессе.
/// Значит. Когда определено, что есть матч от чела, за которым мы следит, они отправляются сюда.
/// Затем такой алгоритм происходит.
/// Рич презенс (который изначально и обнаружил матч) делает проверки чела.
/// Если чел в той же игре, игнорируем. Если нет, отправить его в веб апи. У веб апи лимит колов 100к в день, то есть хоть раз в секунду можно запрашивать.
/// </summary>
public class MatchTracker
{
    private readonly TargetsContainer _targetsContainer;
    private readonly Databaser _databaser;
    private readonly ILogger<MatchTracker> _logger;

    /// <summary>
    /// Если матч убили очень рано, то, скорее всего, это додж до пика.
    /// В случае чего будет 2 матча в базе, всё равно.
    /// </summary>
    readonly TimeSpan earlyAbandonTime;

    /// <summary>
    /// Матчи, которые прямо сейчас идут, и ждём, когда челы вылетят из них.
    /// Лив матчи вроде из одного треда трогаются, но раз я локаю дед, то буду и эти.
    /// </summary>
    readonly List<TrackedMatch> liveMatches = new();

    /// <summary>
    /// Матчи, которые вроде как скорее всего закончились. Опрашиваем их через веб апи.
    /// Кто влияет: апдейтер кладёт матчи, апдейтер убирает (ресуректид), веб чекер берёт и убирает.
    /// </summary>
    readonly List<TrackedMatch> deadMatches = new();

    public MatchTracker(TargetsContainer targetsContainer, Databaser databaser, ILogger<MatchTracker> logger, IOptions<AppOptions> options)
    {
        this._targetsContainer = targetsContainer;
        this._databaser = databaser;
        this._logger = logger;
        this.earlyAbandonTime = options.Value.EarlyAbandonTime;

        _targetsContainer.TargetRemoved += UntrackAccount;
    }

    public async Task InitAsync()
    {
        MatchModel[] unfinished = await _databaser.GetUnfinishedMatchesAsync();

        // TODO можно убрать матчи, где больше нет отслеживаемых челов
        var games = unfinished
        .Select(m => new TrackedMatch(m, m.Players?.All(p => p.HeroId != 0) == true))
        .ToArray();

        deadMatches.AddRange(games);
    }

    public TrackedMatch[] GetDeadMatchesArray()
    {
        lock (deadMatches)
        {
            return deadMatches.ToArray();
        }
    }

    /// <summary>
    /// Матчи, в которых нет <see cref="MatchModel.TvInfo"/> или <see cref="TrackedMatch.gotAllHeroes"/> false
    /// </summary>
    /// <returns></returns>
    public TrackedMatch[] GetLiveMatchesForSourceTv()
    {
        lock (liveMatches)
        {
            return liveMatches.Where(l => l.match.TvInfo == null || !l.gotAllHeroes).ToArray();
        }
    }

    /// <summary>
    /// Возвращает последний живой матч, связанный с аккаунтом.
    /// </summary>
    /// <returns></returns>
    public TrackedMatch? GetLastMatchByAccount(AccountModel account)
    {
        lock (liveMatches)
        {
            // Ласт, потому что новые матчи добавляются в конец.
            // Предположим, играют 2 таргета в 1 матче.
            // Один таргет ливает и идёт некст.
            // Старый матч останется лайв из-за таргета в нём. и оба матча будут содержать новую цель.
            // Но новый матч будет в конце списка.
            return liveMatches.LastOrDefault(match => match.playing.Contains(account));
        }
    }

    public TrackedMatch? FindLiveMatchByLobbyId(ulong id)
    {
        lock (liveMatches)
        {
            return liveMatches.FirstOrDefault(m => m.match.WatchableGameId == id);
        }
    }

    public TrackedMatch? FindDeadMatchByLobbyId(ulong id)
    {
        lock (deadMatches)
        {
            return deadMatches.FirstOrDefault(m => m.match.WatchableGameId == id);
        }
    }

    internal void ResurrectMatch(TrackedMatch tracked)
    {
        _logger.LogInformation("Возрождаем матч {matchId} ({note})", tracked.match.Id, tracked.CreateNote());

        lock (deadMatches)
        {
            if (!deadMatches.Remove(tracked))
            {
                // Уже убрали из мёртвых.
                return;
            }
        }

        lock (liveMatches)
        {
            liveMatches.Add(tracked);
        }
    }

    internal async Task KillMatchAsync(TrackedMatch tracked)
    {
        _logger.LogInformation("Убиваем матч {matchId} ({note})", tracked.match.Id, tracked.CreateNote());

        TimeSpan passed = DateTime.UtcNow - tracked.match.GameDate;

        if (passed > earlyAbandonTime)
        {
            lock (liveMatches)
            {
                liveMatches.Remove(tracked);
            }

            lock (deadMatches)
            {
                deadMatches.Add(tracked);
            }
        }
        else
        {
            lock (liveMatches)
            {
                liveMatches.Remove(tracked);
            }

            await _databaser.UpdateMatchAsync(tracked.match, () => tracked.match.MatchResult = MatchResult.EarlyLeave);

            _logger.LogInformation("Ранний лив {matchId} ({note})", tracked.match.Id, tracked.CreateNote());
        }
    }

    internal void AddMatch(TrackedMatch tracked)
    {
        _logger.LogInformation("Добавляем матчи {matchId} ({note})", tracked.match.Id, tracked.CreateNote());

        lock (liveMatches)
        {
            liveMatches.Add(tracked);
        }
    }

    internal void AddMatches(List<TrackedMatch> matchesToAdd)
    {
        string text = string.Join("; ", matchesToAdd.Select(m => $"{m.match.Id} ({m.CreateNote()})").ToArray());

        _logger.LogInformation("Добавляем матчи {text}", text);

        lock (liveMatches)
        {
            liveMatches.AddRange(matchesToAdd);
        }
    }

    internal void RemoveDeadMatch(TrackedMatch tracked)
    {
        _logger.LogDebug("Убираем матч {matchId} ({note})", tracked.match.Id, tracked.CreateNote());

        lock (deadMatches)
        {
            deadMatches.Remove(tracked);
        }
    }

    internal void UntrackAccount(AccountModel account)
    {
        List<TrackedMatch> toRemoveMatches = new();

        lock (liveMatches)
        {
            var changed = liveMatches
            .Where(l => l.playing.Contains(account))
            .ToArray();

            foreach (var ch in changed)
            {
                ch.playing.Remove(account);
                if (ch.playing.Count == 0)
                {
                    liveMatches.Remove(ch);
                    toRemoveMatches.Add(ch);
                }
            }
        }

        // TODO Проверить, есть ли тут микро окно для создания второго матча при поиске.

        lock (deadMatches)
        {
            // Изначально я хотел следить, нужен ли этот матч вообще кому то, и удалять, если нет, но я устал.
            deadMatches.AddRange(toRemoveMatches);
        }
    }
}
