using System.Numerics;

namespace GrudgeLap.Physics.Track;

/// <summary>
/// The default circuit, laid out in the spirit of the 1989 course: a start straight under a green gantry that
/// bridges over the speed route's return leg, a long hill with the fork on its crest, a speed route that
/// swings round the east side past a barn and its cows, crosses a railway, dives back diagonally under the
/// start through two dips and circles the farm to the north-west, a stunt route that drops off the hill past
/// a barn to the open drawbridge, bridges over the speed route, passes the shop with its pumps, runs the loop
/// on its bottom straight with the checkpoint gate just after it, swings through the banked 180 and climbs
/// back north to the merge, and a hidden skid pad round a column in the open ground off the speed route.
/// Farm fields, houses, the shop, the camouflaged hut, flowers, yellow signs that tip over, sawhorse barriers
/// and the rail-less train are the decor of the original, met in its order; geometry is ours, only the rhythm
/// is borrowed.
/// </summary>
public static class OriginalInspiredTrack
{
    /// <summary>Where both routes merge, heading 60; the road then bends to east and runs to the start line.</summary>
    private static readonly Vector3 JoinPoint = new(-60f, 0f, -30f);
    public const float PadRadius = 48f;
    /// <summary>Distance along the speed route where the railway crosses: on the diagonal, before the first dip.</summary>
    public const float CrossingDistance = 918f + 70f;

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

        // ---- Speed route: down the hill, round the east side, the crossing and the diagonal with its dips under the start, the big loop ----
        var speed = TrackBuilder.ContinueFrom(common.Path, PathKind.Speed)
            .Straight(161f, -10f)
            .Mark(MarkerKind.ReturnPoint, "speed-1")
            .Curve(45f, 130f)
            .Straight(120f)
            .Curve(45f, 130f)
            .Mark(MarkerKind.Sign, "90")
            .Straight(130f)
            .Mark(MarkerKind.Sign, "40")
            .Mark(MarkerKind.Sign, "->")
            .Curve(90f, 85f)
            .Mark(MarkerKind.ReturnPoint, "speed-2")
            .Straight(100f)
            .Curve(40f, 100f)
            .Mark(MarkerKind.Sign, "RR")
            .Straight(300f)                       // the diagonal: 856 m; the railway crosses it, then it passes under the start straight's bridge
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
            .Mark(MarkerKind.Sign, "<-")
            .Curve(-90f, 95f)
            .Straight(100f)
            .Mark(MarkerKind.Sign, "40")
            .Mark(MarkerKind.Sign, "<-")
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
            .Straight(260f)                       // past the shop with its pumps, the loop ahead
            .Mark(MarkerKind.Sign, "60")
            .Loop(15f, 11f)
            .Mark(MarkerKind.ReturnPoint, "stunt-4")
            .Straight(60f)
            .Mark(MarkerKind.Checkpoint, "stunt")   // after the loop, as the original's checkpoint gate came
            .Straight(60f)
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
        // The column the pad is driven round, and a plain road sign pointing the curious off the road toward it.
        track.Props.Add(new TrackProp(PropKind.Tower, pad.Centre, 0f, new Vector3(3f, 14f, 3f), "pad-column"));
        var hint = speed.Path.FrameAt(PadHintDistance(draft, pad));
        var hintSide = MathF.Sign(Vector3.Dot(pad.Centre - hint.Origin, hint.Lateral));
        var hintPos = hint.Origin + hint.Lateral * (hintSide * 8.5f);
        hintPos.Y = track.Terrain.Height(hintPos.X, hintPos.Z);
        track.Props.Add(new TrackProp(PropKind.Sign, hintPos, MathF.Atan2(hint.Tangent.X, hint.Tangent.Z) * MathUtil.Rad2Deg, new Vector3(1.2f, 2.6f, 0.2f), hintSide > 0 ? "PAD >" : "< PAD"));

        // The train: crosses the diagonal at right angles, 17 m/s, out of sight for a while between runs.
        var crossing = speed.Path.FrameAt(CrossingDistance);
        var crossingPoint = crossing.Origin with { Y = track.Terrain.Height(crossing.Origin.X, crossing.Origin.Z) };
        // Visible for 160 m either side of the road; parked out of sight for half a minute between runs. The
        // phase puts the first run at 32 s, so the scripted driver (and the lap tests) meet an empty crossing.
        track.Train = new Train(crossingPoint, crossing.Lateral, halfLength: 160f, speed: 17f, cars: 4, pause: 30f, phase: 33f);
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
                    if (!prop.Solid && prop.Kind != PropKind.Field) continue;
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

