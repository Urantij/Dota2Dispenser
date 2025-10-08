using System.ComponentModel.DataAnnotations;

namespace Dota2Dispenser;

public class AppOptions
{
    public const string Key = "Options";

    [Required] public required string ApiKey { get; set; }

    public TimeSpan EarlyAbandonTime { get; set; } = TimeSpan.FromMinutes(1);

    public TimeSpan WebConfirmerUpdateDelayTime { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Сколько времени нужно подождать после смерти матча, чтобы начать его трогать через апи
    /// </summary>
    public TimeSpan WebConfirmerDeathAddedTime { get; set; } = TimeSpan.FromSeconds(10);

    public TimeSpan InDotaCheckCooldown { get; set; } = TimeSpan.FromSeconds(60);

    public TimeSpan TimeToConfirmBroken { get; set; } = TimeSpan.FromHours(2);

    public TimeSpan RpUpdateDelayTime { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan SourceTvUpdateDelayTime { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan AgeRestricterCheckDelay { get; set; } = TimeSpan.FromSeconds(30);

    [Required] public required string SteamUsername { get; set; }
    [Required] public required string SteamPassword { get; set; }

    public bool? DontStartSteamClient { get; set; }
}