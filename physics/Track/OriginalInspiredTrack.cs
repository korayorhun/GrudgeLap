using System.Numerics;

namespace GrudgeLap.Physics.Track;

/// <summary>
/// The default circuit, laid out on the plan of the 1989 course (see remarks): a start straight that bridges the
/// speed route's diagonal just before the line, then climbs at once to the fork on the crest of a bare hill. The
/// speed route carries straight on over the crest, sweeps round the east side in one long right-hander, passes
/// under the open drawbridge, runs the diagonal back over a crest and through the cutting under the start, meets
/// the train and circles the farm to the north-west, counter-clockwise, with the checkpoint on its top. The stunt
/// route drops off the hill to the right past a barn, over a hump to the drawbridge, which jumps the speed route,
/// bends right past the gas station, runs the loop on its westbound straight and the checkpoint nine slabs later,
/// dives through the cutting under its own return leg into the banked 180 and climbs back north over that cutting
/// to the merge. A hidden skid pad lies off the start straight to the left.
/// </summary>
/// <remarks>
/// The plan was traced from the "Original Track" map of the 1990 Hard Drivin' II track editor (stitched by the
/// weltenschule analysis; 48 map pixels to the 88 ft slab) and checked against an attract-mode video of the
/// arcade: the order of the features, the sides of the farm buildings and the three crossings (the start straight
/// over the speed diagonal, the drawbridge over the speed route, the stunt route over itself) are the original's;
/// the segments are straights and arcs fitted to the traced centreline. The speed lap comes to about 127 slabs, the
/// stunt lap to about 91. World frame: x east, z north, the start line at the origin.
/// </remarks>
public static class OriginalInspiredTrack
{
    /// <summary>Where both routes merge, heading east; the start straight runs on from here.</summary>
    private static readonly Vector3 JoinPoint = new(-175f, 0f, 0f);
    public const float PadRadius = 48f;
    /// <summary>The skid pad: off the start straight to the left, as in the original (a hard left at the line).</summary>
    private static readonly Vector3 PadCentre = new(40f, 0f, 110f);
    /// <summary>Radius of the stunt route's right-hander off the fork hill.</summary>
    private const float HillCurveRadius = 65f;
    /// <summary>Length of that curve (a quarter turn), over which both branches drop the 10 m off the crest.</summary>
    private const float HillDescentLength = HillCurveRadius * MathF.PI / 2f;
    /// <summary>Distance along the speed route where the railway crosses: on the diagonal, after the cutting.</summary>
    public const float CrossingDistance = 1640f;

