using Godot;
using GrudgeLap.Physics;
using GrudgeLap.Physics.Vehicle;

namespace GrudgeLap.Game;

/// <summary>
/// The real car model (0.16): a CC BY model of the catalogue car (tools/car_models.json, credited in THIRD_PARTY.md),
/// prepared by tools/prepare_car_models.py into <c>res://assets/models/&lt;car&gt;.glb</c> already in this node's frame
/// (faced to -Z, scaled to the simulation's wheelbase, its hubs at the rest hub height), and drawn in place of the
/// lofted body when the build has it. A checkout without the prepared models draws the lofted bodies as before.
/// </summary>
/// <remarks>
/// The prepared scene names what the game needs: Body (every mesh that does not turn), Wheel0..3 (hung here on the
/// spinning pivots), Steering/Spin (turned with the steering), Eye, Dials (the live instrument panel goes there) and
/// Mirror (the rear-view mirror's glass). A model without a cabin of its own has no Eye: the lofted cabin is built
/// inside its shell instead. The paint, the windows and the clear lenses get the game's own materials by name
/// (GLP_Paint, GLP_Glass, GLP_Lens); everything else keeps the model's, GLP_Lining too (the painted faces inside the
/// cabin that tools/prepare_car_models.py gave a dark trim).
/// </remarks>
internal sealed partial class CarView
{
    /// <summary>The lofted bodies instead of the models (<c>--lofted</c>), for comparisons.</summary>
    public static bool UseModels { get; set; } = true;

    private Node3D? _model;
    private Vector3? _modelEye;
    private Node3D? _modelMirror;
    private bool _modelMirrorHung;    // the game's own housing hangs there (the model's is out of view or missing)
    private Aabb _modelBounds;
    private readonly List<(MeshInstance3D Mesh, int Surface)> _windows = [];
    private static StandardMaterial3D? _windowOutside;
    private static StandardMaterial3D? _windowInside;
    private static StandardMaterial3D? _lens;
    private static Shader? _paintShader;
    private readonly List<ModelDent> _modelDents = [];
    private readonly List<BaseMaterial3D> _brakeLamps = [];
    private bool _braking;

    /// <summary>
    /// The brake lamps of a real model (tools/car_models.json "brake"): lit while the driver brakes. Set every frame;
    /// only a change touches the materials.
    /// </summary>
    public bool Braking
    {
        set
        {
            if (value == _braking) return;
            _braking = value;
            foreach (var lamp in _brakeLamps) lamp.EmissionEnergyMultiplier = value ? 3f : 0f;
        }
    }

    /// <summary>
    /// A painted mesh of the model that dents in a crash: its surfaces as imported (indexed, in the mesh's own frame,
    /// often scaled) and its frame in the body's, where the dents are; its box there, so a dent elsewhere passes it by.
    /// </summary>
    private sealed class ModelDent(MeshInstance3D node, Transform3D toBody, Aabb bounds)
    {
        public readonly MeshInstance3D Node = node;
        public readonly Mesh Original = node.Mesh;
        public readonly Transform3D ToBody = toBody;
        public readonly Aabb Bounds = bounds;
        public Godot.Collections.Array[]? Arrays;
    }

    private static string ModelKey(CarStyle style) => style switch
    {
        CarStyle.V8 => "v8",
        CarStyle.HotHatch => "hothatch",
        CarStyle.FiveHatch => "five",
        CarStyle.TurboHatch => "turbohatch",
        CarStyle.Saloon => "saloon",
        CarStyle.Estate => "estate",
        CarStyle.Fleet => "fleet",
        _ => "flat12",
    };

    /// <summary>
    /// The prepared models a car can be drawn with, its own first: the three hatchbacks share one (tools/car_models.json
    /// "same_as"), which saves the installer two copies of a million triangles; the wheels are then fitted to the car's
    /// own tyres (HangModelWheels).
    /// </summary>
    private static string[] ModelKeys(CarStyle style) => style switch
    {
        CarStyle.FiveHatch or CarStyle.TurboHatch => [ModelKey(style), "hothatch"],
        _ => [ModelKey(style)],
    };

