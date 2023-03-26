using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using Dota2Dispenser.Shared.Models;
using Microsoft.EntityFrameworkCore;

namespace Dota2Dispenser.Database.Models;

[Index(nameof(SteamID), IsUnique = false)]
public class AccountModel
{
    [Key]
    /// <summary>
    /// SteamID64
    /// </summary>
    public ulong SteamID { get; set; }

    public string? Note { get; set; }

    /// <summary>
    /// UTC
    /// </summary>
    public DateTime DateAdded { get; set; }

    public ICollection<RequestModel> Requests { get; set; }

    public AccountModel() { }
    public AccountModel(ulong steamID, string? note, DateTime dateAdded)
    {
        SteamID = steamID;
        Note = note;
        DateAdded = dateAdded;
    }
}
