using Godot;
using GrudgeLap.Physics.Vehicle;

namespace GrudgeLap.Game;

/// <summary>
/// The finish of a colour mixed in the showroom (0.25): a solid colour as the classic paints are, a metallic as the
/// silver is, or a pearl, whose mica turns its colour with the angle it is seen at (<see cref="CarView.MixedPaint"/>).
/// </summary>
internal enum PaintFinish
{
    Solid,
    Metallic,
    Pearl,
}

/// <summary>
/// The paint chosen for one car (0.25): its own colour (ORIGINAL, the model's factory colour and the default), one of
/// the preset paints (<see cref="CarView.Paints"/>), or a colour mixed in the showroom in a finish. The settings keep
/// it as text, the preset's name or the colour as hex with its finish ("#1E4FA0 METALLIC"), so a file stays readable
/// and a preset added or moved later does not change what an old file means.
/// </summary>
internal readonly record struct PaintChoice(int Preset, Color Colour, PaintFinish Finish)
{
    /// <summary>The <see cref="Preset"/> of a mixed colour.</summary>
    public const int Custom = -1;

    /// <summary>The car's own colour; <c>default</c> is this too.</summary>
    public static PaintChoice Original => default;

    public static PaintChoice OfPreset(int index) => new(Math.Clamp(index, 0, CarView.Paints.Length - 1), default, PaintFinish.Solid);

    /// <summary>A mixed colour, as the settings hold it: opaque and to the 8 bits a channel of its hex keeps.</summary>
    public static PaintChoice Mixed(Color colour, PaintFinish finish) => new(Custom, Color.FromHtml(colour.ToHtml(false)), finish);

    public bool IsCustom => Preset == Custom;

    /// <summary>The paint the car is drawn in, or null for its own.</summary>
    public CarView.CarPaint? Paint => IsCustom ? CarView.MixedPaint(Colour, Finish) : CarView.Paints[Preset].Paint;

    /// <summary>The name the menu and the showroom print: the preset's, or CUSTOM and the finish.</summary>
    public string Name => IsCustom ? $"CUSTOM {FinishName(Finish)}" : CarView.Paints[Preset].Name;

    /// <summary>The mixed colour as the settings write it, e.g. #1E4FA0.</summary>
    public string Hex => $"#{Colour.ToHtml(false).ToUpperInvariant()}";

    public static string FinishName(PaintFinish finish) => finish.ToString().ToUpperInvariant();

    /// <summary>The text the settings keep (see <see cref="TryParse"/>).</summary>
    public string Format() => IsCustom ? $"{Hex} {FinishName(Finish)}" : CarView.Paints[Preset].Name;

    /// <summary>
    /// A paint from its text: a preset's name in any case, with or without its spaces (DARK BLUE, dark-blue,
    /// darkblue), or its index; or a colour as hex with an optional finish after a space or a colon (#1e4fa0,
    /// #1E4FA0 METALLIC, #1e4fa0:pearl; solid when none is given). False for anything else.
    /// </summary>
    public static bool TryParse(string? text, out PaintChoice choice)
    {
        choice = Original;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var parts = text.Trim().Split([' ', ':', ','], StringSplitOptions.RemoveEmptyEntries);
        if (parts[0].StartsWith('#'))
        {
            if (parts[0].Length != 7 || !Color.HtmlIsValid(parts[0]) || parts.Length > 2) return false;
            var finish = PaintFinish.Solid;
            if (parts.Length == 2 && !Enum.TryParse(parts[1], ignoreCase: true, out finish)) return false;
            if (!Enum.IsDefined(finish)) return false;
            choice = Mixed(Color.FromHtml(parts[0]), finish);
            return true;
        }
        var wanted = text.Replace("-", "").Replace("_", "").Replace(" ", "").ToUpperInvariant();
        var index = Array.FindIndex(CarView.Paints, p => p.Name.Replace(" ", "") == wanted);
        if (index < 0 && int.TryParse(text, out var number) && number >= 0 && number < CarView.Paints.Length) index = number;
        if (index < 0) return false;
        choice = OfPreset(index);
        return true;
    }
}

/// <summary>
/// Every car's paint (0.25), kept in the settings as <c>paint.&lt;car&gt;</c> in the game section, the car named by its
/// style (paint.flattwelve, paint.hothatch...), so a car added to the catalogue has its own key with nothing else to
/// write, and a car without one is in its own colour. Until 0.24 one paint (<c>paint</c>, an index into
/// <see cref="CarView.Paints"/>) was kept for whatever car was driven: the first start that reads this book gives every
/// car that paint, so a player's choice is not lost, and drops the old key, so the migration happens once.
/// </summary>
/// <remarks>
/// A book that does not keep (<see cref="Keep"/> false: the automated runs, which share the player's settings file)
/// migrates and changes in memory only, so a test of the game never rewrites the player's choices. The last colour a
/// car was mixed in is remembered while the game runs (<see cref="LastMixed"/>), so stepping from it to a preset and
/// back finds it again; the settings keep only the paint the car is in.
/// </remarks>
internal sealed class PaintBook
{
    private const string Section = "game";
    private const string OldKey = "paint";
    private readonly string _file;
    private readonly PaintChoice[] _choices;
    private readonly PaintChoice?[] _lastMixed;

