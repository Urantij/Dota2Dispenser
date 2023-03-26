using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dota2Dispenser.Database.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Dota2Dispenser.Database;

public class DotaContext : DbContext
{
#nullable disable
    public DbSet<AccountModel> Accounts { get; set; }
    public DbSet<RequestModel> Requests { get; set; }

    public DbSet<MatchModel> Matches { get; set; }
    public DbSet<PlayerModel> Players { get; set; }
#nullable restore

    public DotaContext(DbContextOptions<DotaContext> options)
        : base(options)
    {

    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);

        configurationBuilder.Properties<DateTime>()
                            .HaveConversion<DateTimeToBinaryConverter>();

        configurationBuilder.Properties<DateTime?>()
                            .HaveConversion<DateTimeToBinaryConverter>();
    }
}
