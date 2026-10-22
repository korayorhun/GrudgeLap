using System.Numerics;

namespace GrudgeLap.Physics.Track;

/// <summary>
/// The default circuit, laid out in the spirit of the 1989 course: a start straight that bridges over the
/// speed route's return leg, a long hill with the fork on its crest, a speed route that swings round the east
/// side, dives back diagonally under the start through two dips and circles the farm to the north-west, a
/// stunt route that drops off the hill to the open drawbridge, bridges over the speed route, runs the loop on
/// its bottom straight, swings through the banked 180 and climbs back north to the merge, and a hidden skid
/// pad in the open ground off the speed route. Geometry is ours; only the rhythm is borrowed.
/// </summary>
public static class OriginalInspiredTrack
{
    /// <summary>Where both routes merge, heading 60; the road then bends to east and runs to the start line.</summary>
    private static readonly Vector3 JoinPoint = new(-60f, 0f, -30f);
    public const float PadRadius = 48f;

    public static TrackGeometry Build()
    {
        // ---- Final: a short stub at the merge so both branches end at the same frame -------------------------
        var final = new TrackBuilder(PathKind.Final, JoinPoint, 60f)
            .Mark(MarkerKind.ReturnPoint, "join")
            .Straight(10f);

        // ---- Common: bend to east, start line, the bridge over the speed route's return leg, the hill ---------
        var common = TrackBuilder.ContinueFrom(final.Path, PathKind.Common)
            .Curve(30f, 110f)
            .Mark(MarkerKind.StartFinish, "START")
            .Mark(MarkerKind.ReturnPoint, "start")
            .Straight(45f)
            .Overpass(140f, 4.5f)
            .Straight(105f)
            .Mark(MarkerKind.Sign, "HILL")
            .Straight(161f, 10f)
            .Mark(MarkerKind.Sign, "SPEED/STUNT");

        // ---- Speed route: down the hill, round the east side, the diagonal with its dips under the start, the big loop ----
        var speed = TrackBuilder.ContinueFrom(common.Path, PathKind.Speed)
            .Straight(161f, -10f)
            .Mark(MarkerKind.ReturnPoint, "speed-1")
            .Curve(45f, 130f)
            .Straight(120f)
            .Curve(45f, 130f)
            .Mark(MarkerKind.Sign, "90")
            .Straight(130f)
            .Mark(MarkerKind.Sign, "40")
            .Curve(90f, 85f)
            .Mark(MarkerKind.ReturnPoint, "speed-2")
            .Straight(100f)
            .Curve(40f, 100f)
            .Straight(300f)                       // the diagonal: 856 m, passing under the start straight's bridge first
            .Mark(MarkerKind.Sign, "DIP")
            .Hill(110f, -3f)
            .Straight(130f)
            .Mark(MarkerKind.Sign, "DIP")
            .Hill(100f, -2.5f)
            .Straight(216f)                       // then under the stunt route's bridge near its end
            .Mark(MarkerKind.Sign, "60")
            .Curve(-40.2f, 110f)
            .Mark(MarkerKind.Checkpoint, "speed")
            .Mark(MarkerKind.ReturnPoint, "speed-3")
            .Straight(150f)
            .Mark(MarkerKind.Sign, "40")
            .Curve(-90f, 95f)
            .Straight(100f)
            .Mark(MarkerKind.Sign, "40")
            .Curve(-90f, 95f)
            .Mark(MarkerKind.ReturnPoint, "speed-4")
            .Straight(100f)
            .Curve(-50f, 120f);

        // ---- Stunt route: off the hill to the drawbridge, over the speed route, loop, banked 180, back north ----
        var stunt = TrackBuilder.ContinueFrom(common.Path, PathKind.Stunt)
            .Curve(90f, 100f, 0f, -10f)
            .Mark(MarkerKind.ReturnPoint, "stunt-1")
            .Mark(MarkerKind.Sign, "60")
            .Straight(90f)
            .Drawbridge(30f, 7f, 18f, 34f)
            .Mark(MarkerKind.ReturnPoint, "stunt-2")
            .Overpass(140f, 4.5f)                 // over the speed route's diagonal
            .Straight(10f)
            .Mark(MarkerKind.Sign, "60")
            .Curve(45f, 80f)
            .Straight(90f)
            .Curve(45f, 80f)
            .Mark(MarkerKind.ReturnPoint, "stunt-3")
            .Straight(160f)
            .Mark(MarkerKind.Checkpoint, "stunt")
            .Straight(100f)
            .Mark(MarkerKind.Sign, "60")
            .Loop(15f, 11f)
            .Mark(MarkerKind.ReturnPoint, "stunt-4")
            .Straight(120f)
            .Mark(MarkerKind.Sign, "DIP")
            .Hill(100f, -2.5f)
            .Straight(150f)
            .Mark(MarkerKind.Sign, "90")
            .Curve(-180f, 75f, 32f)
            .Mark(MarkerKind.ReturnPoint, "stunt-5")
            .Straight(40f)
            .Curve(-90f, 80f)
            .Overpass(140f, 4.5f)                 // over its own bottom straight
            .Straight(270f)
            .Curve(45f, 140f);

        // Both approaches arrive heading close to the merge's own 60 degrees, so the connecting splines stay gentle.
        speed.SplineTo(final.Path.First).JoinTo(final.Path);
        stunt.SplineTo(final.Path.First).JoinTo(final.Path);

        // The ground and the props are laid out on a preliminary circuit; the skid pad then needs a spot that
        // is clear of all of them, and the final circuit carries the pad in its terrain.
        var draft = new TrackGeometry(common.Path, speed.Path, stunt.Path, final.Path) { Name = "Farm Circuit" };
        PlaceProps(draft);
        var pad = FindPadSpot(draft);
        var track = new TrackGeometry(common.Path, speed.Path, stunt.Path, final.Path, [pad]) { Name = "Farm Circuit" };
        foreach (var prop in draft.Props)
        {
            track.Props.Add(prop with { Position = prop.Position with { Y = track.Terrain.Height(prop.Position.X, prop.Position.Z) } });
        }
        // A plain road sign points the curious off the road toward the pad.
        var hint = speed.Path.FrameAt(PadHintDistance(draft, pad));
        var hintSide = MathF.Sign(Vector3.Dot(pad.Centre - hint.Origin, hint.Lateral));
        var hintPos = hint.Origin + hint.Lateral * (hintSide * 8.5f);
        hintPos.Y = track.Terrain.Height(hintPos.X, hintPos.Z);
        track.Props.Add(new TrackProp(PropKind.Sign, hintPos, MathF.Atan2(hint.Tangent.X, hint.Tangent.Z) * MathUtil.Rad2Deg, new Vector3(1.2f, 2.6f, 0.2f), hintSide > 0 ? "PAD >" : "< PAD"));
        return track;
    }

