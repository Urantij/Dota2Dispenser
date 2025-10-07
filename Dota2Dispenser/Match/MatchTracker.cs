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
    private readonly TimeSpan _earlyAbandonTime;

    /// <summary>
    /// Матчи, которые прямо сейчас идут, и ждём, когда челы вылетят из них.
    /// Лив матчи вроде из одного треда трогаются, но раз я локаю дед, то буду и эти.
    /// </summary>
    private readonly List<TrackedMatch> _liveMatches = new();

    /// <summary>
    /// Матчи, которые вроде как скорее всего закончились. Опрашиваем их через веб апи.
    /// Кто влияет: апдейтер кладёт матчи, апдейтер убирает (ресуректид), веб чекер берёт и убирает.
    /// </summary>
    private readonly List<TrackedMatch> _deadMatches = new();

    public MatchTracker(TargetsContainer targetsContainer, Databaser databaser, ILogger<MatchTracker> logger,
        IOptions<AppOptions> options)
    {
        this._targetsContainer = targetsContainer;
        this._databaser = databaser;
        this._logger = logger;
        this._earlyAbandonTime = options.Value.EarlyAbandonTime;

        _targetsContainer.TargetRemoved += UntrackAccount;
    }

    public async Task InitAsync()
    {
        MatchModel[] unfinished = await _databaser.GetUnfinishedMatchesAsync();

        // TODO можно убрать матчи, где больше нет отслеживаемых челов
        var games = unfinished
            .Select(m => new TrackedMatch(m, m.Players?.All(p => p.HeroId != 0) == true, DateTimeOffset.UtcNow))
            .ToArray();

        _deadMatches.AddRange(games);
    }

    public TrackedMatch[] GetDeadMatchesArray()
    {
        lock (_deadMatches)
        {
            return _deadMatches.ToArray();
        }
    }

    public TrackedMatch[] GetDeadMatchesMODS(Func<List<TrackedMatch>, IEnumerable<TrackedMatch>> mods)
    {
        lock (_deadMatches)
        {
            return mods(_deadMatches).ToArray();
        }
    }

    /// <summary>
    /// Матчи, в которых нет <see cref="MatchModel.TvInfo"/> или <see cref="TrackedMatch.GotAllHeroes"/> false
    /// </summary>
    /// <returns></returns>
    public TrackedMatch[] GetLiveMatchesForSourceTv()
    {
        lock (_liveMatches)
        {
            return _liveMatches.Where(l => l.Match.TvInfo == null || !l.GotAllHeroes).ToArray();
        }
    }

    /// <summary>
    /// Возвращает последний живой матч, связанный с аккаунтом.
    /// </summary>
    /// <returns></returns>
    public TrackedMatch? GetLastMatchByAccount(AccountModel account)
    {
        lock (_liveMatches)
        {
            // Ласт, потому что новые матчи добавляются в конец.
            // Предположим, играют 2 таргета в 1 матче.
            // Один таргет ливает и идёт некст.
            // Старый матч останется лайв из-за таргета в нём. и оба матча будут содержать новую цель.
            // Но новый матч будет в конце списка.
            return _liveMatches.LastOrDefault(match => match.Playing.Contains(account));
        }
    }

    public TrackedMatch? FindLiveMatchByLobbyId(ulong id)
    {
        lock (_liveMatches)
        {
            return _liveMatches.FirstOrDefault(m => m.Match.WatchableGameId == id);
        }
    }

    public TrackedMatch? FindDeadMatchByLobbyId(ulong id)
    {
        lock (_deadMatches)
        {
            return _deadMatches.FirstOrDefault(m => m.Match.WatchableGameId == id);
        }
    }

    internal void ResurrectMatch(TrackedMatch tracked)
    {
        _logger.LogInformation("Возрождаем матч {matchId} ({note})", tracked.Match.Id, tracked.CreateNote());

        lock (_deadMatches)
        {
            if (!_deadMatches.Remove(tracked))
            {
                // Уже убрали из мёртвых.
                return;
            }

            tracked.DeathDate = null;
        }

        lock (_liveMatches)
        {
            _liveMatches.Add(tracked);
        }
    }

    internal async Task KillMatchAsync(TrackedMatch tracked)
    {
        _logger.LogInformation("Убиваем матч {matchId} ({note})", tracked.Match.Id, tracked.CreateNote());

        TimeSpan passed = DateTime.UtcNow - tracked.Match.GameDate;

        if (passed > _earlyAbandonTime)
        {
            lock (_liveMatches)
            {
                _liveMatches.Remove(tracked);
            }

            lock (_deadMatches)
            {
                _deadMatches.Add(tracked);

                tracked.DeathDate = DateTimeOffset.UtcNow;
            }
        }
        else
        {
            lock (_liveMatches)
            {
                _liveMatches.Remove(tracked);
            }

            await _databaser.UpdateMatchAsync(tracked.Match, () => tracked.Match.MatchResult = MatchResult.EarlyLeave);

            _logger.LogInformation("Ранний лив {matchId} ({note})", tracked.Match.Id, tracked.CreateNote());
        }
    }

    internal void AddMatch(TrackedMatch tracked)
    {
        _logger.LogInformation("Добавляем матчи {matchId} ({note})", tracked.Match.Id, tracked.CreateNote());

        lock (_liveMatches)
        {
            _liveMatches.Add(tracked);
        }
    }

    internal void AddMatches(List<TrackedMatch> matchesToAdd)
    {
        string text = string.Join("; ", matchesToAdd.Select(m => $"{m.Match.Id} ({m.CreateNote()})").ToArray());

        _logger.LogInformation("Добавляем матчи {text}", text);

        lock (_liveMatches)
        {
            _liveMatches.AddRange(matchesToAdd);
        }
    }

    internal void RemoveDeadMatch(TrackedMatch tracked)
    {
        _logger.LogDebug("Убираем матч {matchId} ({note})", tracked.Match.Id, tracked.CreateNote());

        lock (_deadMatches)
        {
            _deadMatches.Remove(tracked);
        }
    }

    internal void UntrackAccount(AccountModel account)
    {
        List<TrackedMatch> toRemoveMatches = new();

        lock (_liveMatches)
        {
            var changed = _liveMatches
                .Where(l => l.Playing.Contains(account))
                .ToArray();

            foreach (var ch in changed)
            {
                ch.Playing.Remove(account);
                if (ch.Playing.Count == 0)
                {
                    _liveMatches.Remove(ch);
                    toRemoveMatches.Add(ch);
                }
            }
        }

        // TODO Проверить, есть ли тут микро окно для создания второго матча при поиске.

        lock (_deadMatches)
        {
            // Изначально я хотел следить, нужен ли этот матч вообще кому то, и удалять, если нет, но я устал.
            _deadMatches.AddRange(toRemoveMatches);
        }
    }
}