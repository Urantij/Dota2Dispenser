using System.Text.Json;

namespace Dota2Dispenser.NoSteam;

// Спасибо велв, что сломали свой апи ендпоинт в мае, и сейчас, в конце июля, он до сих пор сломан.

public class OpenDotaService
{
    private readonly HttpClient _client;

    // Рейт лимит 2к колов в день. Раз в минуту делать это 1440.
    private readonly TimeSpan _cooldown = TimeSpan.FromMinutes(1);
    private DateTimeOffset? _lastCall = null;

    public OpenDotaService()
    {
        _client = new HttpClient();
    }

    public async Task<OpenMatch> DoAsync(ulong matchId,
        CancellationToken cancellationToken = default)
    {
        while (_lastCall != null)
        {
            TimeSpan timePassed = DateTimeOffset.UtcNow - _lastCall.Value;

            if (timePassed < _cooldown)
            {
                TimeSpan sleepTime = (_cooldown - timePassed) + TimeSpan.FromSeconds(1);

                await Task.Delay(sleepTime, cancellationToken);
            }
            else
            {
                break;
            }
        }

        _lastCall = DateTimeOffset.UtcNow;
        using HttpResponseMessage responseMessage = await _client.GetAsync(
            $"https://api.opendota.com/api/matches/{matchId}", HttpCompletionOption.ResponseContentRead,
            cancellationToken: cancellationToken);

        string? content = await responseMessage.Content.ReadAsStringAsync(cancellationToken);

        if (content == "{\"error\":\"Not Found\"}")
        {
            throw new MatchNotFoundException();
        }

        OpenMatch? result = JsonSerializer.Deserialize<OpenMatch>(content);

        if (result == null)
        {
            throw new Exception($"Матч не пришол. ({content})");
        }

        return result;
    }
}