    /// <summary>
    /// Picks the skid pad's spot: beside the speed route, far enough from every road and every solid prop
    /// that the pad and its approach stay clear, preferring the roomiest candidate.
    /// </summary>
    private static SkidPad FindPadSpot(TrackGeometry draft)
    {
        var speed = draft.Speed;
        var best = Vector3.Zero;
        var bestClearance = 0f;
        for (var s = speed.Length * 0.05f; s < speed.Length * 0.55f; s += 40f)
        {
            var f = speed.FrameAt(s);
            var flatLateral = MathUtil.SafeNormalize(new Vector3(f.Lateral.X, 0f, f.Lateral.Z), Vector3.UnitX);
            foreach (var lateral in new[] { -100f, -125f, -150f, 100f, 125f, 150f })
            {
                var candidate = f.Origin + flatLateral * lateral;
                var clearance = draft.ClearanceFromRoads(candidate);
                foreach (var prop in draft.Props)
                {
                    if (!prop.Solid) continue;
                    var dx = prop.Position.X - candidate.X;
                    var dz = prop.Position.Z - candidate.Z;
                    clearance = MathF.Min(clearance, MathF.Sqrt(dx * dx + dz * dz) - MathF.Max(prop.Size.X, prop.Size.Z) * 0.5f);
                }
                if (clearance > bestClearance)
                {
                    bestClearance = clearance;
                    best = candidate;
                }
            }
        }
        best.Y = draft.Terrain.Base(best.X, best.Z);
        return new SkidPad(best, PadRadius);
    }

    /// <summary>Distance along the speed route of the frame nearest to the pad (where the hint sign stands).</summary>
    private static float PadHintDistance(TrackGeometry draft, SkidPad pad)
    {
        var best = 0f;
        var bestDistance = float.MaxValue;
        foreach (var f in draft.Speed.Frames)
        {
            var dx = f.Origin.X - pad.Centre.X;
            var dz = f.Origin.Z - pad.Centre.Z;
            var d = dx * dx + dz * dz;
            if (d < bestDistance)
            {
                bestDistance = d;
                best = f.Distance;
            }
        }
        return best;
    }

