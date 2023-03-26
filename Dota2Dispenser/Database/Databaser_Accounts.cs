using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dota2Dispenser.Database.Models;
using Microsoft.EntityFrameworkCore;

namespace Dota2Dispenser.Database;

public partial class Databaser
{
    public async Task<AccountModel> AddAccountAsync(ulong steamID, string? note, DateTime date)
    {
        using var context = await _contextFactory.CreateDbContextAsync();

        AccountModel db = new(steamID, note, date);

        context.Accounts.Add(db);
        await context.SaveChangesAsync();

        return db;
    }

    public async Task<bool> RemoveUnlinkedAccount(ulong steamId)
    {
        using var context = await _contextFactory.CreateDbContextAsync();

        return await context.Accounts
        .Where(a => a.SteamID == steamId)
        .Where(a => a.Requests.Count == 0)
        .ExecuteDeleteAsync() > 0;
    }

    public async Task<AccountModel[]> GetAccountsAsync()
    {
        using var context = await _contextFactory.CreateDbContextAsync();

        return await context.Accounts.ToArrayAsync();
    }
}
