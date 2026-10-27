using Godot;
using GrudgeLap.Physics.Race;

namespace GrudgeLap.Game;

/// <summary>
/// The high-score table of this machine and the entry of three initials at the end of a credit that makes it,
/// as the cabinet had it: up and down turn the letter, left and right move along the three, Enter (or the pad's
/// A) takes the letter and, on the last one, the line. The table is kept in the user folder as text
/// (<see cref="HighScoreTable.Serialize"/>) and starts as the factory's.
/// </summary>
internal sealed class ScoreBoard
{
    private const string ScoresFile = "user://scores.txt";
    private readonly char[] _letters = new char[HighScoreTable.InitialsLength];
    private HighScore? _pending;
    private float _repeat;
    private int _held;

    public HighScoreTable Table { get; private set; } = HighScoreTable.Factory();
    /// <summary>True while a credit's initials are being entered; <see cref="Update"/> reads the keys then.</summary>
    public bool Entering => _pending is not null;
    public string Initials => new(_letters);
    /// <summary>The letter being changed, 0 to 2.</summary>
    public int Cursor { get; private set; }
    /// <summary>The line of the table the last entry took, to pick it out; -1 for none.</summary>
    public int Rank { get; private set; } = -1;

    public ScoreBoard()
    {
        Load();
    }

    /// <summary>Called once at the end of a credit: opens the entry when the score makes the table.</summary>
    public void Offer(long score, float bestLap, RouteKind route, string car)
    {
        Rank = -1;
        _pending = null;
        if (!Table.Qualifies(score)) return;
        _pending = new HighScore("AAA", score, bestLap, route, car);
        Array.Fill(_letters, 'A');
        Cursor = 0;
        _repeat = 0f;
        _held = 0;
    }

    /// <summary>Drops an entry in progress (a new game was started).</summary>
    public void Dismiss()
    {
        _pending = null;
        Rank = -1;
    }

    /// <summary>Reads the keys while the initials are entered.</summary>
    public void Update(float dt)
    {
        if (_pending is null) return;
        // Up and down step the letter, repeating while held after a pause, like a cabinet's switch.
        var direction = (Input.IsActionPressed(InputSetup.Throttle) ? 1 : 0) - (Input.IsActionPressed(InputSetup.Brake) ? 1 : 0);
        if (direction == 0)
        {
            _held = 0;
        }
        else if (direction != _held || (_repeat -= dt) <= 0f)
        {
            _repeat = direction != _held ? 0.45f : 0.07f;
            _held = direction;
            var alphabet = HighScoreTable.Alphabet;
            var index = alphabet.IndexOf(_letters[Cursor]);
            _letters[Cursor] = alphabet[(index + direction + alphabet.Length) % alphabet.Length];
        }
        if (Input.IsActionJustPressed(InputSetup.SteerRight)) Cursor = Math.Min(Cursor + 1, HighScoreTable.InitialsLength - 1);
        if (Input.IsActionJustPressed(InputSetup.SteerLeft)) Cursor = Math.Max(Cursor - 1, 0);
        if (Input.IsActionJustPressed(InputSetup.Ignition) || Input.IsActionJustPressed(InputSetup.Abort))
        {
            if (Cursor < HighScoreTable.InitialsLength - 1) Cursor++;
            else Commit();
        }
    }

    private void Commit()
    {
        if (_pending is null) return;
        Rank = Table.Add(_pending with { Initials = Initials });
        _pending = null;
        Save();
    }

    private void Load()
    {
        try
        {
            var path = ProjectSettings.GlobalizePath(ScoresFile);
            if (!System.IO.File.Exists(path)) return;
            var table = HighScoreTable.Parse(System.IO.File.ReadAllText(path));
            // A file that parses to nothing (damaged, or from a later format) keeps the factory table rather than an empty one.
            if (table.Entries.Count > 0) Table = table;
        }
        catch (System.Exception ex)
        {
            GD.PrintErr($"high scores could not be loaded: {ex.Message}");
        }
    }

    private void Save()
    {
        try
        {
            var path = ProjectSettings.GlobalizePath(ScoresFile);
            var directory = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) System.IO.Directory.CreateDirectory(directory);
            System.IO.File.WriteAllText(path, Table.Serialize());
        }
        catch (System.Exception ex)
        {
            GD.PrintErr($"high scores could not be saved: {ex.Message}");
        }
    }
}
