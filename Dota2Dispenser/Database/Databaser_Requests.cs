using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dota2Dispenser.Database.Models;
using Microsoft.EntityFrameworkCore;

namespace Dota2Dispenser.Database;

public partial class Databaser
{
    public async Task<bool> CheckRequestAsync(ulong steamId, string identity)
    {
        using var context = await _contextFactory.CreateDbContextAsync();

        return await context.Requests.AnyAsync(r => r.Identity == identity && r.AccountId == steamId);
    }

    public async Task AddRequestAsync(RequestModel request)
    {
        using var context = await _contextFactory.CreateDbContextAsync();

        context.Requests.Add(request);

        await context.SaveChangesAsync();
    }

    public async Task<bool> RemoveRequestAsync(ulong steamId, string identity)
    {
        using var context = await _contextFactory.CreateDbContextAsync();

        return await context.Requests
        .Where(r => r.Identity == identity && r.AccountId == steamId)
        .ExecuteDeleteAsync() > 0;
    }

    public async Task<RequestModel[]> ClearAllRequestsAsync(string identity)
    {
        using var context = await _contextFactory.CreateDbContextAsync();

        var requests = await context.Requests
        .Where(r => r.Identity == identity)
        .AsNoTracking()
        .ToArrayAsync();

        await context.Requests
        .Where(r => r.Identity == identity)
        .ExecuteDeleteAsync();

        return requests;
    }
}
