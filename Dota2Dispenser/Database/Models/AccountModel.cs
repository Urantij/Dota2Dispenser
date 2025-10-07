using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Dota2Dispenser.Database.Models;

[Index(nameof(SteamID), IsUnique = false)]
public class AccountModel
{
    /// <summary>
    /// SteamID64
    /// </summary>
    [Key]
    public ulong SteamID { get; set; }

    public string? Note { get; set; }

    /// <summary>
    /// UTC
    /// </summary>
    public DateTime DateAdded { get; set; }

    public ICollection<RequestModel> Requests { get; set; }

    public AccountModel()
    {
    }

    public AccountModel(ulong steamId, string? note, DateTime dateAdded)
    {
        SteamID = steamId;
        Note = note;
        DateAdded = dateAdded;
    }
}