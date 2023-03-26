using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dota2Dispenser.Database.Models;
using Dota2Dispenser.Shared.Consts;
using Microsoft.EntityFrameworkCore;

namespace Dota2Dispenser.Database;

public partial class Databaser
{
    public async Task<MatchModel[]> GetUnfinishedMatchesAsync()
    {
        using var context = await _contextFactory.CreateDbContextAsync();

        return await context.Matches
        .Where(g => g.MatchResult == MatchResult.None)
        .Include(p => p.Players)
        .ToArrayAsync();
    }

    public async Task AddMatchAsync(MatchModel match)
    {
        using var context = await _contextFactory.CreateDbContextAsync();

        context.Matches.Add(match);
        await context.SaveChangesAsync();
    }

    public async Task AddMatchesAsync(IEnumerable<MatchModel> matches)
    {
        using var context = await _contextFactory.CreateDbContextAsync();

        context.Matches.AddRange(matches);
        await context.SaveChangesAsync();
    }

    public async Task UpdateMatchAsync(MatchModel match, Action update)
    {
        using var context = await _contextFactory.CreateDbContextAsync();

        context.Matches.Attach(match);

        update();

        await context.SaveChangesAsync();
    }
}
