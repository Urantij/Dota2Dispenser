using Dota2Dispenser.Database.Models;

namespace Dota2Dispenser.Match;

public class TrackedMatch
{
    public MatchModel Match { get; }

    /// <summary>
    /// Отслеживаемые челы, которые были замечены в матче.
    /// Везде идёт сравнение по ссылке объекта.
    /// </summary>
    public List<AccountModel> WereSeen { get; } = new();

    /// <summary>
    /// Отслеживаемые челы, которые всё ещё находятся в матче, а не вышли из него.
    /// Везде идёт сравнение по ссылке объекта.
    /// </summary>
    public List<AccountModel> Playing { get; } = new();

    /// <summary>
    /// В теории пати могут изменяться прямо во время игры, и это очень нестабильная тема.
    /// Но она при этом очень ненужная. Самая верная инфа - первая.
    /// </summary>
    public List<ulong[]> Parties { get; } = new();

    /// <summary>
    /// Игра может найтись до того, как будут все пики. И пока не все герои найдены, их следует трогать.
    /// </summary>
    public bool GotAllHeroes { get; set; } = false;

    /// <summary>
    /// Когда матч отметили как ДЕД
    /// </summary>
    public DateTimeOffset? DeathDate { get; set; }

    public TrackedMatch(MatchModel match, bool gotAllHeroes, DateTimeOffset? deathDate)
    {
        this.Match = match;
        this.GotAllHeroes = gotAllHeroes;
        this.DeathDate = deathDate;
    }

    public void AddPlayer(AccountModel account)
    {
        Playing.Add(account);

        if (!WereSeen.Contains(account))
            WereSeen.Add(account);
    }

    public string CreateNote()
    {
        return string.Join(", ", WereSeen.Select(p => p.Note ?? p.SteamID.ToString()).ToArray());
    }
}