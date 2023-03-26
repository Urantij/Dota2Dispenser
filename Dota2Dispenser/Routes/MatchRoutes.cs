using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AutoMapper;
using AutoMapper.QueryableExtensions;
using Dota2Dispenser.Database;
using Dota2Dispenser.Shared.Consts;
using Microsoft.EntityFrameworkCore;

namespace Dota2Dispenser.Routes;

public static class MatchRoutes
{
    public static async Task<IResult> GetAsync(HttpContext httpContext, DotaContext dbContext, IMapper mapper)
    {
        int? dispenserMatchIdParam = httpContext.Request.Query.GetInt(Dota2DispenserParams.dispenserMatchIdFilter);
        ulong? matchIdParam = httpContext.Request.Query.GetUlong(Dota2DispenserParams.matchIdFilter);
        ulong? steamIdParam = httpContext.Request.Query.GetUlong(Dota2DispenserParams.steamIdFilter);
        DateTime? datetimeParam = httpContext.Request.Query.GetDateTime(Dota2DispenserParams.afterDateTimeFilter)?.UtcDateTime;

        var filterQuery = dbContext.Matches.AsQueryable();

        if (dispenserMatchIdParam != null)
        {
            filterQuery = filterQuery.Where(m => m.Id == dispenserMatchIdParam.Value);
        }
        if (matchIdParam != null)
        {
            filterQuery = filterQuery.Where(m => m.TvInfo!.MatchId == matchIdParam.Value);
        }
        if (steamIdParam != null)
        {
            filterQuery = filterQuery.Where(m => m.Players!.Any(p => p.SteamId == steamIdParam.Value));
        }
        if (datetimeParam != null)
        {
            filterQuery = filterQuery.Where(m => m.GameDate > datetimeParam);
        }

        var result = await filterQuery
        .Take(20)
        .OrderByDescending(m => m.Id)
        .ProjectTo<Shared.Models.MatchModel>(mapper.ConfigurationProvider)
        .ToArrayAsync();

        return TypedResults.Ok(result);
    }
}
