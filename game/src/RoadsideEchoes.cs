using Godot;
using GrudgeLap.Physics.Track;
using SN = System.Numerics;

namespace GrudgeLap.Game;

/// <summary>
/// What stands beside the road for the car's noise to come back off (0.47): the marker posts, the signs, the trees by
/// the road, the piers, the pumps, the gantries' legs and the buildings' walls. Once a frame the one the car is passing
/// gives how strongly its noise comes back (<see cref="GrudgeLap.Audio.BodyState.Passing"/>) and from how far, for the
/// whoosh of a roadside going by (<see cref="GrudgeLap.Audio.BodyNoise"/>).
/// </summary>
/// <remarks>
/// Each reflector is a circle on the ground with a size for how much sound it throws back: a post is thin and gives a
/// short, faint whip, a wall broad and gives a long roar. Its echo is strongest abeam, over a window along the road a
/// little longer than the object, and falls with the gap (as its 1.3th power). A grid of 20 m cells keeps the search to the few
/// within reach. Built in the simulation's axes, x and z on the ground.
/// </remarks>
internal sealed class RoadsideEchoes
{
    private const float Cell = 20f;
    private const float Reach = 14f;

    private readonly record struct Reflector(SN.Vector2 At, float Radius, float Size);

    private readonly Dictionary<(int, int), List<Reflector>> _cells = new();
    private bool _passing;

    /// <summary>--pose-log: each object as the car passes it.</summary>
    public static bool Log { get; set; }

    /// <summary>The marker posts' feet (WorldBuilder.BuildMarkerPosts), the simulation's axes: the view places them, the physics does not know them.</summary>
    public static List<SN.Vector3> Posts { get; } = new();

    public RoadsideEchoes(TrackGeometry track)
    {
        foreach (var post in Posts) Add(new SN.Vector2(post.X, post.Z), 0.08f, 0.5f);
        foreach (var prop in track.Props)
        {
            var at = new SN.Vector2(prop.Position.X, prop.Position.Z);
            var half = 0.5f * MathF.Max(prop.Size.X, prop.Size.Z);
            switch (prop.Kind)
            {
                case PropKind.Sign or PropKind.Signpost or PropKind.Crossbuck:
                    Add(at, MathF.Min(half, 0.6f), 0.6f);
                    break;
                case PropKind.Tree:
                    Add(at, 0.4f, 0.8f);
                    break;
                case PropKind.Pier or PropKind.Tower or PropKind.Gate or PropKind.Pump or PropKind.Barrier:
                    Add(at, MathF.Min(half, 2f), 1f);
                    break;
                case PropKind.Barn or PropKind.House or PropKind.Shop or PropKind.Hut:
                    Add(at, half, 1.3f);
                    break;
            }
        }
    }

    private void Add(SN.Vector2 at, float radius, float size)
    {
        var key = ((int)MathF.Floor(at.X / Cell), (int)MathF.Floor(at.Y / Cell));
        if (!_cells.TryGetValue(key, out var list)) _cells[key] = list = new List<Reflector>();
        list.Add(new Reflector(at, radius, size));
    }

    /// <summary>
    /// What the car at <paramref name="position"/> heading along <paramref name="forward"/> is passing: how strongly its
    /// noise comes back (0 to about 1.5) and the gap to the object, m.
    /// </summary>
    public (float Strength, float Gap) Passing(SN.Vector3 position, SN.Vector3 forward)
    {
        var here = new SN.Vector2(position.X, position.Z);
        var ahead = new SN.Vector2(forward.X, forward.Z);
        if (ahead.LengthSquared() < 1e-6f) return (0f, 2f);
        ahead = SN.Vector2.Normalize(ahead);
        var best = 0f;
        var gap = 2f;
        var cx = (int)MathF.Floor(here.X / Cell);
        var cz = (int)MathF.Floor(here.Y / Cell);
        for (var i = cx - 1; i <= cx + 1; i++)
        {
            for (var j = cz - 1; j <= cz + 1; j++)
            {
                if (!_cells.TryGetValue((i, j), out var list)) continue;
                foreach (var r in list)
                {
                    var d = r.At - here;
                    var along = SN.Vector2.Dot(d, ahead);
                    var side = MathF.Abs(d.X * ahead.Y - d.Y * ahead.X) - r.Radius;
                    if (side > Reach || MathF.Abs(along) > Reach) continue;
                    side = MathF.Max(side, 0.5f);
                    // Abeam the echo is strongest; the window along the road is the object's own length and a bit.
                    var window = 1.2f + r.Radius;
                    var abeam = MathF.Exp(-(along * along) / (window * window));
                    // Gentler than a point's fall with the square of the gap: the posts stand 4 to 5 m off a car in its lane,
                    // and at the square their whoosh was lost under the wind.
                    var strength = r.Size * abeam * MathF.Min(1f, MathF.Pow(1.5f / side, 1.3f));
                    if (strength > best)
                    {
                        best = strength;
                        gap = side;
                    }
                }
            }
        }
        if (Log && best > 0.1f != _passing)
        {
            _passing = best > 0.1f;
            if (_passing) GD.Print($"roadside: passing at {gap:0.0} m, strength {best:0.00}, at ({position.X:0}, {position.Z:0})");
        }
        return (best, gap);
    }
}
