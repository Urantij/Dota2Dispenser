using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Dota2Dispenser.Database;

public partial class Databaser
{
    private readonly IDbContextFactory<DotaContext> _contextFactory;

    public Databaser(IDbContextFactory<DotaContext> contextFactory)
    {
        this._contextFactory = contextFactory;
    }

    public async Task InitAsync()
    {
        using var context = await _contextFactory.CreateDbContextAsync();

        await context.Database.MigrateAsync();
    }
}