    private static void PlaceProps(TrackGeometry track)
    {
        var props = track.Props;
        var terrain = track.Terrain;

        void Beside(TrackPath path, float distance, float lateral, PropKind kind, Vector3 size, string label = "", float yawOffset = 0f)
        {
            var f = path.FrameAt(distance);
            var pos = f.Origin + f.Lateral * lateral;
            pos.Y = terrain.Height(pos.X, pos.Z);
            var yaw = MathF.Atan2(f.Tangent.X, f.Tangent.Z) * MathUtil.Rad2Deg + yawOffset;
            props.Add(new TrackProp(kind, pos, yaw, size, label));
        }

        void At(float x, float z, float yawDeg, PropKind kind, Vector3 size, string label = "")
            => props.Add(new TrackProp(kind, new Vector3(x, terrain.Height(x, z), z), yawDeg, size, label));

        // Signs come straight from the markers so they stay in sync with the layout.
        foreach (var path in track.Paths)
        {
            foreach (var m in path.Markers)
            {
                if (m.Kind == MarkerKind.Sign)
                {
                    Beside(path, m.Distance, m.Label == "SPEED/STUNT" ? 0f : 8.5f,
                        m.Label == "SPEED/STUNT" ? PropKind.Signpost : PropKind.Sign, new Vector3(1.2f, 2.6f, 0.2f), m.Label);
                }
                else if (m.Kind == MarkerKind.Checkpoint)
                {
                    Beside(path, m.Distance, 0f, PropKind.Signpost, new Vector3(14f, 5f, 0.4f), "CHECKPOINT");
                }
            }
        }

        // Farm buildings, placed relative to the roads so they can never sit on one (a test checks every solid prop).
        var speed = track.Speed;
        var stunt = track.Stunt;
        Beside(speed, speed.Length * 0.70f, -45f, PropKind.Barn, new Vector3(18f, 9f, 12f));          // inside the north-west loop
        Beside(speed, speed.Length * 0.70f + 22f, -40f, PropKind.Cow, new Vector3(2.2f, 1.5f, 0.8f));
        Beside(speed, speed.Length * 0.70f + 34f, -52f, PropKind.Cow, new Vector3(2.2f, 1.5f, 0.8f), "", 40f);
        Beside(speed, speed.Length * 0.76f, 40f, PropKind.House, new Vector3(11f, 7f, 9f), "", -20f);  // outside the loop
        Beside(speed, speed.Length * 0.86f, 42f, PropKind.House, new Vector3(10f, 7f, 9f), "", 15f);
        Beside(speed, speed.Length * 0.80f, -40f, PropKind.Flowers, new Vector3(8f, 0.5f, 8f));
        Beside(speed, speed.Length * 0.63f, -46f, PropKind.Shop, new Vector3(14f, 5f, 10f), "", 90f);
        Beside(speed, speed.Length * 0.22f, 48f, PropKind.Hut, new Vector3(5f, 3f, 4f), "THE HUT", 180f);  // east side, speed course
        Beside(speed, speed.Length * 0.12f, 45f, PropKind.House, new Vector3(10f, 7f, 9f), "", -30f);
        Beside(speed, speed.Length * 0.30f, 50f, PropKind.Barn, new Vector3(16f, 8f, 11f));
        Beside(speed, speed.Length * 0.30f + 20f, 44f, PropKind.Cow, new Vector3(2.2f, 1.5f, 0.8f), "", 70f);
        Beside(stunt, stunt.Length * 0.52f, 40f, PropKind.House, new Vector3(10f, 7f, 9f), "", 25f);
        Beside(stunt, stunt.Length * 0.72f, -36f, PropKind.Cow, new Vector3(2.2f, 1.5f, 0.8f));

        // Tree lines: outside the speed route and in the open ground between the branches.
        // Stops short of the merge, where the stunt route runs alongside.
        for (var i = 0; i < 23; i++)
        {
            var d = speed.Length * (0.03f + 0.037f * i);
            var side = i % 3 == 0 ? -1f : 1f;
            Beside(speed, d, side * (24f + 11f * (i % 4)), PropKind.Tree, new Vector3(4.5f + (i % 3), 8f + (i % 4), 4.5f + (i % 3)), $"speed-tree-{i}");
        }
        for (var i = 0; i < 12; i++)
        {
            var d = stunt.Length * (0.05f + 0.075f * i);
            Beside(stunt, d, (i % 2 == 0 ? 1f : -1f) * (26f + 8f * (i % 3)), PropKind.Tree, new Vector3(5f, 9f, 5f), $"stunt-tree-{i}");
        }
        // A wood beyond the north-west corner of the speed loop.
        var corner = speed.FrameAt(speed.Length * 0.74f).Origin;
        for (var i = 0; i < 12; i++)
        {
            At(corner.X - 120f + i * 19f, corner.Z + 140f - (i % 3) * 28f, 0f, PropKind.Tree, new Vector3(5f, 10f, 5f), $"wood-a-{i}");
            At(corner.X - 170f, corner.Z + 100f + i * 30f, 0f, PropKind.Tree, new Vector3(5f, 9f, 5f), $"wood-b-{i}");
        }
    }
}
