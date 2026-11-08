using System.Text.Json;
using Godot;

namespace GrudgeLap.Game;

/// <summary>
/// Writes the generated track world to a glTF binary (<c>--export-world=&lt;file.glb&gt;</c>): terrain, road,
/// shoulders, lines, bridges, buildings, signs, trees, the train and the player's car at the start, so that
/// another engine can be dressed around the same circuit. A JSON file beside it (same name, <c>.json</c>) lists
/// what that engine should place itself: every grass tuft, every prop by kind, the roads' centrelines and the cockpit camera.
/// </summary>
/// <remarks>
/// Copies go into a scene of their own, without the camera, lights, sky, the far ground plane, the drones or the
/// grass multimeshes (tens of thousands of instances another engine scatters better from the JSON); the live
/// world is not touched. Positions are metres on Godot axes (+Y up, -Z forward, +X right).
/// </remarks>
internal static class WorldExport
{
    public static Error Write(Node root, string path, CarView car, Camera3D camera, GrudgeLap.Physics.Track.TrackGeometry track)
    {
        var scene = new Node3D { Name = "GrudgeLapWorld" };
        var grass = new List<float[]>();
        var forest = new List<float[]>();
        var forestMeshes = new SortedDictionary<int, Mesh>();
        var props = new List<object>();
        var instanced = new List<object>();
        var templates = new List<(string Name, Mesh Mesh, Material? Material)>();
        foreach (var child in root.GetChildren())
        {
            // The clouds are a sheet for this engine's sky; the other builds its own.
            if (child is not Node3D node || node is CameraRig or Light3D or Camera3D || node.Name == "FarGround" || node.Name == "Clouds") continue;
            // With the real land (0.37) the old ring of mountains is hidden and stays behind; the land goes out instead.
            if (node.Name == "Mountains" && !node.Visible) continue;
            if (node is CarView && node != car) continue;
            foreach (var multimesh in Descendants(node).OfType<MultiMeshInstance3D>())
            {
                // The wood's trees go out as placements of their variant meshes (Forest-<band>-<variant>), the grass as tufts.
                var multimeshName = multimesh.Name.ToString();
                // The fences, the power poles and the marker posts with their reflectors (0.76) go out as placements of a
                // template mesh each, which the file carries once (Instanced-<n>); until then they went out as grass tufts.
                if (InstancedGroup(multimesh, node) is { } group && multimesh.Multimesh.Mesh is { } instancedMesh)
                {
                    // One template per mesh and material: the fence's sixty-odd chunks share one bay.
                    var known = templates.FindIndex(e => e.Mesh == instancedMesh && e.Material == multimesh.MaterialOverride);
                    var template = known >= 0 ? templates[known].Name : $"Instanced-{templates.Count}";
                    if (known < 0) templates.Add((template, instancedMesh, multimesh.MaterialOverride));
                    var transforms = new List<float[]>();
                    var colours = multimesh.Multimesh.UseColors ? new List<float[]>() : null;
                    for (var i = 0; i < multimesh.Multimesh.InstanceCount; i++)
                    {
                        var t = multimesh.GlobalTransform * multimesh.Multimesh.GetInstanceTransform(i);
                        transforms.Add([t.Basis.X.X, t.Basis.X.Y, t.Basis.X.Z, t.Basis.Y.X, t.Basis.Y.Y, t.Basis.Y.Z, t.Basis.Z.X, t.Basis.Z.Y, t.Basis.Z.Z, t.Origin.X, t.Origin.Y, t.Origin.Z]);
                        if (colours is not null)
                        {
                            var c = multimesh.Multimesh.GetInstanceColor(i);
                            colours.Add([c.R, c.G, c.B]);
                        }
                    }
                    instanced.Add(new { group, name = multimeshName, mesh = template, transforms, colours });
                    continue;
                }
                var isForest = multimeshName.StartsWith("Forest-", StringComparison.Ordinal);
                var variant = isForest ? int.Parse(multimeshName[(multimeshName.LastIndexOf('-') + 1)..], System.Globalization.CultureInfo.InvariantCulture) : 0;
                if (isForest && multimesh.Multimesh.Mesh is { } treeMesh) forestMeshes.TryAdd(variant, treeMesh);
                for (var i = 0; i < multimesh.Multimesh.InstanceCount; i++)
                {
                    var t = multimesh.GlobalTransform * multimesh.Multimesh.GetInstanceTransform(i);
                    var forward = -t.Basis.Z;
                    if (isForest) forest.Add([t.Origin.X, t.Origin.Y, t.Origin.Z, Mathf.Atan2(forward.X, -forward.Z), t.Basis.X.Length(), t.Basis.Y.Length(), variant]);
                    else grass.Add([t.Origin.X, t.Origin.Y, t.Origin.Z, Mathf.Atan2(forward.X, -forward.Z), t.Basis.Y.Length()]);
                }
            }
            var name = node.Name.ToString();
            var dash = name.IndexOf('-');
            if (dash > 0 && Enum.TryParse<GrudgeLap.Physics.Track.PropKind>(name[..dash], out var kind))
            {
                props.Add(new { kind = kind.ToString(), label = name[(dash + 1)..], position = Vec(node.GlobalPosition), yaw = node.GlobalRotation.Y });
            }
            if (node.Duplicate() is not Node3D copy) continue;
            foreach (var multimesh in Descendants(copy).OfType<MultiMeshInstance3D>().ToList()) multimesh.Free();
            if (copy is MultiMeshInstance3D) continue;
            scene.AddChild(copy);
            copy.Transform = node.Transform;
            if (node == car) copy.Name = "PlayerCar";
            // The train between its runs stands hidden where it was built, at the start line; the other engine keeps it
            // for its own game to move, so the copy is parked 500 m under the plain, out of any still (0.13).
            if (node is TrainView && !node.Visible) copy.Position += new Vector3(0f, -500f, 0f);
        }

        // One of each tree of the wood travels in the file, named for the placements to find (ForestVariant-<n>).
        foreach (var (variant, mesh) in forestMeshes) scene.AddChild(new MeshInstance3D { Mesh = mesh, Name = $"ForestVariant-{variant}" });
        // And one of each instanced piece of dressing, in its multimesh's material (Instanced-<n>, 0.76), and a metre of
        // power-line wire along +Y (Instanced-Wire) for the other engine to string between the poles: the game's own wires
        // are ribbons its wire shader turns to the camera, which glTF cannot carry.
        foreach (var (name, mesh, material) in templates) scene.AddChild(new MeshInstance3D { Mesh = mesh, Name = name, MaterialOverride = material });
        if (templates.Count > 0)
        {
            scene.AddChild(new MeshInstance3D
            {
                Name = "Instanced-Wire",
                Mesh = new CylinderMesh { TopRadius = 0.012f, BottomRadius = 0.012f, Height = 1f, RadialSegments = 6, Rings = 1 },
                MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.12f, 0.12f, 0.13f), Metallic = 0.6f, Roughness = 0.5f },
            });
        }

        // The roads' centrelines every 8 m (0.13): position, tangent and half width of each frame on earth, so the other
        // engine can scatter its own wear (cracks, patches, oil, skid marks) along them.
        var roads = new List<object>();
        foreach (var trackPath in track.Paths)
        {
            for (var d = 0f; d < trackPath.Length; d += 8f)
            {
                var f = trackPath.FrameAt(d);
                if (f.Surface != GrudgeLap.Physics.Vehicle.SurfaceType.Road) continue;
                roads.Add(new { path = trackPath.Kind.ToString(), distance = d, position = Vec(Convert.ToGodot(f.Origin)), tangent = Vec(Convert.ToGodot(f.Tangent)), halfWidth = f.HalfWidth, kind = f.Kind.ToString() });
            }
        }

        // Disposed here, not left to the finalizer: the export quits the game right after, and a GltfState finalized
        // once the engine had shut down crashed the process on exit (access violation in GodotObject.Dispose).
        using var document = new GltfDocument();
        using var state = new GltfState();
        var error = document.AppendFromScene(scene, state);
        if (error == Error.Ok)
        {
            var directory = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) System.IO.Directory.CreateDirectory(directory);
            error = document.WriteToFilesystem(state, path);
        }
        var eye = camera.GlobalTransform;
        var placement = new
        {
            units = "metres; Godot axes: +Y up, -Z forward, +X right",
            car = new { position = Vec(car.GlobalPosition), basis = new[] { Vec(car.GlobalBasis.X), Vec(car.GlobalBasis.Y), Vec(car.GlobalBasis.Z) } },
            camera = new { eye = Vec(eye.Origin), forward = Vec(-eye.Basis.Z), up = Vec(eye.Basis.Y), fov = camera.Fov },
            props,
            grass,
            forest,
            roads,
            instanced,
        };
        System.IO.File.WriteAllText(System.IO.Path.ChangeExtension(path, ".json"), JsonSerializer.Serialize(placement));
        GD.Print(error == Error.Ok ? $"world exported: {path} ({scene.GetChildCount()} nodes, {props.Count} props, {grass.Count} grass tufts, {forest.Count} trees of the wood)" : $"world export failed: {error}");
        scene.QueueFree();
        return error;
    }

    private static float[] Vec(Vector3 v) => [v.X, v.Y, v.Z];

    /// <summary>The kind of instanced dressing a multimesh is ("Fences", "PowerLines", "MarkerPosts"), from its ancestors' names
    /// up to the top-level node; null for anything else (the grass, the meadow and the wood go out as before).</summary>
    private static string? InstancedGroup(Node multimesh, Node top)
    {
        for (var n = multimesh.GetParent(); n is not null; n = n.GetParent())
        {
            var name = n.Name.ToString();
            if (name is "Fences" or "PowerLines") return name;
            if (name.StartsWith("MarkerPosts-", StringComparison.Ordinal)) return "MarkerPosts";
            if (n == top) break;
        }
        return null;
    }

    private static IEnumerable<Node> Descendants(Node node)
    {
        foreach (var child in node.GetChildren())
        {
            yield return child;
            foreach (var grandchild in Descendants(child)) yield return grandchild;
        }
    }
}
