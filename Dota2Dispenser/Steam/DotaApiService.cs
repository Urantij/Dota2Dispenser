using Microsoft.Extensions.Options;
using SteamKitDota2.Web;

namespace Dota2Dispenser.Steam;

public class DotaApiService
{
    public DotaApi Api { get; }

    public DotaApiService(IOptions<AppOptions> options)
    {
        Api = new DotaApi(options.Value.ApiKey);
    }
}