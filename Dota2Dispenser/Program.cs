using AutoMapper;
using Dota2Dispenser.Database;
using Dota2Dispenser.Match;
using Dota2Dispenser.Person;
using Dota2Dispenser.Routes;
using Dota2Dispenser.Steam;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Dota2Dispenser;

public class Program
{
    public static async Task Main(string[] appArgs)
    {
        AppDomain.CurrentDomain.UnhandledException += (sender, ex) =>
        {
            File.WriteAllText($"CRASH {DateTime.Now:yyyy.MM.dd HH:mm:ss}.txt", ex.ExceptionObject.ToString());
        };

        var builder = WebApplication.CreateBuilder(appArgs);
        builder.Logging.ClearProviders();
        builder.Services.AddLogging(b =>
        {
            b.AddSimpleConsole(c => c.TimestampFormat = "[HH:mm:ss] ");

#if DEBUG
            {
                b.SetMinimumLevel(LogLevel.Debug);
            }
#else
            {
                if (appArgs.Contains("--debug"))
                {
                    b.SetMinimumLevel(LogLevel.Debug);
                }
            }
#endif
        });

        builder.Services.AddDbContextFactory<DotaContext>(options =>
            options.UseSqlite(builder.Configuration.GetConnectionString("DotaContext")));

        builder.Services.AddOptions<AppOptions>()
            .Bind(builder.Configuration.GetSection(AppOptions.Key))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        builder.Services.AddSingleton<DotaApiService>();
        builder.Services.AddSingleton<Databaser>();
        builder.Services.AddSingleton<TargetsContainer>();
        builder.Services.AddSingleton<MatchTracker>();
        builder.Services.AddSingleton<WebConfirmer>();
        builder.Services.AddSingleton<RpMovement>();
        builder.Services.AddSingleton<SourceTvMovement>();
        builder.Services.AddSingleton<SteamService>();

        builder.Services.AddRouting(options =>
            options.ConstraintMap.Add("ulong", typeof(UlongRouteConstraint)));

        builder.Services.AddAutoMapper(config =>
        {
            config.CreateMap<Database.Models.SourceMatchInfo, Shared.Models.SourceMatchInfo>();
            config.CreateMap<Database.Models.DetailsMatchInfo, Shared.Models.DetailsMatchInfo>();
            config.CreateMap<Database.Models.PlayerModel, Shared.Models.PlayerModel>();
            config.CreateMap<Database.Models.MatchModel, Shared.Models.MatchModel>();
        });

        var app = builder.Build();

        app.Services.GetRequiredService<DotaApiService>();
        await app.Services.GetRequiredService<Databaser>().InitAsync();
        await app.Services.GetRequiredService<TargetsContainer>().InitAsync();
        await app.Services.GetRequiredService<MatchTracker>().InitAsync();
        app.Services.GetRequiredService<WebConfirmer>().Init();
        app.Services.GetRequiredService<RpMovement>().Init();
        app.Services.GetRequiredService<SourceTvMovement>().Init();
        app.Services.GetRequiredService<SteamService>().Init();

        AppDomain.CurrentDomain.ProcessExit += (sender, e) =>
        {
            SqliteConnection.ClearAllPools();
        };
        AppDomain.CurrentDomain.DomainUnload += (sender, e) =>
        {
            SqliteConnection.ClearAllPools();
        };

        app.MapGet("/jokerge", () => "Hello World!");

        app.MapDelete("/account/{id:ulong}", AccountRoutes.DeleteAsync);
        app.MapPost("/account", AccountRoutes.PostAsync);

        app.MapGet("/match", MatchRoutes.GetAsync);

        await app.RunAsync();
    }
}
