using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Threading.Tasks;
using Dota2Dispenser.Shared.Consts;
using Microsoft.EntityFrameworkCore;

namespace Dota2Dispenser.Database.Models;

[Index(nameof(SteamId), IsUnique = false)]
public class PlayerModel
{
    [Key]
    public int Id { get; set; }

    [ForeignKey(nameof(Match))]
    /// <summary>
    /// Клюс матча в бд, а не айди сам.
    /// </summary>
    public int MatchId { get; set; }
    public MatchModel Match { get; set; }

    [Required]
    public ulong SteamId { get; set; }
    /// <summary>
    /// 0, если инфы нет. герои становятся доступны через 2 минуты как их пикнут (дилей)
    /// </summary>
    public uint HeroId { get; set; }
    /// <summary>
    /// Становится доступен, когда игра успешно завершается
    /// http://sharonkuo.me/dota2/matchdetails.html
    /// </summary>
    public int? LeaverStatus { get; set; }

    /// <summary>
    /// Если пати не было, значение будет -1.
    /// Если инфа появилась слишком поздно, -2. (Не было шанса узнать)
    /// </summary>
    public int? PartyIndex { get; set; }

    /// <summary>
    /// Становится доступен, когда игра успешно завершается.
    /// </summary>
    public int? PlayerSlot { get; set; }

    public PlayerModel() { }
    public PlayerModel(int matchId, ulong steamId, uint heroId)
    {
        MatchId = matchId;
        SteamId = steamId;
        HeroId = heroId;
    }

    public static bool IsAbandonStatus(int status)
        => status switch
        {
            2 or 3 or 4 => true,
            _ => false,
        };
}
