using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Dota2Dispenser.Database.Models;

[Owned]
/// <summary>
/// Доступно только после завершения игры.
/// </summary>
public class DetailsMatchInfo
{
    public bool? RadiantWin { get; set; }

    public TimeSpan Duration { get; set; }

    public DetailsMatchInfo() { }
    public DetailsMatchInfo(bool? radiantWin, TimeSpan duration)
    {
        RadiantWin = radiantWin;
        Duration = duration;
    }
}
