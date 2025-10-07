using Dota2Dispenser.Database.Models;
using Microsoft.EntityFrameworkCore;

namespace Dota2Dispenser.Database;

public partial class Databaser
{
    public async Task<AccountModel> AddAccountAsync(ulong steamId, string? note, DateTime date)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        AccountModel db = new(steamId, note, date);

        context.Accounts.Add(db);
        await context.SaveChangesAsync();

        return db;
    }

    public async Task<bool> RemoveUnlinkedAccount(ulong steamId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        return await context.Accounts
            .Where(a => a.SteamID == steamId)
            .Where(a => a.Requests.Count == 0)
            .ExecuteDeleteAsync() > 0;
    }

    public async Task<AccountModel[]> GetAccountsAsync()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        return await context.Accounts.ToArrayAsync();
    }
}