using Godot;

namespace GrudgeLap.Game;

/// <summary>
/// Writes the generated track world (terrain, road, shoulders, lines, bridges, buildings, signs, trees and the
/// train) to a glTF binary (<c>--export-world=&lt;file.glb&gt;</c>), so that another engine can be dressed
/// around the same circuit.
/// </summary>
/// <remarks>
/// Copies go into a scene of their own, without the cars, the camera, lights, sky, the far ground plane or the
/// grass multimeshes (tens of thousands of instances another engine scatters better itself); the live world is
/// not touched. Positions stay in metres on Godot axes (+Y up, -Z forward); importers convert.
/// </remarks>
internal static class WorldExport
{
    public static Error Write(Node root, string path)
    {
        var scene = new Node3D { Name = "GrudgeLapWorld" };
        foreach (var child in root.GetChildren())
        {
            if (child is not Node3D node || node is CarView or CameraRig or Light3D or Camera3D || node.Name == "FarGround") continue;
            if (node.Duplicate() is not Node3D copy) continue;
            foreach (var multimesh in Descendants(copy).OfType<MultiMeshInstance3D>().ToList()) multimesh.Free();
            if (copy is MultiMeshInstance3D) continue;
            scene.AddChild(copy);
            copy.Transform = node.Transform;
        }
        var document = new GltfDocument();
        var state = new GltfState();
        var error = document.AppendFromScene(scene, state);
        if (error == Error.Ok)
        {
            var directory = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) System.IO.Directory.CreateDirectory(directory);
            error = document.WriteToFilesystem(state, path);
        }
        GD.Print(error == Error.Ok ? $"world exported: {path} ({scene.GetChildCount()} nodes)" : $"world export failed: {error}");
        scene.QueueFree();
        return error;
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
