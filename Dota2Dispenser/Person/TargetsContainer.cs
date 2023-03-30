using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dota2Dispenser.Database;
using Dota2Dispenser.Database.Models;

namespace Dota2Dispenser.Person;

/// <summary>
/// Просто хранит тех, за кем нужно следить.
/// </summary>
public class TargetsContainer
{
    readonly object locker = new();

    readonly List<AccountModel> targets = new();

    private readonly Databaser _databaser;

    public event Action<AccountModel>? TargetRemoved;

    public TargetsContainer(Databaser databaser)
    {
        this._databaser = databaser;
    }

    public async Task InitAsync()
    {
        targets.AddRange(await _databaser.GetAccountsAsync());
    }

    public bool Contains(ulong target)
    {
        lock (locker)
        {
            return targets.Any(t => t.SteamID == target);
        }
    }

    public async Task<bool> AddAsync(ulong target, string identity, string? note)
    {
        if (await _databaser.CheckRequestAsync(target, identity))
            return false;

        AccountModel? account;
        lock (locker)
        {
            account = targets.FirstOrDefault(t => t.SteamID == target);
        }

        if (account == null)
        {
            account = await _databaser.AddAccountAsync(target, note, DateTime.UtcNow);

            lock (locker)
            {
                targets.Add(account);
            }

            // TODO трек матчей, куда случайно попался новый таргет. это очень тупо, но забавно
        }

        RequestModel request = new(identity, note, DateTime.UtcNow, account.SteamID);
        await _databaser.AddRequestAsync(request);

        return true;
    }

    public async Task<bool> RemoveAsync(ulong targetId, string identity)
    {
        if (!Contains(targetId))
        {
            return false;
        }

        bool requestRemoved = await _databaser.RemoveRequestAsync(targetId, identity);

        bool removed = await _databaser.RemoveUnlinkedAccount(targetId);
        if (!removed)
            return requestRemoved;

        AccountModel? account;
        lock (locker)
        {
            account = targets.FirstOrDefault(t => t.SteamID == targetId);

            if (account == null)
                return requestRemoved;

            targets.Remove(account);
        }

        TargetRemoved?.Invoke(account);

        return requestRemoved;
    }

    public AccountModel[] GetTargets()
    {
        lock (locker)
        {
            return targets.ToArray();
        }
    }

    public AccountModel? FindAccount(ulong targetId)
    {
        lock (locker)
        {
            return targets.FirstOrDefault(t => t.SteamID == targetId);
        }
    }
}