    /// <summary>
    /// Builds the car from its prepared model, if the build has one: false leaves the lofted body to be built. The
    /// scene is loaded once and cached by Godot, so the player's car, the ghost and the drones share its meshes.
    /// </summary>
    private bool TryBuildModel(VehicleSpec spec)
    {
        if (!UseModels) return false;
        var key = ModelKeys(spec.Style).FirstOrDefault(k => ResourceLoader.Exists($"res://assets/models/{k}.glb"));
        if (key is null) return false;
        var path = $"res://assets/models/{key}.glb";
        if (ResourceLoader.Load<PackedScene>(path)?.Instantiate() is not Node3D scene) return false;
        if (scene.FindChild("CarModel", true, false) is not Node3D model || model.GetNodeOrNull<Node3D>("Wheel0") is null)
        {
            GD.PrintErr($"{path} is not a prepared car model (tools/prepare_car_models.py); the lofted body is drawn");
            scene.Free();
            return false;
        }
        scene.Name = "Model";
        AddChild(scene);
        _model = model;

        // The body node of the lofted car stays, empty, at the road's height: the crash code hangs debris and cracks on it.
        _body = new Node3D { Name = "Body", Position = new Vector3(0f, -_cgHeight, 0f) };
        AddChild(_body);

        HangModelWheels(spec, fitTyres: key != ModelKey(spec.Style));
        _steeringSpin = model.GetNodeOrNull<Node3D>("Steering/Spin");
        if (model.GetNodeOrNull<Node3D>("Eye") is { } eye) _modelEye = FrameOf(eye).Origin;
        _modelMirror = model.GetNodeOrNull<Node3D>("Mirror");
        if (_modelMirror is null && model.GetNodeOrNull<Node3D>("MirrorHung") is { } hung)
        {
            _modelMirror = hung;
            _modelMirrorHung = true;
        }
        if (model.GetNodeOrNull<Node3D>("Dials") is { } dials) MountModelDials(dials);
        DressModel(model);
        _modelBounds = ModelBounds(model.GetNodeOrNull<Node3D>("Body") ?? model);
        SetPaintCabin();
        CollectModelDents(model.GetNodeOrNull<Node3D>("Body") ?? model);
        if (_modelEye is null)
        {
            // No cabin in the model: the lofted cabin (dashboard, binnacle, wheel, seats) inside its shell.
            BuildInterior(_body, new StandardMaterial3D { AlbedoColor = Trim, Roughness = 0.75f });
        }
        return true;
    }

    /// <summary>A node's transform in this node's frame, before it is in the tree (Node3D.GlobalTransform needs the tree).</summary>
    private Transform3D FrameOf(Node3D node)
    {
        var frame = node.Transform;
        for (var parent = node.GetParent(); parent is not null && parent != this; parent = parent.GetParent())
        {
            if (parent is Node3D spatial) frame = spatial.Transform * frame;
        }
        return frame;
    }

