using System.Text.Json;
using Godot;

namespace GrudgeLap.Game;

/// <summary>
/// Writes the generated track world to a glTF binary (<c>--export-world=&lt;file.glb&gt;</c>): terrain, road,
/// shoulders, lines, bridges, buildings, signs, trees, the train and the player's car at the start, so that
/// another engine can be dressed around the same circuit. A JSON file beside it (same name, <c>.json</c>) lists
/// what that engine should place itself: every grass tuft, every prop by kind, and the cockpit camera.
/// </summary>
/// <remarks>
/// Copies go into a scene of their own, without the camera, lights, sky, the far ground plane, the drones or the
/// grass multimeshes (tens of thousands of instances another engine scatters better from the JSON); the live
/// world is not touched. Positions are metres on Godot axes (+Y up, -Z forward, +X right).
/// </remarks>
internal static class WorldExport
{
    public static Error Write(Node root, string path, CarView car, Camera3D camera)
    {
        var scene = new Node3D { Name = "GrudgeLapWorld" };
        var grass = new List<float[]>();
        var forest = new List<float[]>();
        var forestMeshes = new SortedDictionary<int, Mesh>();
        var props = new List<object>();
        foreach (var child in root.GetChildren())
        {
            // The clouds are a sheet for this engine's sky; the other builds its own.
            if (child is not Node3D node || node is CameraRig or Light3D or Camera3D || node.Name == "FarGround" || node.Name == "Clouds") continue;
            if (node is CarView && node != car) continue;
            foreach (var multimesh in Descendants(node).OfType<MultiMeshInstance3D>())
            {
                // The wood's trees go out as placements of their variant meshes (Forest-<band>-<variant>), the grass as tufts.
                var multimeshName = multimesh.Name.ToString();
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
        }

        // One of each tree of the wood travels in the file, named for the placements to find (ForestVariant-<n>).
        foreach (var (variant, mesh) in forestMeshes) scene.AddChild(new MeshInstance3D { Mesh = mesh, Name = $"ForestVariant-{variant}" });

        var document = new GltfDocument();
        var state = new GltfState();
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
        };
        System.IO.File.WriteAllText(System.IO.Path.ChangeExtension(path, ".json"), JsonSerializer.Serialize(placement));
        GD.Print(error == Error.Ok ? $"world exported: {path} ({scene.GetChildCount()} nodes, {props.Count} props, {grass.Count} grass tufts, {forest.Count} trees of the wood)" : $"world export failed: {error}");
        scene.QueueFree();
        return error;
    }

    private static float[] Vec(Vector3 v) => [v.X, v.Y, v.Z];

    private static IEnumerable<Node> Descendants(Node node)
    {
        foreach (var child in node.GetChildren())
        {
            yield return child;
            foreach (var grandchild in Descendants(child)) yield return grandchild;
        }
    }
}
