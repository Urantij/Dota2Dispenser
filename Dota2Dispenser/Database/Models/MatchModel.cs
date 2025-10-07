using System.ComponentModel.DataAnnotations;
using Dota2Dispenser.Shared.Consts;

namespace Dota2Dispenser.Database.Models;

public class MatchModel
{
    /// <summary>
    /// Айди в базе.
    /// </summary>
    [Key]
    public int Id { get; set; }

    [Required] public ulong WatchableGameId { get; set; }

    /// <summary>
    /// UTC. Если игра сломана или не закончилась, то дата очень примерная.
    /// </summary>
    [Required]
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

    public MatchModel()
    {
    }

    public MatchModel(ulong watchableGameId, DateTime gameDate)
    {
        WatchableGameId = watchableGameId;
        GameDate = gameDate;
    }
}