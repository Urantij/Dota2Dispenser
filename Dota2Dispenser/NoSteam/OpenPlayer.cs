using System.Text.Json.Serialization;

namespace Dota2Dispenser.NoSteam;

public class OpenPlayer
{
    [JsonPropertyName("account_id")] public ulong? AccountId { get; init; }
    [JsonPropertyName("player_slot")] public int? PlayerSlot { get; init; }
    [JsonPropertyName("isRadiant")] public bool? IsRadiant { get; init; }
    [JsonPropertyName("hero_id")] public uint HeroId { get; init; }
    [JsonPropertyName("leaver_status")] public int? LeaverStatus { get; init; }
}