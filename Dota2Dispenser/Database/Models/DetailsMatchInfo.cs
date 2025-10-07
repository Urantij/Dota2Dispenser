using Microsoft.EntityFrameworkCore;

namespace Dota2Dispenser.Database.Models;

/// <summary>
/// Доступно только после завершения игры.
/// </summary>
[Owned]
public class DetailsMatchInfo
{
    public bool? RadiantWin { get; set; }

    public TimeSpan Duration { get; set; }

    public DetailsMatchInfo()
    {
    }

    public DetailsMatchInfo(bool? radiantWin, TimeSpan duration)
    {
        RadiantWin = radiantWin;
        Duration = duration;
    }
}