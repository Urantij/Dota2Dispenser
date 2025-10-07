using Dota2Dispenser.Database.Models;
using Dota2Dispenser.Shared.Consts;
using Microsoft.EntityFrameworkCore;

namespace Dota2Dispenser.Database;

public partial class Databaser
{
    public async Task<MatchModel[]> GetUnfinishedMatchesAsync()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        return await context.Matches
            .OrderBy(g => g.Id)
            .Where(g => g.MatchResult == MatchResult.None)
            .Include(p => p.Players)
            .ToArrayAsync();
    }

    public async Task AddMatchAsync(MatchModel match)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        context.Matches.Add(match);
        await context.SaveChangesAsync();
    }

    public async Task AddMatchesAsync(IEnumerable<MatchModel> matches)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        context.Matches.AddRange(matches);
        await context.SaveChangesAsync();
    }

    public async Task UpdateMatchAsync(MatchModel match, Action update)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        context.Matches.Attach(match);

        update();

        await context.SaveChangesAsync();
    }
}