    public static TrackGeometry Build()
    {
        // ---- Final: from the merge over the speed route's diagonal to just behind the line ---------------------
        var final = new TrackBuilder(PathKind.Final, JoinPoint, 90f)
            .Mark(MarkerKind.ReturnPoint, "join")
            .Straight(111f)
            .Overpass(50f, 1.0f)                  // the speed route's cutting passes under it
            .Straight(4f);

        // ---- Common: the line, then the climb to the fork on the crest ----------------------------------------
        // The car starts 10 m behind the line: its first crossing opens the lap.
        var common = TrackBuilder.ContinueFrom(final.Path, PathKind.Common)
            .Straight(10f)
            .Mark(MarkerKind.StartFinish, "START")
            .Mark(MarkerKind.ReturnPoint, "start")
            .Straight(74f)
            .Straight(154f, 10f)
            .Mark(MarkerKind.Sign, "SPEED/STUNT");

        // ---- Speed route --------------------------------------------------------------------------------------
        // Off the crest the branches share their first metres of road, so they drop at the same rate until they
        // have parted: the speed road takes its 10 m over the length of the stunt route's curve off the hill and
        // runs on flat. With a gentler descent of its own it stood up to a metre above the stunt road where the two
        // overlap, and a car taking the stunt route dropped off its edge 25 m past the fork.
        var speed = TrackBuilder.ContinueFrom(common.Path, PathKind.Speed)
            .Straight(HillDescentLength, -10f)
            .Straight(161f - HillDescentLength)
            .Mark(MarkerKind.ReturnPoint, "speed-1")
            .Straight(20f)
            .Mark(MarkerKind.Sign, "90>")
            .Curve(85f, 205f)
            .Curve(65f, 205f)
            .Curve(41f, 155f)
            .Mark(MarkerKind.ReturnPoint, "speed-2")
            .Curve(10f, 600f)
            .Curve(31f, 290f)                     // under the open drawbridge
            .Straight(150f)
            .Hill(140f, 3f)
            .Straight(10f)
            .Mark(MarkerKind.Sign, "DIP")
            .Hill(110f, -4.5f)                    // the cutting under the start straight
            .Straight(25f)
            .Curve(5f, 700f)
            .Curve(18f, 400f)
            .Mark(MarkerKind.Sign, "90<")
            .Curve(-51f, 150f)
            .Mark(MarkerKind.Checkpoint, "speed")
            .Mark(MarkerKind.ReturnPoint, "speed-3")
            .Straight(48f)
            .Curve(-30f, 165f)
            .Curve(-67f, 270f)
            .Mark(MarkerKind.Sign, "90<")
            .Curve(-35f, 270f)
            .Curve(-51f, 151f)
            .Mark(MarkerKind.ReturnPoint, "speed-4")
            .Curve(-45f, 268f);

        // ---- Stunt route --------------------------------------------------------------------------------------
        var stunt = TrackBuilder.ContinueFrom(common.Path, PathKind.Stunt)
            .Curve(90f, HillCurveRadius, 0f, -10f)   // off the hill to the right
            .Mark(MarkerKind.ReturnPoint, "stunt-1")
            .Hill(60f, 3f)                        // the hump at its foot
            .Straight(10f)
            .Curve(20f, 300f)
            .Mark(MarkerKind.Sign, "60")
            .Straight(67f)
            .Drawbridge(30f, 7f, 18f, 34f)        // over the speed route
            .Mark(MarkerKind.ReturnPoint, "stunt-2")
            .Straight(58f)
            .Curve(95f, 83f)                      // round the gas station
            .Straight(40f)
            .Mark(MarkerKind.ReturnPoint, "stunt-3")
            .Mark(MarkerKind.Sign, "60")
            .Straight(25f)
            .Loop(15f, 11f)
            .Mark(MarkerKind.ReturnPoint, "stunt-4")
            .Hill(60f, 2f)
            .Straight(60f)
            .Mark(MarkerKind.Sign, "90<")
            .Curve(-35f, 150f)
            .Straight(40f)
            .Mark(MarkerKind.Checkpoint, "stunt")
            .Straight(40f)
            .Mark(MarkerKind.Sign, "DIP")
            .Hill(110f, -4.5f)                    // the cutting under the return leg
            .Curve(-25f, 150f)
            .Straight(40f)
            .Mark(MarkerKind.Sign, "90<")
            .Curve(-180f, 62f, 32f)
            .Mark(MarkerKind.ReturnPoint, "stunt-5")
            .Curve(-50f, 145f)
            .Straight(6.5f)
            .Overpass(90f, 1.0f)                  // over the bottom straight's cutting
            .Straight(120f)
            .Curve(35f, 250f);

        speed.SplineTo(final.Path.First).JoinTo(final.Path);
        stunt.SplineTo(final.Path.First).JoinTo(final.Path);

        var pad = new SkidPad(PadCentre, PadRadius);
        var track = new TrackGeometry(common.Path, speed.Path, stunt.Path, final.Path, [pad], rollingGround: 0.5f) { Name = "Farm Circuit" };
        PlaceProps(track);
        track.AddBridgePiers();
        track.Props.Add(new TrackProp(PropKind.Tower, pad.Centre with { Y = track.Terrain.Height(pad.Centre.X, pad.Centre.Z) }, 0f, new Vector3(3f, 14f, 3f), "pad-column"));

        var crossing = speed.Path.FrameAt(CrossingDistance);
        var crossingPoint = crossing.Origin with { Y = track.Terrain.Height(crossing.Origin.X, crossing.Origin.Z) };
        // A long passenger train (six cars), 17 m/s, out of sight for half a minute between runs.
        track.Train = new Train(crossingPoint, crossing.Lateral, halfLength: 160f, speed: 17f, cars: 6, pause: 30f, phase: 33f);
        return track;
    }

