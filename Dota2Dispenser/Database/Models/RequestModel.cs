using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Threading.Tasks;

namespace Dota2Dispenser.Database.Models;

public class RequestModel
{
    [Key]
    public int Id { get; set; }

    public string Identity { get; set; }

    public string? Note { get; set; }

    public DateTime DateAdded { get; set; }

    [ForeignKey(nameof(Account))]
    public ulong AccountId { get; set; }
    public AccountModel Account { get; set; }

    public RequestModel() { }
    public RequestModel(string identity, string? note, DateTime dateAdded, ulong accountId)
    {
        Identity = identity;
        Note = note;
        DateAdded = dateAdded;
        AccountId = accountId;
    }
}
