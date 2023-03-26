using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dota2Dispenser.Database.Models;

namespace Dota2Dispenser.Match;

public class TrackedMatch
{
    public readonly MatchModel match;

    /// <summary>
    /// Отслеживаемые челы, которые были замечены в матче.
    /// Везде идёт сравнение по ссылке объекта.
    /// </summary>
    public readonly List<AccountModel> wereSeen = new();

    /// <summary>
    /// Отслеживаемые челы, которые всё ещё находятся в матче, а не вышли из него.
    /// Везде идёт сравнение по ссылке объекта.
    /// </summary>
    public readonly List<AccountModel> playing = new();

    /// <summary>
    /// В теории пати могут изменяться прямо во время игры, и это очень нестабильная тема.
    /// Но она при этом очень ненужная. Самая верная инфа - первая.
    /// </summary>
    public List<ulong[]> parties = new();

    /// <summary>
    /// Игра может найтись до того, как будут все пики. И пока не все герои найдены, их следует трогать.
    /// </summary>
    public bool gotAllHeroes = false;

    public TrackedMatch(MatchModel match, bool gotAllHeroes)
    {
        this.match = match;
        this.gotAllHeroes = gotAllHeroes;
    }

    public void AddPlayer(AccountModel account)
    {
        playing.Add(account);

        if (!wereSeen.Contains(account))
            wereSeen.Add(account);
    }

    public string CreateNote()
    {
        return string.Join(", ", wereSeen.Select(p => p.Note ?? p.SteamID.ToString()).ToArray());
    }
}
