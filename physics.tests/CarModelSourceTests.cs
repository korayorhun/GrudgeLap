using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;
using Xunit.Abstractions;

namespace GrudgeLap.Physics.Tests;

/// <summary>
/// The real car models are named in four places that nothing else ties together: unreal/Scripts/fetch_models.py fetches
/// them, tools/car_models.json (the game) and unreal/Import/Models/models.json (the Unreal level) draw them, a LICENSE.txt
/// beside each and THIRD_PARTY.md credit them. models13 credited two models no entry drew, and every fresh build fetched
/// their 52 MB (its review). A model the mirror does not hold is fetched from the project's own copy and checked against
/// the SHA-256 its LICENSE.txt gives (cars13 bodies: the BRZ's Toyota 86); one that is not from Sketchfab is named by
/// its page, which its LICENSE.txt and THIRD_PARTY.md must name (v74: RainerS's 458, from BlendSwap).
/// </summary>
public sealed class CarModelSourceTests
{
    private readonly ITestOutputHelper _out;

    public CarModelSourceTests(ITestOutputHelper output) => _out = output;

    [Fact]
    public void Every_fetched_model_is_licensed_drawn_and_credited()
    {
        var root = TrackTests.FindRepoRoot();
        var models = Path.Combine(root, "unreal/Import/Models");
        // "name.glb": "https://huggingface.co/datasets/allenai/objaverse/resolve/main/glbs/000-011/<Sketchfab uid>.glb", and
        // the models the mirror does not hold (RELEASED, cars13 bodies): "name.glb": {"url": ..., "sha256": ...,
        // "sketchfab": "<uid>"}, or "page": "<its page>" for a model from another site (v74: the 458's BlendSwap page),
        // whose LICENSE.txt must describe the very file (its SHA-256).
        var script = File.ReadAllText(Path.Combine(root, "unreal/Scripts/fetch_models.py"));
        // Per file: what its LICENSE.txt must name (the Sketchfab uid, or the page) and the address THIRD_PARTY.md credits.
        var fetched = Regex.Matches(script, @"""([\w.-]+\.glb)"":\s*""https://[^""]+/([0-9a-f]{32})\.glb""")
            .ToDictionary(m => m.Groups[1].Value, m => (Key: m.Groups[2].Value, Credit: SketchfabPage(m.Groups[2].Value)));
        var released = Regex.Matches(script, @"""([\w.-]+\.glb)"":\s*\{[^}]*""sha256"":\s*""([0-9a-f]{64})""[^}]*""(sketchfab|page)"":\s*""([^""]+)""");
        Assert.NotEmpty(released);
        foreach (Match entry in released)
        {
            var key = entry.Groups[4].Value;
            // A page is BlendSwap's: another site needs its address here and in the scan of the credits below.
            Assert.Matches(entry.Groups[3].Value == "sketchfab" ? "^[0-9a-f]{32}$" : @"^https://www\.blendswap\.com/blend/\d+$", key);
            fetched.Add(entry.Groups[1].Value, (key, entry.Groups[3].Value == "sketchfab" ? SketchfabPage(key) : key));
            var licence = Path.Combine(models, Path.GetFileNameWithoutExtension(entry.Groups[1].Value) + ".LICENSE.txt");
            Assert.True(File.Exists(licence) && File.ReadAllText(licence).Contains(entry.Groups[2].Value, StringComparison.Ordinal),
                $"{entry.Groups[1].Value}'s LICENSE.txt does not name the SHA-256 fetch_models.py checks it against");
        }
        var game = DrawnFiles(Path.Combine(root, "tools/car_models.json"));
        // And the models the build copies into the game as they come (0.51: the herd's scanned cow, CowBuilder).
        foreach (Match copy in Regex.Matches(File.ReadAllText(Path.Combine(root, "tools/build.ps1")), @"unreal/Import/Models/([\w.-]+\.glb)"))
        {
            game.Add(copy.Groups[1].Value);
        }
        var unreal = DrawnFiles(Path.Combine(models, "models.json"));
        var credits = File.ReadAllText(Path.Combine(root, "THIRD_PARTY.md"));
        _out.WriteLine($"{fetched.Count} fetched; the game draws {game.Count}, the Unreal level {unreal.Count}");
        Assert.True(fetched.Count >= 12, "fetch_models.py's list was not read");

        foreach (var (file, (key, _)) in fetched)
        {
            var licence = Path.Combine(models, Path.GetFileNameWithoutExtension(file) + ".LICENSE.txt");
            Assert.True(File.Exists(licence), $"{file} is fetched without a LICENSE.txt beside it");
            Assert.Contains(key, File.ReadAllText(licence));
            Assert.True(game.Contains(file) || unreal.Contains(file), $"{file} is fetched but no entry draws it");
        }
        foreach (var file in game)
        {
            Assert.True(fetched.ContainsKey(file), $"tools/car_models.json draws {file}, which fetch_models.py does not fetch");
            Assert.True(credits.Contains(fetched[file].Credit, StringComparison.Ordinal),
                $"the game draws {file} and THIRD_PARTY.md does not credit it");
        }
        // And nothing credited that the game does not draw.
        var drawn = game.Select(f => fetched[f].Credit).ToHashSet();
        foreach (Match credit in Regex.Matches(credits, @"https://sketchfab\.com/3d-models/[0-9a-f]{32}|https://www\.blendswap\.com/blend/\d+"))
            Assert.True(drawn.Contains(credit.Value), $"THIRD_PARTY.md credits {credit.Value}, which the game does not draw");
    }