        // A field beside the road: slid outward and shrunk until the whole rectangle keeps clear of every road.
        void Field(TrackPath path, float distance, float lateral, float width, float depth, string label)
        {
            var f = path.FrameAt(distance);
            var across = MathUtil.SafeNormalize(new Vector3(f.Lateral.X, 0f, f.Lateral.Z), Vector3.UnitX);
            var along = MathUtil.SafeNormalize(new Vector3(f.Tangent.X, 0f, f.Tangent.Z), Vector3.UnitZ);
            var yaw = MathF.Atan2(f.Tangent.X, f.Tangent.Z) * MathUtil.Rad2Deg;
            foreach (var shift in new[] { 0f, 15f, 30f, 45f, 60f, 80f })
            {
                foreach (var scale in new[] { 1f, 0.85f, 0.7f, 0.55f })
                {
                    var centre = f.Origin + across * (lateral + MathF.Sign(lateral) * shift);
                    var w = width * scale;
                    var d = depth * scale;
                    var clear = true;
                    for (var u = -0.5f; u <= 0.5f && clear; u += 0.1f)
                    {
                        for (var v = -0.5f; v <= 0.5f; v += 0.1f)
                        {
                            if (track.ClearanceFromRoads(centre + across * (u * w) + along * (v * d)) < TrackBuilder.DefaultHalfWidth + 6f) { clear = false; break; }
                        }
                    }
                    if (!clear) continue;
                    props.Add(new TrackProp(PropKind.Field, centre with { Y = terrain.Height(centre.X, centre.Z) }, yaw, new Vector3(w, 0f, d), label));
                    return;
                }
            }
        }

        // Signs come straight from the markers so they stay in sync with the layout; arrows and RR stand on both sides.
        foreach (var path in track.Paths)
        {
            foreach (var m in path.Markers)
            {
                if (m.Kind == MarkerKind.Sign)
                {
                    if (m.Label == "SPEED/STUNT")
                    {
                        Beside(path, m.Distance, 0f, PropKind.Signpost, new Vector3(1.2f, 2.6f, 0.2f), m.Label);
                    }
                    else if (m.Label == "RR")
                    {
                        Beside(path, m.Distance, 8.5f, PropKind.Crossbuck, new Vector3(1.6f, 3.2f, 0.2f), m.Label);
                        Beside(path, m.Distance, -8.5f, PropKind.Crossbuck, new Vector3(1.6f, 3.2f, 0.2f), m.Label);
                    }
                    else
                    {
                        Beside(path, m.Distance, 8.5f, PropKind.Sign, new Vector3(1.2f, 2.6f, 0.2f), m.Label);
                        if (m.Label is "->" or "<-") Beside(path, m.Distance, -8.5f, PropKind.Sign, new Vector3(1.2f, 2.6f, 0.2f), m.Label);
                    }
                }
                else if (m.Kind == MarkerKind.Checkpoint)
                {
                    Beside(path, m.Distance, 0f, PropKind.Signpost, new Vector3(14f, 5f, 0.4f), "CHECKPOINT");
                }
                else if (m.Kind == MarkerKind.StartFinish)
                {
                    Beside(path, m.Distance, 0f, PropKind.Gate, new Vector3(16f, 6f, 0.5f), "START");
                }
            }
        }

        var speed = track.Speed;
        var stunt = track.Stunt;
        var common = track.Common;

        // ---- East side (speed route): the farm after the first curves ----------------------------------------
        Beside(speed, speed.Length * 0.12f, 45f, PropKind.House, new Vector3(10f, 7f, 9f), "orange", -30f);
        Beside(speed, speed.Length * 0.22f, 48f, PropKind.Hut, new Vector3(5f, 3f, 4f), "THE HUT", 180f);
        Beside(speed, speed.Length * 0.30f, 50f, PropKind.Barn, new Vector3(16f, 8f, 11f), "33");
        Beside(speed, speed.Length * 0.30f + 20f, 44f, PropKind.Cow, new Vector3(2.2f, 1.5f, 0.8f), "", 70f);
        Beside(speed, speed.Length * 0.30f + 12f, 62f, PropKind.Cow, new Vector3(2.2f, 1.5f, 0.8f), "", -20f);
        Field(speed, speed.Length * 0.27f, 120f, 110f, 90f, "plough");
        Field(speed, speed.Length * 0.165f, 118f, 70f, 60f, "crop");
        Beside(speed, speed.Length * 0.08f, -60f, PropKind.Flowers, new Vector3(8f, 0.5f, 8f));

        // ---- The diagonal: the level crossing, flowers -------------------------------------------------------
        Beside(speed, speed.Length * 0.41f, 40f, PropKind.Flowers, new Vector3(8f, 0.5f, 8f));
        Beside(speed, speed.Length * 0.52f, 36f, PropKind.Flowers, new Vector3(7f, 0.5f, 7f));

