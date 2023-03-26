using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using SteamKitDota2.Web;

namespace Dota2Dispenser.Steam;

public class DotaApiService
{
    public readonly DotaApi api;

    public DotaApiService(IOptions<AppOptions> options)
    {
        api = new DotaApi(options.Value.ApiKey);
    }
}
