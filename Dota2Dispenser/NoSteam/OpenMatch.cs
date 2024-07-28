using System.Text.Json.Serialization;

namespace Dota2Dispenser.NoSteam;

// https://github.com/sominola/OpenDota-API/blob/main/OpenDotaApi/Api/Matches/Model/Match.cs
// Спиздил отсюда. Увырге, но их либа сыпет странной хуйнёй при попытке использовать. Разбираться как-то впадлу.
public class OpenMatch
{
    [JsonPropertyName("match_id")] public required ulong MatchId { get; init; }

    [JsonPropertyName("duration")] public required int Duration { get; init; }

    [JsonPropertyName("radiant_win")] public bool? RadiantWin { get; init; }

    [JsonPropertyName("start_time")] public required int StartTime { get; init; }

    [JsonPropertyName("players")] public required OpenPlayer[] Players { get; init; }
}