        // ---- North-west loop: barn, cows, houses, fields ------------------------------------------------------
        Beside(speed, speed.Length * 0.70f, -45f, PropKind.Barn, new Vector3(18f, 9f, 12f), "33");
        Beside(speed, speed.Length * 0.70f + 22f, -40f, PropKind.Cow, new Vector3(2.2f, 1.5f, 0.8f));
        Beside(speed, speed.Length * 0.70f + 34f, -52f, PropKind.Cow, new Vector3(2.2f, 1.5f, 0.8f), "", 40f);
        Beside(speed, speed.Length * 0.76f, 40f, PropKind.House, new Vector3(11f, 7f, 9f), "white", -20f);
        Beside(speed, speed.Length * 0.86f, 42f, PropKind.House, new Vector3(10f, 7f, 9f), "orange", 15f);
        Beside(speed, speed.Length * 0.80f, -40f, PropKind.Flowers, new Vector3(8f, 0.5f, 8f));
        Field(speed, speed.Length * 0.74f, -130f, 120f, 90f, "hay");
        Field(speed, speed.Length * 0.66f, -110f, 90f, 70f, "crop");

        // ---- Stunt side, in the original's order: the barn before the drawbridge, the shop with its pumps between the
        // bridge and the loop, a house past the banked curve, a cow by the bottom straight, barriers at the fork -------
        Beside(stunt, 120f, -48f, PropKind.Barn, new Vector3(16f, 8f, 11f), "33", 20f);
        Beside(stunt, 138f, -40f, PropKind.Cow, new Vector3(2.2f, 1.5f, 0.8f), "", -60f);
        Beside(stunt, 112f, -62f, PropKind.Cow, new Vector3(2.2f, 1.5f, 0.8f), "", 30f);
        Beside(stunt, stunt.Length * 0.34f, 46f, PropKind.Shop, new Vector3(14f, 5f, 10f), "GAS", -90f);
        Beside(stunt, stunt.Length * 0.34f + 9f, 34f, PropKind.Pump, new Vector3(1.0f, 1.8f, 0.6f), "", -90f);
        Beside(stunt, stunt.Length * 0.34f + 4f, 34f, PropKind.Pump, new Vector3(1.0f, 1.8f, 0.6f), "", -90f);
        Beside(stunt, stunt.Length * 0.52f, 40f, PropKind.House, new Vector3(10f, 7f, 9f), "white", 25f);
        Beside(stunt, stunt.Length * 0.72f, -36f, PropKind.Cow, new Vector3(2.2f, 1.5f, 0.8f));
        Field(stunt, stunt.Length * 0.44f, 85f, 130f, 80f, "plough");
        Field(stunt, stunt.Length * 0.62f, 90f, 100f, 70f, "crop");
        foreach (var d in new[] { 40f, 52f, 64f })
        {
            Beside(stunt, d, 9.5f, PropKind.Barrier, new Vector3(2.2f, 1.0f, 0.5f));
        }

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
        // A wood beyond the north-west corner of the speed loop, and hedgerows along the fields.
        var corner = speed.FrameAt(speed.Length * 0.74f).Origin;
        for (var i = 0; i < 12; i++)
        {
            At(corner.X - 120f + i * 19f, corner.Z + 140f - (i % 3) * 28f, 0f, PropKind.Tree, new Vector3(5f, 10f, 5f), $"wood-a-{i}");
            At(corner.X - 170f, corner.Z + 100f + i * 30f, 0f, PropKind.Tree, new Vector3(5f, 9f, 5f), $"wood-b-{i}");
        }
        // Hedgerows along the long side of each field that faces away from the road.
        foreach (var field in props.Where(p => p.Kind == PropKind.Field).ToList())
        {
            var yaw = field.YawDeg * MathUtil.Deg2Rad;
            var axisX = new Vector3(MathF.Cos(yaw), 0f, -MathF.Sin(yaw));
            var axisZ = new Vector3(MathF.Sin(yaw), 0f, MathF.Cos(yaw));
            var outer = field.Position + axisX * (field.Size.X * 0.5f + 7f);
            var inner = field.Position - axisX * (field.Size.X * 0.5f + 7f);
            var side = track.ClearanceFromRoads(outer) > track.ClearanceFromRoads(inner) ? 1f : -1f;
            var n = (int)(field.Size.Z / 14f);
            for (var i = 0; i < n; i++)
            {
                var p = field.Position + axisX * (side * (field.Size.X * 0.5f + 7f)) + axisZ * (-field.Size.Z * 0.5f + 7f + i * 14f);
                if (track.ClearanceFromRoads(p) < 16f) continue;
                At(p.X, p.Z, 0f, PropKind.Tree, new Vector3(4f + (i % 3), 7f + (i % 2) * 2f, 4f + (i % 3)), $"hedge-{field.Label}-{i}");
            }
        }
    }
}
