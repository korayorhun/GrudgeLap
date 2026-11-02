using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;
using Xunit.Abstractions;

namespace GrudgeLap.Physics.Tests;

/// <summary>
/// The real car models are named in four places that nothing else ties together: unreal/Scripts/fetch_models.py fetches
/// them, tools/car_models.json (the game) and unreal/Import/Models/models.json (the Unreal level) draw them, a LICENSE.txt
/// beside each and THIRD_PARTY.md credit them. models13 credited two models no entry drew, and every fresh build fetched
/// their 52 MB (its review).
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
        // "name.glb": "https://huggingface.co/datasets/allenai/objaverse/resolve/main/glbs/000-011/<Sketchfab uid>.glb"
        var fetched = Regex.Matches(File.ReadAllText(Path.Combine(root, "unreal/Scripts/fetch_models.py")),
                @"""([\w.-]+\.glb)"":\s*""https://[^""]+/([0-9a-f]{32})\.glb""")
            .ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value);
        var game = DrawnFiles(Path.Combine(root, "tools/car_models.json"));
        var unreal = DrawnFiles(Path.Combine(models, "models.json"));
        var credits = File.ReadAllText(Path.Combine(root, "THIRD_PARTY.md"));
        _out.WriteLine($"{fetched.Count} fetched; the game draws {game.Count}, the Unreal level {unreal.Count}");
        Assert.True(fetched.Count >= 12, "fetch_models.py's list was not read");

        foreach (var (file, uid) in fetched)
        {
            var licence = Path.Combine(models, Path.GetFileNameWithoutExtension(file) + ".LICENSE.txt");
            Assert.True(File.Exists(licence), $"{file} is fetched without a LICENSE.txt beside it");
            Assert.Contains(uid, File.ReadAllText(licence));
            Assert.True(game.Contains(file) || unreal.Contains(file), $"{file} is fetched but no entry draws it");
        }
        foreach (var file in game)
        {
            Assert.True(fetched.ContainsKey(file), $"tools/car_models.json draws {file}, which fetch_models.py does not fetch");
            Assert.True(credits.Contains($"https://sketchfab.com/3d-models/{fetched[file]}", StringComparison.Ordinal),
                $"the game draws {file} and THIRD_PARTY.md does not credit it");
        }
        // And nothing credited that the game does not draw.
        var drawn = game.Select(f => fetched[f]).ToHashSet();
        foreach (Match credit in Regex.Matches(credits, @"https://sketchfab\.com/3d-models/([0-9a-f]{32})"))
            Assert.True(drawn.Contains(credit.Groups[1].Value), $"THIRD_PARTY.md credits {credit.Value}, which the game does not draw");
    }

    /// <summary>
    /// The 458's model needs three things of its entry that nothing else checks before the game is run (models13 review):
    /// its glasshouse is one opaque black material whose name does not say glass (from the seat the driver saw black
    /// with a sliver of road), its paint is "Material", a part of every "Material.0NN" (named exactly, or every part
    /// takes the paint), and the 570S cabin it borrows has its two seats in one mesh, which the eyes took for a roof
    /// (0.86 m over the road, the binnacle hiding it). And its rims, whose accessors' bounds make them bigger than the
    /// tyres, roll only if the preparation measures them by their vertices, which it can only with the binary chunk.
    /// </summary>
    [Fact]
    public void The_458_sees_through_its_glass_and_its_rims_roll()
    {
        var root = TrackTests.FindRepoRoot();
        using var config = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "tools/car_models.json")));
        var entry = config.RootElement.GetProperty("458");
        Assert.Equal("ferrari-458-italia-antonis.glb", entry.GetProperty("file").GetString());
        Assert.Equal("==Material.048", entry.GetProperty("glass")[0].GetString());
        Assert.Equal("==Material", entry.GetProperty("paint")[0].GetString());
        Assert.Equal("=object_16", entry.GetProperty("cabin_from").GetProperty("seats")[0].GetString());
        var prepare = File.ReadAllText(Path.Combine(root, "tools/prepare_car_models.py"));
        Assert.Contains("if is_entry_glass(entry, name):", prepare, StringComparison.Ordinal);
        Assert.Contains("name = \"Seat_\" + name", prepare, StringComparison.Ordinal);
        Assert.Contains("cm.rig(model, entry, spec, car, blob)", prepare, StringComparison.Ordinal);
        Assert.Contains("def rig(model, entry, spec, car, blob=None):", File.ReadAllText(Path.Combine(root, "tools/car_models.py")), StringComparison.Ordinal);
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
}
