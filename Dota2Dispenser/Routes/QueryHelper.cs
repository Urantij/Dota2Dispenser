using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Primitives;

namespace Dota2Dispenser.Routes;

public static class QueryHelper
{
    public static string? GetString(this IQueryCollection collection, string key)
    {
        if (collection.TryGetValue(key, out StringValues values))
        {
            return values.FirstOrDefault();
        }
        return null;
    }

    public static ulong? GetUlong(this IQueryCollection collection, string key)
    {
        if (collection.TryGetValue(key, out StringValues values))
        {
            if (ulong.TryParse(values.FirstOrDefault(), out ulong result))
                return result;
        }
        return null;
    }

    public static int? GetInt(this IQueryCollection collection, string key)
    {
        if (collection.TryGetValue(key, out StringValues values))
        {
            if (int.TryParse(values.FirstOrDefault(), out int result))
                return result;
        }
        return null;
    }

    public static DateTimeOffset? GetDateTime(this IQueryCollection collection, string key)
    {
        if (collection.TryGetValue(key, out StringValues values))
        {
            if (long.TryParse(values.FirstOrDefault(), out long result))
                return DateTimeOffset.FromUnixTimeSeconds(result);
        }
        return null;
    }
}
