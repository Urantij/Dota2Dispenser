using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;

namespace Dota2Dispenser;

public class AppOptions
{
    public const string Key = "Options";

    [Required]
    public required string ApiKey { get; set; }

    public TimeSpan EarlyAbandonTime { get; set; } = TimeSpan.FromMinutes(1);

    public TimeSpan WebConfirmerUpdateDelayTime { get; set; } = TimeSpan.FromSeconds(5);
    public TimeSpan TimeToConfirmBroken { get; set; } = TimeSpan.FromHours(2);

    public TimeSpan UpdaterUpdateDelayTime { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan AgeRestricterCheckDelay { get; set; } = TimeSpan.FromMinutes(1);

    [Required]
    public required string SteamUsername { get; set; }
    [Required]
    public required string SteamPassword { get; set; }
}