    /// <summary>
    /// The decor, where the original has it: positions are distances along the routes (lateral metres to the
    /// driver's right, negative to the left), read off the traced plan and the attract video.
    /// </summary>
    private static void PlaceProps(TrackGeometry track)
    {
        var props = track.Props;
        var terrain = track.Terrain;
        var speed = track.Speed;
        var stunt = track.Stunt;
        var sign = new Vector3(1.2f, 2.6f, 0.2f);
        var cow = new Vector3(2.2f, 1.5f, 0.8f);
        var barrier = new Vector3(2.2f, 1.0f, 0.5f);

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

        // A roadside tree, unless that spot is near another road (where the routes cross or run close).
        void Tree(TrackPath path, float distance, float lateral, Vector3 size, string label)
        {
            var f = path.FrameAt(distance);
            var pos = f.Origin + f.Lateral * lateral;
            if (track.ClearanceFromRoads(pos) < 16f) return;
            At(pos.X, pos.Z, 0f, PropKind.Tree, size, label);
        }

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

        // Signs, gantries and the checkpoint banners come from the markers, so they move with the layout.
        foreach (var path in track.Paths)
        {
            foreach (var m in path.Markers)
            {
                if (m.Kind == MarkerKind.Sign)
                {
                    if (m.Label == "SPEED/STUNT") Beside(path, m.Distance, 0f, PropKind.Signpost, sign, m.Label);
                    else Beside(path, m.Distance, 8.5f, PropKind.Sign, sign, m.Label);
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

        // ---- Stunt route ---------------------------------------------------------------------------------------
        // Chevrons on the outside of the curve off the hill, from where it has pulled clear of the speed road, and
        // the barn with its cows at the road's edge on the left at its foot (the original's car could be put into it).
        foreach (var d in new[] { 52f, 66f, 80f, 94f })
        {
            Beside(stunt, d, -8.5f, PropKind.Sign, sign, "->");
        }
        Beside(stunt, 112f, -20f, PropKind.Barn, new Vector3(16f, 8f, 11f), "33", 90f);
        Beside(stunt, 126f, -13f, PropKind.Cow, cow, "", 60f);
        Beside(stunt, 98f, -15f, PropKind.Cow, cow, "", -30f);
        // Sawhorses on both sides of the drawbridge's approach, and before the loop and on its exit hump: kept 20 m
        // clear of the loop itself, whose descending half sits behind its entry and beside its exit.
        foreach (var d in new[] { 300f, 316f, 332f, 642f, 654f, 812f, 826f, 840f })
        {
            Beside(stunt, d, 8.6f, PropKind.Barrier, barrier);
            Beside(stunt, d, -8.6f, PropKind.Barrier, barrier);
        }
        Beside(stunt, 812f, 9f, PropKind.Sign, sign, "60");
        // The gas station inside the bend after the bridge, its pumps towards the road; the hut across the road.
        Beside(stunt, 586f, 32f, PropKind.Shop, new Vector3(14f, 5f, 10f), "GAS", -90f);
        Beside(stunt, 580f, 21f, PropKind.Pump, new Vector3(1.0f, 1.8f, 0.6f), "", -90f);
        Beside(stunt, 592f, 21f, PropKind.Pump, new Vector3(1.0f, 1.8f, 0.6f), "", -90f);
        Beside(stunt, 548f, -26f, PropKind.Hut, new Vector3(5f, 3f, 4f), "THE HUT", 180f);
        Beside(stunt, 620f, -30f, PropKind.Flowers, new Vector3(8f, 0.5f, 8f));
        // A white house on the open ground west of the return leg, a cow by the bottom straight.
        Beside(stunt, 1860f, -42f, PropKind.House, new Vector3(11f, 7f, 9f), "white", 25f);
        Beside(stunt, 960f, 30f, PropKind.Cow, cow, "", 40f);
        Beside(stunt, 1760f, 28f, PropKind.Flowers, new Vector3(7f, 0.5f, 7f));

        // ---- Speed route ---------------------------------------------------------------------------------------
        // The farm on the left, outside the first corner of the east sweep: the orange house, the barn and its cows.
        Beside(speed, 262f, -38f, PropKind.House, new Vector3(10f, 7f, 9f), "orange", -30f);
        Beside(speed, 318f, -42f, PropKind.Barn, new Vector3(16f, 8f, 11f), "33", 10f);
        Beside(speed, 340f, -32f, PropKind.Cow, cow, "", 70f);
        Beside(speed, 330f, -55f, PropKind.Cow, cow, "", -20f);
        // Crossbucks either side of the level crossing on the diagonal.
        Beside(speed, CrossingDistance - 40f, 8.5f, PropKind.Crossbuck, new Vector3(1.6f, 3.2f, 0.2f), "RR");
        Beside(speed, CrossingDistance - 40f, -8.5f, PropKind.Crossbuck, new Vector3(1.6f, 3.2f, 0.2f), "RR");
        Beside(speed, 1180f, 34f, PropKind.Flowers, new Vector3(8f, 0.5f, 8f));
        Beside(speed, 1560f, -30f, PropKind.Flowers, new Vector3(7f, 0.5f, 7f));
        // North-west loop, counter-clockwise: the barn and its cows on the right just after the checkpoint, the
        // houses on the right (outside) down the west side.
        Beside(speed, 1885f, 40f, PropKind.Barn, new Vector3(18f, 9f, 12f), "33", 5f);
        Beside(speed, 1905f, 30f, PropKind.Cow, cow, "", 40f);
        Beside(speed, 1868f, 52f, PropKind.Cow, cow, "", -50f);
        Beside(speed, 2290f, 46f, PropKind.House, new Vector3(10f, 7f, 9f), "orange", 15f);
        Beside(speed, 2400f, 44f, PropKind.House, new Vector3(11f, 7f, 9f), "white", -20f);
        Beside(speed, 2150f, -36f, PropKind.Flowers, new Vector3(8f, 0.5f, 8f));

        // ---- Fields: inside and outside the east sweep, round the north-west loop, as on the original's plan --------
        Field(speed, 300f, -95f, 140f, 110f, "hay");
        Field(speed, 520f, -80f, 120f, 110f, "plough");
        Field(speed, 690f, -85f, 130f, 100f, "crop");
        Field(speed, 420f, 110f, 120f, 100f, "crop");
        Field(speed, 600f, 105f, 110f, 90f, "plough");
        Field(speed, 1990f, 100f, 150f, 120f, "crop");
        Field(speed, 2160f, 110f, 130f, 110f, "plough");
        Field(speed, 2360f, 100f, 120f, 110f, "hay");
        Field(speed, 2510f, 105f, 120f, 100f, "crop");
        Field(speed, 2120f, -130f, 150f, 120f, "crop");
        Field(speed, 2690f, 95f, 110f, 110f, "plough");
        Field(stunt, 1880f, -120f, 120f, 90f, "crop");

        // A few trees along the roads (the original's plain is open; the wood stands back at the horizon), and
        // hedgerows along the long side of each field that faces away from the road.
        for (var i = 0; i < 14; i++)
        {
            var d = speed.Length * (0.04f + 0.066f * i);
            var side = i % 3 == 0 ? -1f : 1f;
            Tree(speed, d, side * (26f + 10f * (i % 4)), new Vector3(4.5f + (i % 3), 8f + (i % 4), 4.5f + (i % 3)), $"speed-tree-{i}");
        }
        for (var i = 0; i < 8; i++)
        {
            var d = stunt.Length * (0.08f + 0.11f * i);
            Tree(stunt, d, (i % 2 == 0 ? 1f : -1f) * (28f + 8f * (i % 3)), new Vector3(5f, 9f, 5f), $"stunt-tree-{i}");
        }
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
