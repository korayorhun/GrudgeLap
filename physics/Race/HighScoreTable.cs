using System.Globalization;

namespace GrudgeLap.Physics.Race;

/// <summary>One line of the high-score table: whose credit, how many points, and its best lap, route and car.</summary>
public sealed record HighScore(string Initials, long Score, float BestLap, RouteKind Route, string Car)
{
    /// <summary>True when the credit finished a lap; the table shows a time only then.</summary>
    public bool HasLap => BestLap > 0f && float.IsFinite(BestLap);
}

/// <summary>
/// The cabinet's table of the ten best credits, kept in order. A score that beats the last line gets in and the
/// player enters three initials for it, as in 1989. Engine-agnostic: the Godot and the Unreal versions keep it in
/// the same text form (<see cref="Serialize"/>), so a table moves between them.
/// </summary>
public sealed class HighScoreTable
{
    public const int Capacity = 10;
    public const int InitialsLength = 3;
    /// <summary>The letters the initials are made of, in the order the entry steps through them.</summary>
    public const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ ";
    private const string Header = "GLPSCORES 1";

    private readonly List<HighScore> _entries = new();

    /// <summary>Best first.</summary>
    public IReadOnlyList<HighScore> Entries => _entries;

    /// <summary>The table as the cabinet shipped: ten names to beat, from a good credit down to a short one.</summary>
    public static HighScoreTable Factory()
    {
        var table = new HighScoreTable();
        (string Initials, long Score, float Lap, RouteKind Route)[] lines =
        [
            ("ACE", 92000, 101.4f, RouteKind.Stunt), ("JET", 84500, 104.9f, RouteKind.Speed), ("RAY", 76000, 108.2f, RouteKind.Stunt),
            ("MAX", 68000, 112.0f, RouteKind.Speed), ("SKY", 59500, 117.5f, RouteKind.Stunt), ("TAZ", 51000, 123.8f, RouteKind.Speed),
            ("ZED", 43000, -1f, RouteKind.Undecided), ("BOB", 35500, -1f, RouteKind.Undecided), ("KAY", 27000, -1f, RouteKind.Undecided), ("LEE", 18000, -1f, RouteKind.Undecided),
        ];
        foreach (var (initials, score, lap, route) in lines) table._entries.Add(new HighScore(initials, score, lap, route, "FLAT-12 GT '84"));
        return table;
    }

    /// <summary>True when a credit with this score would get a line.</summary>
    public bool Qualifies(long score) => score > 0 && (_entries.Count < Capacity || score > _entries[^1].Score);

    /// <summary>
    /// Puts a credit in its place and drops the line that falls off the end. Returns the line it took (0 is the
    /// top), or -1 when the score does not qualify. An equal score ranks below the earlier credit.
    /// </summary>
    public int Add(HighScore entry)
    {
        if (!Qualifies(entry.Score)) return -1;
        var rank = _entries.FindIndex(e => e.Score < entry.Score);
        if (rank < 0) rank = _entries.Count;
        _entries.Insert(rank, entry with { Initials = CleanInitials(entry.Initials) });
        if (_entries.Count > Capacity) _entries.RemoveRange(Capacity, _entries.Count - Capacity);
        return rank;
    }

    /// <summary>Upper-case letters of the <see cref="Alphabet"/> only, exactly <see cref="InitialsLength"/> of them.</summary>
    public static string CleanInitials(string initials)
    {
        var letters = new char[InitialsLength];
        for (var i = 0; i < InitialsLength; i++)
        {
            var c = i < initials.Length ? char.ToUpperInvariant(initials[i]) : ' ';
            letters[i] = Alphabet.Contains(c) ? c : ' ';
        }
        return new string(letters);
    }

    /// <summary>A text form: a header line, then one line per entry with its fields separated by '|'.</summary>
    public string Serialize()
    {
        var lines = new List<string> { Header };
        foreach (var e in _entries)
        {
            var lap = e.HasLap ? e.BestLap.ToString("F2", CultureInfo.InvariantCulture) : "-1";
            lines.Add($"{e.Initials}|{e.Score.ToString(CultureInfo.InvariantCulture)}|{lap}|{(int)e.Route}|{e.Car.Replace('|', ' ')}");
        }
        return string.Join("\n", lines) + "\n";
    }

    /// <summary>Reads <see cref="Serialize"/>'s form; lines that do not parse are skipped, and a text without the header gives an empty table.</summary>
    public static HighScoreTable Parse(string text)
    {
        var table = new HighScoreTable();
        var lines = text.Split('\n');
        if (lines.Length == 0 || lines[0].Trim() != Header) return table;
        foreach (var raw in lines.Skip(1))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length == 0) continue;
            var parts = line.Split('|');
            if (parts.Length < 5) continue;
            if (!long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var score)) continue;
            if (!float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var lap)) lap = -1f;
            if (!int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var route) || !Enum.IsDefined(typeof(RouteKind), route)) route = (int)RouteKind.Undecided;
            table.Add(new HighScore(parts[0], score, lap, (RouteKind)route, parts[4]));
        }
        return table;
    }
}