    private PaintBook(string file, bool keep)
    {
        _file = file;
        Keep = keep;
        _choices = new PaintChoice[VehicleSpec.Catalogue.Length];
        _lastMixed = new PaintChoice?[VehicleSpec.Catalogue.Length];
    }

    /// <summary>Changes are written to the settings file (false: held in memory, see the remarks).</summary>
    public bool Keep { get; }

    /// <summary>The paint of a car of the catalogue, by its index.</summary>
    public PaintChoice this[int car] => _choices[car];

    /// <summary>The colour the car was last mixed in, if it has been (its paint now or before a preset was chosen).</summary>
    public PaintChoice? LastMixed(int car) => _lastMixed[car];

    /// <summary>The settings key of a car's paint.</summary>
    public static string Key(VehicleSpec spec) => $"paint.{spec.Style.ToString().ToLowerInvariant()}";

    /// <summary>Reads every car's paint from the settings file (missing: every car in its own), migrating an 0.24 file.</summary>
    public static PaintBook Load(string file, bool keep)
    {
        var book = new PaintBook(file, keep);
        var settings = new ConfigFile();
        settings.Load(file);   // a missing file leaves the settings empty
        if (Migrate(settings) && keep) settings.Save(file);
        for (var i = 0; i < book._choices.Length; i++)
        {
            var text = settings.GetValue(Section, Key(VehicleSpec.Catalogue[i]), "").AsString();
            if (!PaintChoice.TryParse(text, out var choice)) continue;
            book._choices[i] = choice;
            if (choice.IsCustom) book._lastMixed[i] = choice;
        }
        return book;
    }

    /// <summary>The 0.24 file's one paint given to every car that has none of its own yet; the old key dropped.</summary>
    private static bool Migrate(ConfigFile settings)
    {
        if (!settings.HasSectionKey(Section, OldKey)) return false;
        var old = settings.GetValue(Section, OldKey, 0L);
        var index = old.VariantType == Variant.Type.Int ? (int)old.AsInt64() : 0;
        if (index > 0 && index < CarView.Paints.Length)
        {
            var paint = PaintChoice.OfPreset(index).Format();
            foreach (var spec in VehicleSpec.Catalogue)
            {
                if (!settings.HasSectionKey(Section, Key(spec))) settings.SetValue(Section, Key(spec), paint);
            }
        }
        settings.EraseSectionKey(Section, OldKey);
        return true;
    }

    /// <summary>
    /// Sets a car's paint and, when the book keeps and <paramref name="keep"/> is true, writes it (the other keys of
    /// the file as they were); false holds it for this run only (<c>--paint=</c>).
    /// </summary>
    public void Set(int car, PaintChoice choice, bool keep = true)
    {
        _choices[car] = choice;
        if (choice.IsCustom) _lastMixed[car] = choice;
        if (!keep || !Keep) return;
        var settings = new ConfigFile();
        settings.Load(_file);
        settings.SetValue(Section, Key(VehicleSpec.Catalogue[car]), choice.Format());
        settings.Save(_file);
    }

    /// <summary>
    /// The paints the menu's PAINT line steps through for a car: its own colour and the presets, and the colour it was
    /// last mixed in when it has one.
    /// </summary>
    public PaintChoice[] Options(int car)
    {
        var presets = Enumerable.Range(0, CarView.Paints.Length).Select(PaintChoice.OfPreset);
        return _lastMixed[car] is { } mixed ? [.. presets, mixed] : [.. presets];
    }
}

/// <summary>Hue, saturation and lightness (each 0 to 1) of an sRGB colour, for the showroom's colour picker.</summary>
internal static class Hsl
{
    public static Color ToColour(float hue, float saturation, float lightness)
    {
        var chroma = (1f - MathF.Abs(2f * lightness - 1f)) * saturation;
        var h = (hue - MathF.Floor(hue)) * 6f;
        var x = chroma * (1f - MathF.Abs(h % 2f - 1f));
        var (r, g, b) = h switch
        {
            < 1f => (chroma, x, 0f),
            < 2f => (x, chroma, 0f),
            < 3f => (0f, chroma, x),
            < 4f => (0f, x, chroma),
            < 5f => (x, 0f, chroma),
            _ => (chroma, 0f, x),
        };
        var m = lightness - chroma * 0.5f;
        return new Color(r + m, g + m, b + m);
    }

    public static (float Hue, float Saturation, float Lightness) From(Color colour)
    {
        var max = MathF.Max(colour.R, MathF.Max(colour.G, colour.B));
        var min = MathF.Min(colour.R, MathF.Min(colour.G, colour.B));
        var lightness = (max + min) * 0.5f;
        var chroma = max - min;
        if (chroma < 1e-5f) return (0f, 0f, lightness);
        var saturation = chroma / (1f - MathF.Abs(2f * lightness - 1f));
        float hue;
        if (max == colour.R) hue = (colour.G - colour.B) / chroma % 6f;
        else if (max == colour.G) hue = (colour.B - colour.R) / chroma + 2f;
        else hue = (colour.R - colour.G) / chroma + 4f;
        hue /= 6f;
        if (hue < 0f) hue += 1f;
        return (hue, Math.Clamp(saturation, 0f, 1f), lightness);
    }
}
