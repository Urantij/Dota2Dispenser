using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dota2Dispenser.Database;
using Dota2Dispenser.Shared.Consts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dota2Dispenser.Match;

public class AgeRestricter
{
    private readonly MatchTracker _matchTracker;
    private readonly Databaser _databaser;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger _logger;
    private readonly TimeSpan ageLimit;
    private readonly TimeSpan checkDelay;

    public AgeRestricter(MatchTracker matchTracker, Databaser databaser, IHostApplicationLifetime lifetime, ILogger<AgeRestricter> logger, IOptions<AppOptions> options)
    {
        this._matchTracker = matchTracker;
        this._databaser = databaser;
        this._lifetime = lifetime;
        this._logger = logger;
        ageLimit = options.Value.TimeToConfirmBroken;
        checkDelay = options.Value.AgeRestricterCheckDelay;
    }

    public void Init()
    {
        _logger.LogInformation("Запускаемся...");

        Task.Run(LoopAsync);
    }

    async Task LoopAsync()
    {
        while (!_lifetime.ApplicationStopping.IsCancellationRequested)
        {
            try
            {
                var deadMatches = _matchTracker.GetDeadMatchesArray();

                // В теории тут гонка состояний, потому что мы трогаем матчи и тут и в вебчекере.
                // Но обновление одной сущности не должно вызвать проблем.
                // Плюс вероятность пересечения мала очень.

                DateTime utcNow = DateTime.UtcNow;
                foreach (var tracked in deadMatches)
                {
                    TimeSpan passed = utcNow - tracked.match.GameDate;
                    if (passed < ageLimit)
                        continue;

                    // Прошло много времени, пора прощаться.
                    await RemoveMatchAsync(tracked, "timeout");
                }

                try
                {
                    await Task.Delay(checkDelay, _lifetime.ApplicationStopping);
                }
                catch { return; }
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"{nameof(LoopAsync)} Exception");
                try
                {
                    await Task.Delay(checkDelay, _lifetime.ApplicationStopping);
                }
                catch { return; }
            }
        }
    }

    async Task RemoveMatchAsync(TrackedMatch tracked, string reason)
    {
        _matchTracker.RemoveDeadMatch(tracked);
        await _databaser.UpdateMatchAsync(tracked.match, () => tracked.match.MatchResult = MatchResult.Broken);

        _logger.LogInformation("Сломался {matchId} ({note}) {reason}", tracked.match.Id, tracked.CreateNote(), reason);
    }
}