    /// <summary>
    /// The model's wheels on pivots of this car's own, so the simulation steers and spins them and the suspension
    /// moves them (SetWheels): each pivot at the model's hub, its meshes centred on it. The prepared hubs are the
    /// simulation's, to the centimetre; a model prepared for other numbers is reported.
    /// </summary>
    private void HangModelWheels(VehicleSpec spec, bool fitTyres)
    {
        for (var i = 0; i < 4; i++)
        {
            var source = _model!.GetNode<Node3D>($"Wheel{i}");
            var hub = FrameOf(source).Origin;
            var expected = new Vector3((i % 2 == 0 ? -1f : 1f) * HalfTrack(spec, i < 2), hub.Y, i < 2 ? -spec.FrontAxle : spec.RearAxle);
            if (!fitTyres && MathF.Abs(hub.Z - expected.Z) > 0.05f)
            {
                GD.PrintErr($"car model {ModelKey(spec.Style)}: wheel {i} at z {hub.Z:0.00}, the simulation's at {expected.Z:0.00} (tools/car_models.py SPECS out of date?)");
            }
            var pivot = new Node3D { Name = $"Wheel{i}", Position = hub };
            AddChild(pivot);
            _outsideOnly.Add(pivot);
            var spinner = new Node3D { Name = "Spin" };
            pivot.AddChild(spinner);
            foreach (var child in source.GetChildren())
            {
                // Out of the imported scene: its owner would not be an ancestor any more.
                Disown(child);
                source.RemoveChild(child);
                spinner.AddChild(child);
            }
            _wheelPivots[i] = pivot;
            _wheelSpinners[i] = spinner;
            if (fitTyres)
            {
                // Another car's model: its wheels were sized to that car's tyres; these are this car's.
                var radius = 0f;
                foreach (var mesh in Descendants(spinner).OfType<MeshInstance3D>())
                {
                    if (mesh.Mesh is null) continue;
                    var box = mesh.Transform * mesh.GetAabb();
                    radius = MathF.Max(radius, MathF.Max(box.Size.Y, box.Size.Z) * 0.5f);
                }
                if (radius > 0.1f) spinner.Scale = Vector3.One * Math.Clamp(spec.WheelRadius / radius, 0.85f, 1.15f);
            }
        }
    }

    private static void Disown(Node node)
    {
        node.Owner = null;
        foreach (var child in node.GetChildren()) Disown(child);
    }