    /// <summary>
    /// The 458's model (v74: RainerS's, converted from his .blend by tools/convert_rainers_458.py) needs things of its
    /// entry that nothing else checks before the game is run. Its paint is "MainBody.001" and "MainBody.000", which no
    /// rule of the preparation takes for paint: named exactly, both in Rosso Corsa (the traffic's 458 draws the larger
    /// one's colour). Four of its black trims have "window" in their names and would be drawn as glass unless kept. Its
    /// brake lamps are the tail lamps' LED rings and the high-mount stop lamp, not the whole faces, whose chrome centres
    /// stay unlit as on the car (the earlier model lit an opaque red disc). Its lamp covers become clear lenses only by
    /// their transmission, as their names do not say glass. Its own plain cabin and the maker's marks are left out, and
    /// the 570S cabin it borrows has its two seats in one mesh, which the eyes took for a roof (0.86 m over the road, the
    /// binnacle hiding it, models13).
    /// </summary>
    [Fact]
    public void The_458_lights_its_LED_rings_and_leaves_out_its_cabin_and_badges()
    {
        var root = TrackTests.FindRepoRoot();
        using var config = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "tools/car_models.json")));
        var entry = config.RootElement.GetProperty("458");
        Assert.Equal("ferrari-458-italia-rainers.glb", entry.GetProperty("file").GetString());
        Assert.Equal(new[] { "==MainBody.001", "==MainBody.000" }, Strings(entry.GetProperty("paint")));
        foreach (var paint in new[] { "MainBody.001", "MainBody.000" })
            Assert.Equal("#E4002B", entry.GetProperty("materials").GetProperty(paint).GetProperty("colour").GetString());
        var kept = Strings(entry.GetProperty("keep")).ToHashSet();
        foreach (var trim in new[] { "rearwindowedges", "windowinterioredge", "windowtriangle", "hoodwindowconnector" })
            Assert.Contains(trim, kept);
        Assert.Equal(new[] { "==TailLampLED", "==Break_Light_Highlight.000" }, Strings(entry.GetProperty("brake")));
        var dropped = Strings(entry.GetProperty("drop")).ToHashSet();
        Assert.Contains("interior", dropped);
        Assert.Contains("badge", dropped);
        Assert.Equal("=object_16", entry.GetProperty("cabin_from").GetProperty("seats")[0].GetString());
        var prepare = File.ReadAllText(Path.Combine(root, "tools/prepare_car_models.py"));
        Assert.Contains("\"KHR_materials_transmission\" in material.get(\"extensions\", {})", prepare, StringComparison.Ordinal);
        Assert.Contains("if any(w.lower() in lower for w in entry.get(\"keep\", [])):", prepare, StringComparison.Ordinal);
        Assert.Contains("name = \"Seat_\" + name", prepare, StringComparison.Ordinal);
        // Any model may still have rims whose accessors' bounds make them bigger than their tyres (the earlier 458's):
        // they roll only if the preparation measures them by their vertices, which it can only with the binary chunk.
        Assert.Contains("cm.rig(model, entry, spec, car, blob)", prepare, StringComparison.Ordinal);
        Assert.Contains("def rig(model, entry, spec, car, blob=None):", File.ReadAllText(Path.Combine(root, "tools/car_models.py")), StringComparison.Ordinal);
        // The conversion is the record of how the published file was made: its LICENSE.txt names it and it is kept.
        var licence = Regex.Replace(File.ReadAllText(Path.Combine(root, "unreal/Import/Models/ferrari-458-italia-rainers.LICENSE.txt")), @"\s+", " ");
        Assert.Contains("Creative Commons Attribution 3.0", licence, StringComparison.Ordinal);
        Assert.Contains("RainerS", licence, StringComparison.Ordinal);
        Assert.Contains("tools/convert_rainers_458.py", licence, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(root, "tools/convert_rainers_458.py")), "the 458's LICENSE.txt names a conversion tools/ does not hold");
    }

    /// <summary>
    /// The BRZ's model (cars13 bodies, Ethan's Toyota 86) is the author's work but for its two seats, which its page says
    /// are not his: they are outside the CC BY grant and must stay out, with the Toyota badges and the caps' logos (the
    /// design's owner's marks). Its paint is "material", a part of "black_car_material" (named exactly), and it has no
    /// cabin of its own, so it borrows the W212's.
    /// </summary>
    [Fact]
    public void The_BRZ_leaves_out_what_its_licence_does_not_cover()
    {
        var root = TrackTests.FindRepoRoot();
        using var config = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "tools/car_models.json")));
        var entry = config.RootElement.GetProperty("brz");
        Assert.Equal("toyota-86-ethanchew.glb", entry.GetProperty("file").GetString());
        var dropped = entry.GetProperty("drop").EnumerateArray().Select(e => e.GetString()).ToHashSet();
        foreach (var part in new[] { "=Object_17", "=Object_18", "=Object_13", "=Object_26", "=Object_32", "=Object_38", "=Object_44" })
            Assert.Contains(part, dropped);
        Assert.Equal("==material", entry.GetProperty("paint")[0].GetString());
        Assert.Equal("fleet", entry.GetProperty("cabin_from").GetProperty("car").GetString());
        var licence = Regex.Replace(File.ReadAllText(Path.Combine(root, "unreal/Import/Models/toyota-86-ethanchew.LICENSE.txt")), @"\s+", " ");
        Assert.Contains("Seat model is not mine", licence, StringComparison.Ordinal);
    }

    /// <summary>The source files a model config's entries draw (every entry's "file", the cabin donors' too).</summary>
    private static HashSet<string> DrawnFiles(string config)
    {
        using var json = JsonDocument.Parse(File.ReadAllText(config));
        return json.RootElement.EnumerateObject()
            .Where(e => e.Value.ValueKind == JsonValueKind.Object && e.Value.TryGetProperty("file", out _))
            .Select(e => e.Value.GetProperty("file").GetString()!)
            .ToHashSet();
    }

    /// <summary>A Sketchfab model's page, as THIRD_PARTY.md credits it, from its uid.</summary>
    private static string SketchfabPage(string uid) => $"https://sketchfab.com/3d-models/{uid}";

    /// <summary>A JSON array of strings.</summary>
    private static string[] Strings(JsonElement array) => array.EnumerateArray().Select(e => e.GetString()!).ToArray();
}
