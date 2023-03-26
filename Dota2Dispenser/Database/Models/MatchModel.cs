using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using Dota2Dispenser.Shared.Consts;
using Microsoft.EntityFrameworkCore;

namespace Dota2Dispenser.Database.Models;

public class MatchModel
{
    [Key]
    /// <summary>
    /// Айди в базе.
    /// </summary>
    public int Id { get; set; }

    [Required]
    public ulong WatchableGameId { get; set; }

    [Required]
    /// <summary>
    /// UTC. Если игра сломана или не закончилась, то дата очень примерная.
    /// </summary>
    public DateTime GameDate { get; set; }

    /// <summary>
    /// Тип лобби, полученный из RP. Работает только для обычных игр.
    /// </summary>
    public string? RichPresenceLobbyType { get; set; }

    public MatchResult MatchResult { get; set; }

    public SourceMatchInfo? TvInfo { get; set; }
    public DetailsMatchInfo? DetailsInfo { get; set; }

    /// <summary>
    /// Игроки появляются вместе с <see cref="TvInfo"/> или <see cref="DetailsInfo"/>
    /// </summary>
    public ICollection<PlayerModel>? Players { get; set; }

    public MatchModel() { }
    public MatchModel(ulong watchableGameId, DateTime gameDate)
    {
        WatchableGameId = watchableGameId;
        GameDate = gameDate;
    }
}