    /// <summary>The live instrument panel on the model's dials, as wide as the prepared node's x scale says.</summary>
    private void MountModelDials(Node3D dials)
    {
        var width = dials.Basis.Scale.X;
        dials.Basis = dials.Basis.Orthonormalized();
        var size = new Vector2(width, width * InstrumentCluster.PanelSize.Y / InstrumentCluster.PanelSize.X);
        _gaugeFace = new MeshInstance3D
        {
            Name = "Dials",
            Mesh = new QuadMesh { Size = size },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.035f, 0.035f, 0.04f), Roughness = 0.85f },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        dials.AddChild(_gaugeFace);
    }

    /// <summary>
    /// The game's own materials on the model: the paint in this car's colour (a coated dielectric, as the lofted
    /// body's), the windows tinted as the lofted glass from outside and nearly clear from the seat, the lamp lenses
    /// clear. Everything else keeps the model's own.
    /// </summary>
    private void DressModel(Node3D model)
    {
        var paint = ModelPaint(_paint);
        _modelPaint = paint;
        _windowOutside ??= new StandardMaterial3D
        {
            AlbedoColor = new Color(0.05f, 0.08f, 0.11f, 0.80f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            Metallic = 0.9f,
            Roughness = 0.05f,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        // From the seat the glass is all but clear: a faint tint and the sky's reflection, so the road is seen through it.
        _windowInside ??= new StandardMaterial3D
        {
            AlbedoColor = new Color(0.30f, 0.33f, 0.36f, 0.06f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            Metallic = 0.3f,
            Roughness = 0.05f,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        _lens ??= new StandardMaterial3D
        {
            AlbedoColor = new Color(0.92f, 0.94f, 0.96f, 0.18f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            Roughness = 0.04f,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        foreach (var mesh in Descendants(model).OfType<MeshInstance3D>())
        {
            if (mesh.Mesh is null) continue;
            for (var s = 0; s < mesh.Mesh.GetSurfaceCount(); s++)
            {
                switch (mesh.Mesh.SurfaceGetMaterial(s)?.ResourceName)
                {
                    case "GLP_Paint":
                        mesh.SetSurfaceOverrideMaterial(s, paint);
                        break;
                    case "GLP_Glass":
                        mesh.SetSurfaceOverrideMaterial(s, _windowOutside);
                        _windows.Add((mesh, s));
                        break;
                    case "GLP_Lens":
                        mesh.SetSurfaceOverrideMaterial(s, _lens);
                        break;
                    case "GLP_Brake":
                        // The model's own lens, glowing red when the car brakes; a copy per car, so one car braking
                        // does not light another's.
                        if (mesh.Mesh.SurfaceGetMaterial(s) is BaseMaterial3D lens && lens.Duplicate() is BaseMaterial3D lamp)
                        {
                            lamp.EmissionEnabled = true;
                            lamp.Emission = new Color(1f, 0.06f, 0.03f);
                            lamp.EmissionEnergyMultiplier = 0f;
                            mesh.SetSurfaceOverrideMaterial(s, lamp);
                            _brakeLamps.Add(lamp);
                        }
                        break;
                }
            }
        }
    }

    /// <summary>
    /// Car paint for a model: the lofted body's coated dielectric (metallic 0.05, roughness 0.38 under a near-mirror
    /// clearcoat). Models draw their panels from both faces and few have a headlining, so from the seat the underside
    /// of the roof showed in the body colour: the back face of a panel is a dark lining, but only inside the cabin
    /// (a box in the car's frame, kept up to date by UpdatePaintFrame). Outside it a back face is paint too, because
    /// some models have whole panels wound the wrong way (a Renault 12's boot lid and door showed black).
    /// </summary>
    private static ShaderMaterial ModelPaint(Color colour)
    {
        _paintShader ??= new Shader
        {
            Code = """
                shader_type spatial;
                render_mode cull_disabled;
                uniform vec3 paint : source_color = vec3(0.76, 0.03, 0.05);
                uniform vec3 lining : source_color = vec3(0.045, 0.045, 0.05);
                uniform mat4 to_car = mat4(1.0);
                uniform vec3 cabin_min = vec3(1.0);
                uniform vec3 cabin_max = vec3(-1.0);
                varying vec3 world_position;
                void vertex() {
                    world_position = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
                }
                void fragment() {
                    vec3 local = (to_car * vec4(world_position, 1.0)).xyz;
                    bool in_cabin = all(greaterThan(local, cabin_min)) && all(lessThan(local, cabin_max));
                    if (FRONT_FACING || !in_cabin) {
                        ALBEDO = paint;
                        METALLIC = 0.05;
                        ROUGHNESS = 0.38;
                        CLEARCOAT = 1.0;
                        CLEARCOAT_ROUGHNESS = 0.04;
                    } else {
                        ALBEDO = lining;
                        ROUGHNESS = 0.9;
                    }
                }
                """,
        };
        var material = new ShaderMaterial { Shader = _paintShader };
        material.SetShaderParameter("paint", colour);
        return material;
    }

    private ShaderMaterial? _modelPaint;

    /// <summary>
    /// The cabin the paint lines from inside: from the eyes, a box over the seats and under the roof, from the
    /// dashboard to behind the seats' backs, as wide as the inside of the doors. A model without a cabin of its own
    /// keeps the empty box: its paint is paint on both faces.
    /// </summary>
    private void SetPaintCabin()
    {
        if (_modelPaint is null || _modelEye is not { } eye) return;
        _modelPaint.SetShaderParameter("cabin_min", new Vector3(-0.86f, eye.Y - 0.55f, eye.Z - 1.1f));
        _modelPaint.SetShaderParameter("cabin_max", new Vector3(0.86f, eye.Y + 0.6f, eye.Z + 0.75f));
    }

    /// <summary>World to this car's frame, for the paint's cabin test: every frame the car moves.</summary>
    public override void _Process(double delta)
    {
        if (_modelPaint is not null && IsInsideTree()) _modelPaint.SetShaderParameter("to_car", new Projection(GlobalTransform.AffineInverse()));
    }

    /// <summary>The model's box in this node's frame, for the contact shadow (MarkPlayer).</summary>
    private Aabb ModelBounds(Node3D root)
    {
        var bounds = new Aabb();
        var first = true;
        foreach (var mesh in Descendants(root).OfType<MeshInstance3D>())
        {
            if (mesh.Mesh is null) continue;
            var box = FrameOf(mesh) * mesh.GetAabb();
            bounds = first ? box : bounds.Merge(box);
            first = false;
        }
        return bounds;
    }

    /// <summary>The model's painted meshes, which dent in a crash as the lofted shell does (CarView.Damage.cs).</summary>
    private void CollectModelDents(Node3D root)
    {
        var lift = new Transform3D(Basis.Identity, new Vector3(0f, _cgHeight, 0f));
        foreach (var mesh in Descendants(root).OfType<MeshInstance3D>())
        {
            if (mesh.Mesh is not { } surfaces) continue;
            var painted = false;
            for (var s = 0; s < surfaces.GetSurfaceCount() && !painted; s++) painted = surfaces.SurfaceGetMaterial(s)?.ResourceName == "GLP_Paint";
            if (!painted) continue;
            var toBody = lift * FrameOf(mesh);
            _modelDents.Add(new ModelDent(mesh, toBody, toBody * surfaces.GetAabb()));
        }
    }

    /// <summary>
    /// The dents on the model's paint: every vertex of a mesh a dent reaches moves by the dent's push, carried into the
    /// mesh's own frame, and its normal leans with the crease. The meshes are indexed, so a face keeps its smooth
    /// shading rather than the facets of the lofted shell; the imported level-of-detail meshes are left behind.
    /// </summary>
    private void ApplyModelDents()
    {
        foreach (var d in _modelDents)
        {
            var reached = false;
            foreach (var dent in _dents)
            {
                if (d.Bounds.Grow(dent.Radius).HasPoint(dent.Centre)) reached = true;
            }
            if (!reached)
            {
                d.Node.Mesh = d.Original;
                continue;
            }
            var count = d.Original.GetSurfaceCount();
            if (d.Arrays is null)
            {
                d.Arrays = new Godot.Collections.Array[count];
                for (var s = 0; s < count; s++) d.Arrays[s] = d.Original.SurfaceGetArrays(s);
            }
            var back = d.ToBody.Basis.Inverse();
            var mesh = new ArrayMesh();
            var any = false;
            for (var s = 0; s < count; s++)
            {
                var arrays = new Godot.Collections.Array(d.Arrays[s]);
                var source = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                var vertices = (Vector3[])source.Clone();
                var normalsVariant = arrays[(int)Mesh.ArrayType.Normal];
                var normals = normalsVariant.VariantType == Variant.Type.PackedVector3Array ? (Vector3[])normalsVariant.AsVector3Array().Clone() : null;
                for (var i = 0; i < vertices.Length; i++)
                {
                    var offset = DentOffset(d.ToBody * source[i]);
                    if (offset.LengthSquared() < 1e-10f) continue;
                    var local = back * offset;
                    vertices[i] = source[i] + local;
                    if (normals is not null) normals[i] = (normals[i] - local.Normalized() * 0.35f).Normalized();
                    any = true;
                }
                arrays[(int)Mesh.ArrayType.Vertex] = vertices;
                if (normals is not null) arrays[(int)Mesh.ArrayType.Normal] = normals;
                mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
                mesh.SurfaceSetMaterial(s, d.Original.SurfaceGetMaterial(s));
            }
            d.Node.Mesh = any ? mesh : d.Original;
        }
    }

    /// <summary>From the seat the windows are all but clear; from outside, tinted.</summary>
    private void SetModelWindows(bool insideCar)
    {
        var material = insideCar ? _windowInside : _windowOutside;
        foreach (var (mesh, surface) in _windows) mesh.SetSurfaceOverrideMaterial(surface, material);
    }
}
