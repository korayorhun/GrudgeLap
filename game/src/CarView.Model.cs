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
/// cabin that tools/prepare_car_models.py gave a dark trim). A paint drawn from a texture (the 205's and the VAZ's
/// atlases, the Delta's white) carries a paint mask and is recoloured where the mask says paint (TexturePaint).
/// </remarks>
internal sealed partial class CarView
{
    /// <summary>The lofted bodies instead of the models (<c>--lofted</c>), for comparisons.</summary>
    public static bool UseModels { get; set; } = true;

    /// <summary>
    /// The prepared model a car is drawn with, as a resource path, or null when the build has none (the lofted body is
    /// drawn then). The showroom loads its neighbours' models on a thread ahead of a step (Showroom.Preload).
    /// </summary>
    public static string? ModelPath(VehicleSpec spec)
    {
        if (!UseModels) return null;
        var key = ModelKeys(spec.Style).FirstOrDefault(k => ResourceLoader.Exists($"res://assets/models/{k}.glb"));
        return key is null ? null : $"res://assets/models/{key}.glb";
    }

    /// <summary>The car's own colour whatever it is painted: its model's paint that covers the most of it, or the lofted body's (<see cref="LoftPaint"/>).</summary>
    public Color OwnColour => _paints.Count > 0 ? _paints[0].Factory : _loftPaint.Colour;

    /// <summary>
    /// The body's box in this node's frame (origin at the centre of gravity): the model's, or the lofted body's from its
    /// style's widest half width, nose and tail stations and roof height. The showroom frames its camera by it.
    /// </summary>
    public Aabb Bounds => _model is not null
        ? _modelBounds
        : new Aabb(new Vector3(-_style.HalfWidth.Max(), -_cgHeight, _style.Keys[0]), new Vector3(2f * _style.HalfWidth.Max(), _style.CentreH.Max(), _style.Keys[^1] - _style.Keys[0]));

    private Node3D? _model;
    private Vector3? _modelEye;
    private float? _modelGearTop;
    private Node3D? _modelMirror;
    private bool _modelMirrorHung;    // the game's own housing hangs there (the model's is out of view or missing)
    private Aabb _modelBounds;
    private readonly List<(MeshInstance3D Mesh, int Surface)> _windows = [];
    private static StandardMaterial3D? _windowOutside;
    private static StandardMaterial3D? _windowInside;
    private static StandardMaterial3D? _lens;
    private static Shader? _paintShader;
    private static Shader? _texturePaintShader;
    private static Shader? _texturePaintCulledShader;
    private readonly List<PaintSurface> _paints = [];
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
            // At night the tail lamps' glow flares with the brake (0.59).
            foreach (var glow in _tailGlows) glow.MaterialOverride = NightLamps.GlowMaterial(TailRed, value ? BrakeGlow : TailGlow);
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

    /// <summary>
    /// One paint material of the model, as the game draws it: its factory colour (the model's own: the flat paint's
    /// colour, or the key colour a paint texture was masked by, a near-white one lifted to white), whether it is drawn
    /// from a texture, and whether its factory paint is that texture as it is (TexturePaint).
    /// </summary>
    private readonly record struct PaintSurface(ShaderMaterial Material, Color Factory, bool Textured, bool FactoryTexel);

    /// <summary>The menu's white (Paints), what a near-white paint texture's own colour is lifted to (PaintFor).</summary>
    private const float WhiteValue = 0.90f;

    private static string ModelKey(CarStyle style) => style switch
    {
        CarStyle.V8 => "v8",
        CarStyle.HotHatch => "hothatch",
        CarStyle.FiveHatch => "five",
        CarStyle.TurboHatch => "turbohatch",
        CarStyle.Saloon => "saloon",
        CarStyle.Estate => "estate",
        CarStyle.Fleet => "fleet",
        // cars13: the keys the parallel model track delivers; until a file is there the lofted body is drawn.
        CarStyle.FlatPlaneV8 => "458",
        CarStyle.HgtFive => "bravohgt",
        CarStyle.HatchSixteen => "bravo16",
        CarStyle.CupraTsi => "leoncupra",
        CarStyle.Boxer => "brz",
        CarStyle.ClioSixteen => "clio",
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
        if (ModelPath(spec) is not { } path) return false;
        var key = System.IO.Path.GetFileNameWithoutExtension(path);
        if (LoadModelScene(path)?.Instantiate() is not Node3D scene) return false;
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
        BuildModelLiners(model);
        BuildModelPlate(model);
        DressModel(model);
        // Parts of a borrowed cabin that poke through the shell (car_models.json "inside_only") are drawn from the seat only.
        foreach (var inside in Descendants(model).OfType<Node3D>().Where(n => n.Name.ToString().StartsWith("Inside_", StringComparison.Ordinal)))
        {
            inside.Visible = false;
            _cockpitOnly.Add(inside);
        }
        _modelBounds = ModelBounds(model.GetNodeOrNull<Node3D>("Body") ?? model);
        SetPaintCabin();
        CollectModelDents(model.GetNodeOrNull<Node3D>("Body") ?? model);
        if (_modelEye is null)
        {
            // No cabin in the model: the lofted cabin (dashboard, binnacle, wheel, seats) inside its shell.
            BuildInterior(_body, new StandardMaterial3D { AlbedoColor = Trim, Roughness = 0.75f });
        }
        else
        {
            // The model's own cabin: the same daylight the lofted cabin gets, placed from the model's eye (in the body's
            // frame, which sits at the road's height). Without it the cabin was lit by the sky's ambient alone.
            var modelEye = _modelEye.Value + new Vector3(0f, _cgHeight, 0f);
            BuildCabinLight(_body, modelEye, modelEye.X);
        }
        return true;
    }

    /// <summary>
    /// Asks Godot's loader thread for the models of the cars a credit draws (the player's and the traffic's), so they
    /// are parsed while the world is built on the main thread (U3, 0.29.0: loaded one after another when the drones
    /// were built, the five everyday models took 2.2 s of the start). A path already loading or loaded is left alone.
    /// </summary>
    public static void RequestModels(IEnumerable<VehicleSpec> specs)
    {
        foreach (var path in specs.Select(ModelPath).OfType<string>().Distinct())
        {
            if (ResourceLoader.HasCached(path)) continue;
            if (ResourceLoader.LoadThreadedGetStatus(path) == ResourceLoader.ThreadLoadStatus.InvalidResource) ResourceLoader.LoadThreadedRequest(path);
        }
    }

    /// <summary>The model's scene: from the loader thread when it has it or is still on it (RequestModels), loaded here otherwise.</summary>
    private static PackedScene? LoadModelScene(string path)
    {
        var status = ResourceLoader.LoadThreadedGetStatus(path);
        if (status is ResourceLoader.ThreadLoadStatus.InProgress or ResourceLoader.ThreadLoadStatus.Loaded) return ResourceLoader.LoadThreadedGet(path) as PackedScene;
        return ResourceLoader.Load<PackedScene>(path);
    }

    private static StandardMaterial3D? _linerMaterial;
    private static StandardMaterial3D? _plateMaterial;

    /// <summary>
    /// Wheel-arch liners where the prepared scene asks for them (Liner0..3, car_models.json "liners": the 86's arches
    /// are open behind the tyres, so the road showed through the car at the wheels' height): a dark half-tube over each
    /// hub from 20 to 160 degrees, open below and clear of the bumpers, with a wall on the inboard side. The node's scale
    /// carries the liner's outboard reach, radius and inboard reach (the preparation sizes it to the arch's lip).
    /// </summary>
    private static void BuildModelLiners(Node3D model)
    {
        for (var i = 0; i < 4; i++)
        {
            if (model.GetNodeOrNull<Node3D>($"Liner{i}") is not { } liner) continue;
            var outboard = liner.Scale.X;
            var radius = liner.Scale.Y;
            var inboard = liner.Scale.Z;
            liner.Scale = Vector3.One;
            _linerMaterial ??= new StandardMaterial3D { AlbedoColor = new Color(0.05f, 0.05f, 0.05f), Roughness = 0.95f, CullMode = BaseMaterial3D.CullModeEnum.Disabled };
            liner.AddChild(new MeshInstance3D { Name = "Liner", Mesh = ArchLiner(outboard, inboard, radius, i % 2 == 0 ? 1f : -1f), MaterialOverride = _linerMaterial });
        }
    }

    /// <summary>A part-tube about the x axis from 20 degrees behind the hub over the top to 20 degrees ahead, and a sector closing its inboard end.</summary>
    private static ArrayMesh ArchLiner(float outboard, float inboard, float radius, float inboardSign)
    {
        const int segments = 24;
        var x0 = -inboardSign * outboard;
        var x1 = inboardSign * inboard;
        var st = new MeshBuilder();
        for (var s = 0; s <= segments; s++)
        {
            var angle = Mathf.DegToRad(20f + 140f * s / segments);
            var y = MathF.Sin(angle);
            var z = MathF.Cos(angle);
            st.SetNormal(new Vector3(0f, -y, -z));
            st.AddVertex(new Vector3(x0, radius * y, radius * z));
            st.AddVertex(new Vector3(x1, radius * y, radius * z));
        }
        for (var s = 0; s < segments; s++)
        {
            var a = 2 * s;
            st.AddIndex(a); st.AddIndex(a + 1); st.AddIndex(a + 2);
            st.AddIndex(a + 1); st.AddIndex(a + 3); st.AddIndex(a + 2);
        }
        var centre = st.VertexCount;
        st.SetNormal(new Vector3(-inboardSign, 0f, 0f));
        st.AddVertex(new Vector3(x1, 0f, 0f));
        for (var s = 0; s <= segments; s++)
        {
            var angle = Mathf.DegToRad(20f + 140f * s / segments);
            st.AddVertex(new Vector3(x1, radius * MathF.Sin(angle), radius * MathF.Cos(angle)));
        }
        for (var s = 0; s < segments; s++)
        {
            st.AddIndex(centre); st.AddIndex(centre + 1 + s); st.AddIndex(centre + 2 + s);
        }
        return st.Commit();
    }

    /// <summary>The rear number plate where the prepared scene puts one (Plate, car_models.json "plate": a model without its own); the node's scale is its size.</summary>
    private static void BuildModelPlate(Node3D model)
    {
        if (model.GetNodeOrNull<Node3D>("Plate") is not { } plate) return;
        var size = plate.Scale;
        plate.Scale = Vector3.One;
        _plateMaterial ??= new StandardMaterial3D { AlbedoColor = new Color(0.92f, 0.92f, 0.9f), Roughness = 0.5f };
        plate.AddChild(new MeshInstance3D { Name = "Plate", Mesh = new BoxMesh { Size = new Vector3(size.X, size.Y, 0.008f) }, MaterialOverride = _plateMaterial });
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

    /// <summary>The live instrument panel on the model's dials, as wide as the prepared node's x scale says; a child
    /// Gear (prepare_car_models.py, the entry's dials gear_top) says where on it the gear readout goes, in the panel's
    /// widths up from its centre.</summary>
    private void MountModelDials(Node3D dials)
    {
        var width = dials.Basis.Scale.X;
        if (dials.GetNodeOrNull<Node3D>("Gear") is { } gear)
            _modelGearTop = InstrumentCluster.PanelSize.Y * 0.5f - gear.Position.Y * InstrumentCluster.PanelSize.X;
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
    /// body's; the model's own colour when none is chosen), the windows tinted as the lofted glass from outside and
    /// nearly clear from the seat, the lamp lenses clear. Everything else keeps the model's own.
    /// </summary>
    private void DressModel(Node3D model)
    {
        // One paint material per paint material of the model, shared by its surfaces: the cabin's frame is updated
        // on each of them every frame (_Process), and a model has one to three.
        var paints = new Dictionary<Material, ShaderMaterial>();
        var painted = new List<(Material Source, MeshInstance3D Mesh, int Surface)>();
        _windowOutside ??= new StandardMaterial3D
        {
            AlbedoColor = new Color(0.05f, 0.08f, 0.11f, 0.80f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            Metallic = 0.9f,
            Roughness = 0.05f,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            SpecularMode = WindowSpecular,
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
            SpecularMode = WindowSpecular,
        };
        foreach (var mesh in Descendants(model).OfType<MeshInstance3D>())
        {
            if (mesh.Mesh is null) continue;
            for (var s = 0; s < mesh.Mesh.GetSurfaceCount(); s++)
            {
                switch (mesh.Mesh.SurfaceGetMaterial(s)?.ResourceName)
                {
                    case "GLP_Paint" when mesh.Mesh.SurfaceGetMaterial(s) is { } source:
                        if (!paints.TryGetValue(source, out var paint))
                        {
                            paint = PaintFor(source);
                            paints[source] = paint;
                        }
                        mesh.SetSurfaceOverrideMaterial(s, paint);
                        painted.Add((source, mesh, s));
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
                    case var _ when mesh.Mesh.SurfaceGetMaterial(s) is BaseMaterial3D own:
                        CorrectModelMaterial(own);
                        break;
                }
            }
        }
        // The body's colour first, the paint that covers the most of the car (the McLaren has two, its orange and a
        // maroon): the model's own colour, for what takes the car's colour from Paint (the debris of a crash) and for
        // the showroom's ORIGINAL swatch. Measured by area since the showroom (0.25): counted by the surfaces' indices,
        // the maroon's finely cut parts outnumbered the orange panels, and the orange car's swatch was maroon.
        if (paints.Count > 1)
        {
            var area = paints.ToDictionary(p => p.Value, p => PaintArea(p.Key, painted, model));
            _paints.Sort((a, b) => area.GetValueOrDefault(b.Material).CompareTo(area.GetValueOrDefault(a.Material)));
        }
        if (_paintChoice is null && _paints.Count > 0) _paint = _paints[0].Factory;
    }

    /// <summary>The area a paint material of a model covers, m², worked out once a run (the imported scene and its materials are cached).</summary>
    private static readonly Dictionary<ulong, double> _paintAreas = new();

    private static double PaintArea(Material source, List<(Material Source, MeshInstance3D Mesh, int Surface)> painted, Node3D model)
    {
        if (_paintAreas.TryGetValue(source.GetInstanceId(), out var known)) return known;
        var area = 0.0;
        foreach (var (material, mesh, surface) in painted)
        {
            if (material != source || mesh.Mesh is null) continue;
            // The surface in the model's frame, so a mesh scaled under the model counts at its drawn size.
            var toModel = Transform3D.Identity;
            for (Node? node = mesh; node is not null && node != model; node = node.GetParent())
            {
                if (node is Node3D placed) toModel = placed.Transform * toModel;
            }
            var arrays = mesh.Mesh.SurfaceGetArrays(surface);
            var vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            var indices = arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
            var count = indices.Length > 0 ? indices.Length : vertices.Length;
            for (var i = 0; i + 2 < count; i += 3)
            {
                var a = toModel * vertices[indices.Length > 0 ? indices[i] : i];
                var b = toModel * vertices[indices.Length > 0 ? indices[i + 1] : i + 1];
                var c = toModel * vertices[indices.Length > 0 ? indices[i + 2] : i + 2];
                area += 0.5 * (b - a).Cross(c - a).Length();
            }
        }
        _paintAreas[source.GetInstanceId()] = area;
        return area;
    }

    /// <summary>
    /// The mark of a model material already corrected: the imported scene is cached, so its materials are shared by every
    /// car drawn with it. Kept on the material itself; a static set of them (until the showroom's review) held every
    /// model the game had drawn, textures and all, for the session, the showroom's eight among them (68 MB of video
    /// memory after a visit to each car).
    /// </summary>
    private static readonly StringName CorrectedMark = "glp_corrected";

    /// <summary>
    /// Physically plausible values for a model's own materials where the exporter left none (0.19). Many of the CC BY
    /// models carry the glTF defaults (metallic 1, roughness 1, no texture to drive them): a matt plastic or a dial
    /// then shades as rough grey metal, a mirror's glass as a white blot. Tyres were exported glossy (roughness 0.15 on
    /// the Testarossa, a third metallic on the Renault) and read as plastic toys' wheels. The material is corrected in
    /// place, once, as every instance of the cached scene shares it.
    /// </summary>
    private static void CorrectModelMaterial(BaseMaterial3D material)
    {
        if (material.HasMeta(CorrectedMark)) return;
        material.SetMeta(CorrectedMark, true);
        var name = material.ResourceName.ToLowerInvariant();
        static bool Says(string name, params string[] words) => words.Any(name.Contains);
        var drivenByTexture = material.RoughnessTexture is not null || material.MetallicTexture is not null;
        var exporterDefault = !drivenByTexture && material.Metallic >= 0.999f && material.Roughness >= 0.999f;
        if (material.EmissionEnabled && Says(name, "back", "rear", "tail") && material.Emission.S < 0.2f)
        {
            // A rear lamp unit exported glowing white (the McLaren's "backights", a white block on its tail that the
            // rain's dark light made glare): rear lamps are red lenses, lit as tail lamps.
            material.AlbedoColor = new Color(0.45f, 0.02f, 0.02f);
            material.Emission = new Color(1f, 0.05f, 0.03f);
            material.EmissionEnergyMultiplier = MathF.Min(material.EmissionEnergyMultiplier, 0.6f);
            material.Metallic = 0f;
            material.Roughness = 0.1f;
        }
        else if (Says(name, "tyre", "tire", "pneu", "rubber"))
        {
            // Rubber: a dielectric with a broad, dull sheen (roughness 0.85-0.9); its albedo, about 0.03, the model's.
            material.Metallic = 0f;
            if (!drivenByTexture) material.Roughness = MathF.Max(material.Roughness, 0.86f);
        }
        else if (Says(name, "mirror") && exporterDefault && material.AlbedoTexture is null && material.AlbedoColor.Luminance > 0.6f)
        {
            // A wing mirror's glass left at the defaults: silvered glass, so it mirrors the road through the probe.
            material.Roughness = 0.03f;
            material.AlbedoColor = new Color(0.86f, 0.87f, 0.88f);
        }
        else if (exporterDefault && !Says(name, "chrome", "croom", "metal", "metel", "steel", "alu", "alminum", "zinc", "brushed", "exhaust", "muffler"))
        {
            // No metal by name and no values of its own: plastic, carbon, trim or a printed dial, a matt dielectric.
            material.Metallic = 0f;
            material.Roughness = 0.6f;
        }
        else if (Says(name, "wheel", "weel", "rim") && !drivenByTexture && material.AlbedoTexture is null && material.Metallic < 0.5f
                 && material.AlbedoColor.S < 0.15f && material.AlbedoColor.V > 0.3f)
        {
            // A grey dielectric wheel: a silver painted alloy, metallic flakes under lacquer.
            material.Metallic = 0.75f;
            material.Roughness = 0.32f;
        }
    }

    /// <summary>
    /// Car paint for a model: the lofted body's coated dielectric (metallic 0.05, roughness 0.38 under a near-mirror
    /// clearcoat; a metallic paint's own, CarPaint). Models draw their panels from both faces and few have a
    /// headlining, so from the seat the underside of the roof showed in the body colour: the back face of a panel is
    /// a dark lining, but only inside the cabin
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
                uniform float paint_metallic = 0.05;
                uniform float paint_roughness = 0.38;
                uniform float paint_pearl = 0.0;
                uniform vec3 pearl_tint : source_color = vec3(1.0);
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
                        // A pearl's flop (CarPaint.Pearl): the colour turns to its tint as the panel is seen edge-on,
                        // the two-tone blend on the view angle that real-time car paints use for a mica's colour shift;
                        // cubed, so a panel seen at 60 degrees keeps nearly all its own colour and only the turning
                        // edges take the tint.
                        float edge = 1.0 - abs(dot(NORMAL, VIEW));
                        ALBEDO = mix(paint, pearl_tint, paint_pearl * 0.55 * edge * edge * edge);
                        METALLIC = paint_metallic;
                        ROUGHNESS = paint_roughness;
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

    /// <summary>
    /// The paint for one of the model's paint materials: the texture paint when it has a texture and the mask the
    /// preparation made for it (emission slot, with its key colour in the extras), the flat paint otherwise, in the
    /// chosen paint or the model's own.
    /// </summary>
    private ShaderMaterial PaintFor(Material source)
    {
        PaintSurface surface;
        if (source is BaseMaterial3D { AlbedoTexture: { } albedo, EmissionTexture: { } mask } own && PaintKey(own) is { } key)
        {
            // The factory paint is the texture as it is, but a near-white one is lifted to the menu's white: the
            // Delta's Martini white is a texture of sRGB 185, a dirty mid grey under the sun (albedo 0.49 where a
            // white car's is near 0.8). Lifted, it is drawn as a paint (white times the texel's lightness), so its
            // lettering and grime stay as they are.
            var lifted = key.S < 0.12f && key.V > 0.6f;
            var factory = lifted ? new Color(key.R * WhiteValue / key.V, key.G * WhiteValue / key.V, key.B * WhiteValue / key.V) : key;
            surface = new PaintSurface(TexturePaint(own, albedo, mask), factory, Textured: true, FactoryTexel: !lifted);
        }
        else
        {
            // A flat paint's own colour; one with a texture but no mask (a model prepared before the masks) as before.
            var factory = source is BaseMaterial3D flat ? flat.AlbedoColor : DefaultPaint;
            surface = new PaintSurface(ModelPaint(factory), factory, Textured: false, FactoryTexel: false);
        }
        _paints.Add(surface);
        ApplyPaint(surface);
        return surface.Material;
    }

    /// <summary>
    /// Car paint drawn from a texture (tools/prepare_car_models.py paint_mask): two of the models carry their paint in
    /// a texture atlas with the lamps, the badges, the pinstripe, the trim and the chrome (the 205's, the VAZ's), and
    /// the Delta's paint is a white texture with its Martini lettering and its grime. Where the mask's red says paint
    /// the colour is the chosen paint times the texel's lightness against the key colour (the mask's green, halved),
    /// so the texture's shading, panel lines, grime and lettering stay in any colour, with the paint's finish
    /// (CarPaint: metallic and roughness) under the flat paint's clearcoat; everything else is the model's own
    /// material (its texture, roughness, metallic and normal maps) untouched. The factory paint is the texture itself
    /// (a near-white one lifted to white, PaintFor) with the model's own roughness and clearcoat. The
    /// back face of a panel in the cabin is the lining, as ModelPaint's (the three models draw their paint from both
    /// faces); a material drawn from its front only keeps that (the culled shader). Opaque: a paint texture has no
    /// holes (the VAZ's exporter marked every material see-through, which the preparation undoes).
    /// </summary>
    private static ShaderMaterial TexturePaint(BaseMaterial3D own, Texture2D albedo, Texture2D mask)
    {
        static Shader Make(string cull) => new()
        {
            Code = $$"""
                shader_type spatial;
                render_mode {{cull}};
                uniform vec3 paint : source_color = vec3(0.76, 0.03, 0.05);
                uniform float paint_metallic = 0.05;
                uniform float paint_roughness = 0.38;
                uniform float paint_pearl = 0.0;
                uniform vec3 pearl_tint : source_color = vec3(1.0);
                uniform bool factory_texel = false;
                uniform bool own_finish = false;
                uniform float own_clearcoat = 0.0;
                uniform float own_clearcoat_roughness = 0.04;
                uniform sampler2D albedo_texture : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
                uniform vec4 albedo_tint : source_color = vec4(1.0);
                uniform sampler2D paint_mask : hint_default_black, filter_linear_mipmap_anisotropic, repeat_enable;
                uniform sampler2D roughness_texture : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
                uniform vec4 roughness_channel = vec4(0.0, 1.0, 0.0, 0.0);
                uniform float roughness = 1.0;
                uniform sampler2D metallic_texture : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
                uniform vec4 metallic_channel = vec4(0.0, 0.0, 1.0, 0.0);
                uniform float metallic = 0.0;
                uniform sampler2D normal_texture : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
                uniform bool has_normal = false;
                uniform float normal_scale = 1.0;
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
                        vec3 texel = texture(albedo_texture, UV).rgb * albedo_tint.rgb;
                        vec2 mask = texture(paint_mask, UV).rg;
                        float coat = mask.r;
                        float edge = 1.0 - abs(dot(NORMAL, VIEW));
                        vec3 colour = mix(paint, pearl_tint, paint_pearl * 0.55 * edge * edge * edge);
                        vec3 painted = factory_texel ? texel : min(colour * (mask.g * 2.0), vec3(1.0));
                        ALBEDO = mix(texel, painted, coat);
                        float own_roughness = dot(texture(roughness_texture, UV), roughness_channel) * roughness;
                        float own_metallic = dot(texture(metallic_texture, UV), metallic_channel) * metallic;
                        // The model's own finish keeps its roughness (and its own coat), not its metallic: a
                        // paint is a coated dielectric, and the Delta's metallic is glTF's default of 1, which drew
                        // its white as polished aluminium.
                        ROUGHNESS = mix(own_roughness, own_finish ? own_roughness : paint_roughness, coat);
                        METALLIC = mix(own_metallic, paint_metallic, coat);
                        CLEARCOAT = own_finish ? own_clearcoat * coat : coat;
                        CLEARCOAT_ROUGHNESS = own_finish ? own_clearcoat_roughness : 0.04;
                        if (has_normal) {
                            NORMAL_MAP = texture(normal_texture, UV).rgb;
                            NORMAL_MAP_DEPTH = normal_scale;
                        }
                    } else {
                        ALBEDO = lining;
                        ROUGHNESS = 0.9;
                    }
                }
                """,
        };
        var culled = own.CullMode == BaseMaterial3D.CullModeEnum.Back;
        var shader = culled ? _texturePaintCulledShader ??= Make("cull_back") : _texturePaintShader ??= Make("cull_disabled");
        var material = new ShaderMaterial { Shader = shader };
        material.SetShaderParameter("albedo_texture", albedo);
        material.SetShaderParameter("albedo_tint", own.AlbedoColor);
        material.SetShaderParameter("paint_mask", mask);
        material.SetShaderParameter("roughness", own.Roughness);
        material.SetShaderParameter("metallic", own.Metallic);
        if (own.ClearcoatEnabled)
        {
            material.SetShaderParameter("own_clearcoat", own.Clearcoat);
            material.SetShaderParameter("own_clearcoat_roughness", own.ClearcoatRoughness);
        }
        if (own.RoughnessTexture is { } roughnessTexture)
        {
            material.SetShaderParameter("roughness_texture", roughnessTexture);
            material.SetShaderParameter("roughness_channel", Channel(own.RoughnessTextureChannel));
        }
        if (own.MetallicTexture is { } metallicTexture)
        {
            material.SetShaderParameter("metallic_texture", metallicTexture);
            material.SetShaderParameter("metallic_channel", Channel(own.MetallicTextureChannel));
        }
        if (own.NormalEnabled && own.NormalTexture is { } normal)
        {
            material.SetShaderParameter("normal_texture", normal);
            material.SetShaderParameter("has_normal", true);
            material.SetShaderParameter("normal_scale", own.NormalScale);
        }
        return material;
    }

    /// <summary>
    /// The key colour a paint texture was masked by (sRGB), from the material's extras (glp_paint_key, linear), which
    /// tells the preparation's mask from an emission of the model's own; null without one.
    /// </summary>
    private static Color? PaintKey(BaseMaterial3D material)
    {
        if (!material.HasMeta("extras") || material.GetMeta("extras").AsGodotDictionary() is not { } extras) return null;
        if (!extras.TryGetValue("glp_paint_key", out var key) || key.AsFloat32Array() is not { Length: 3 } k) return null;
        return new Color(k[0], k[1], k[2]).LinearToSrgb();
    }

    /// <summary>A texture channel as the vector that picks it out of a texel (BaseMaterial3D's own convention).</summary>
    private static Vector4 Channel(BaseMaterial3D.TextureChannel channel) => channel switch
    {
        BaseMaterial3D.TextureChannel.Red => new Vector4(1f, 0f, 0f, 0f),
        BaseMaterial3D.TextureChannel.Green => new Vector4(0f, 1f, 0f, 0f),
        BaseMaterial3D.TextureChannel.Blue => new Vector4(0f, 0f, 1f, 0f),
        BaseMaterial3D.TextureChannel.Alpha => new Vector4(0f, 0f, 0f, 1f),
        _ => new Vector4(1f / 3f, 1f / 3f, 1f / 3f, 0f),
    };

    /// <summary>The chosen paint, or the model's own, on one of its paints.</summary>
    private void ApplyPaint(PaintSurface surface)
    {
        var paint = _paintChoice ?? new CarPaint(surface.Factory);
        var material = surface.Material;
        material.SetShaderParameter("paint", paint.Colour);
        material.SetShaderParameter("paint_metallic", paint.Metallic);
        material.SetShaderParameter("paint_roughness", paint.Roughness);
        material.SetShaderParameter("paint_pearl", paint.Pearl);
        material.SetShaderParameter("pearl_tint", paint.Pearl > 0f ? PearlTint(paint.Colour) : paint.Colour);
        if (!surface.Textured) return;
        // A paint texture's own colour is the texture itself (not its key's one hue), and its own finish the model's
        // roughness and coat: its roughness map holds the 205's bonnet dust and the VAZ's matt beige, which a new
        // paint's coat glossed over.
        material.SetShaderParameter("factory_texel", _paintChoice is null && surface.FactoryTexel);
        material.SetShaderParameter("own_finish", _paintChoice is null);
    }

    /// <summary>
    /// Repaints the car in place (the menu's PAINT): a paint, or null for the model's own. A lofted body has its
    /// colour built into its meshes and is not repainted here (false): the caller builds it again.
    /// </summary>
    public bool Repaint(CarPaint? paint)
    {
        if (_paints.Count == 0) return false;
        _paintChoice = paint;
        _paint = paint?.Colour ?? _paints[0].Factory;
        foreach (var surface in _paints) ApplyPaint(surface);
        return true;
    }

    /// <summary>
    /// The cabin the paint lines from inside: from the eyes, a box over the seats and under the roof, from the
    /// dashboard to behind the seats' backs, as wide as the inside of the doors. A model without a cabin of its own
    /// keeps the empty box: its paint is paint on both faces.
    /// </summary>
    private void SetPaintCabin()
    {
        if (_modelEye is not { } eye) return;
        foreach (var surface in _paints)
        {
            surface.Material.SetShaderParameter("cabin_min", new Vector3(-0.86f, eye.Y - 0.55f, eye.Z - 1.1f));
            surface.Material.SetShaderParameter("cabin_max", new Vector3(0.86f, eye.Y + 0.6f, eye.Z + 0.75f));
        }
    }

    /// <summary>
    /// The shade of a closed cabin (0.19), on the player's car from the seat: a reflection probe the size of the cabin
    /// box whose ambient light is a dim constant instead of the open sky's. The roof, the pillars and the doors hide
    /// most of the sky from a real dashboard, so a cabin is far darker than the road outside and the eye, adapted to
    /// the road, sees it in shadow, lit where the sun comes in through the glass. Without it every inside face took the
    /// whole sky's ambient light and the dashboard glowed as if the car had no roof. The probe captures nothing (cull
    /// mask 0, interior: no sky), so moving with the car costs it an empty capture; inside it the glossy trim reflects
    /// the dark cabin rather than the sky.
    /// </summary>
    private void BuildCabinShade(Vector3 eye)
    {
        // Narrower than the paint's cabin box: the wing mirrors' glass, just outside the doors, keeps the road's probe.
        var min = new Vector3(-0.78f, eye.Y - 0.55f, eye.Z - 1.1f);
        var max = new Vector3(0.78f, eye.Y + 0.6f, eye.Z + 0.75f);
        var shade = new ReflectionProbe
        {
            Name = "CabinShade",
            Position = (min + max) * 0.5f,
            Size = max - min,
            Interior = true,
            UpdateMode = ReflectionProbe.UpdateModeEnum.Once,
            CullMask = 0,
            EnableShadows = false,
            BlendDistance = 0.1f,
            // The capture is empty; a low intensity keeps whatever it holds (fog, a clear colour) out of the chrome.
            Intensity = 0.2f,
            AmbientMode = ReflectionProbe.AmbientModeEnum.Color,
            AmbientColor = CabinAmbient,
            AmbientColorEnergy = 1f,
            Visible = _insideCar == true,
        };
        AddChild(shade);
        _cockpitOnly.Add(shade);
        _cabinShade = shade;
    }

    private ReflectionProbe? _cabinShade;
    /// <summary>The cabin's daylight fill (BuildCabinLight) and each light's day energy, for <see cref="CabinLight"/>.</summary>
    private readonly List<(OmniLight3D Light, float Energy)> _cabinFill = [];

    /// <summary>
    /// The cabin's light against the day's (0.57): the closed cabin's dim constant ambient (CabinAmbient) and the two
    /// lights standing in for the daylight through the glass (BuildCabinLight) lit the dashboard grey at night as by day;
    /// they come down with the night sky, leaving the cabin to its instruments.
    /// </summary>
    public float CabinLight
    {
        set
        {
            if (_cabinShade is not null) _cabinShade.AmbientColor = CabinAmbient * value;
            foreach (var (light, energy) in _cabinFill) light.LightEnergy = energy * value;
        }
    }

    /// <summary>The cabin's ambient light (BuildCabinShade): about a quarter of the open sky's, a little warm from the trim it bounces off.</summary>
    private static readonly Color CabinAmbient = new(0.26f, 0.247f, 0.234f);

    /// <summary>
    /// A drone's cabin (0.21): the meshes inside it cast no shadow and are drawn only near the camera. The models
    /// bring whole cabins (seats, dashboard, door cards, the 205's roll cage), and the 205 and the Golf added to the
    /// traffic brought a hundred draw calls more (the shadow cascades draw each mesh again) for what tinted glass
    /// shows only close by; the shell shades the inside anyway. Trimmed, the eight drones draw 140 fewer than the
    /// three-car traffic before them.
    /// </summary>
    public void TrimCabinForTraffic()
    {
        if (_model is null || _modelEye is not { } eye) return;
        // From the floor to the roof lining, inside the doors, from the dashboard to the rear seats' backs: what the
        // cabin holds (the paint's lining box, SetPaintCabin, is the glasshouse over the seats).
        var cabin = new Aabb(new Vector3(-0.80f, eye.Y - 0.95f, eye.Z - 1.2f), new Vector3(1.60f, 1.55f, 2.5f));
        foreach (var mesh in Descendants(_model).OfType<MeshInstance3D>())
        {
            if (mesh.Mesh is null || !cabin.Encloses(FrameOf(mesh) * mesh.GetAabb())) continue;
            // Only the cabin's own materials: the windows, the lenses and the paint are the outside's.
            var inside = true;
            for (var s = 0; s < mesh.Mesh.GetSurfaceCount() && inside; s++)
            {
                inside = mesh.GetSurfaceOverrideMaterial(s) is null && mesh.Mesh.SurfaceGetMaterial(s)?.ResourceName.ToString() is not ("GLP_Paint" or "GLP_Glass" or "GLP_Lens" or "GLP_Brake");
            }
            if (!inside) continue;
            mesh.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
            mesh.VisibilityRangeEnd = CabinSeenWithin;
        }
    }

    /// <summary>
    /// How near a drone's cabin is drawn (TrimCabinForTraffic): the 205's red seats show through its screen at 18 m,
    /// and past 25 m a seat is a few pixels behind tinted glass.
    /// </summary>
    private const float CabinSeenWithin = 25f;

    /// <summary>World to this car's frame, for the paint's cabin test: every frame the car moves.</summary>
    public override void _Process(double delta)
    {
        if (_paints.Count == 0 || !IsInsideTree()) return;
        var toCar = new Projection(GlobalTransform.AffineInverse());
        foreach (var surface in _paints) surface.Material.SetShaderParameter("to_car", toCar);
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

    private static bool _overcast;

    /// <summary>
    /// Under cloud (Bootstrap.ApplyRain) the windows and the lamp lenses take no highlight from the sun (0.19). The overcast sun is a
    /// source ten degrees wide (WorldBuilder.SetWeather), and the near-mirror glass returned it as a glaring white
    /// block on a car's rear window, where a real window under cloud only mirrors the grey sky, which it still does.
    /// </summary>
    public static bool Overcast
    {
        set
        {
            _overcast = value;
            if (_windowOutside is not null) _windowOutside.SpecularMode = WindowSpecular;
            if (_lens is not null) _lens.SpecularMode = WindowSpecular;
        }
    }

    private static BaseMaterial3D.SpecularModeEnum WindowSpecular => _overcast ? BaseMaterial3D.SpecularModeEnum.Disabled : BaseMaterial3D.SpecularModeEnum.SchlickGgx;

    /// <summary>From the seat the windows are all but clear; from outside, tinted.</summary>
    private void SetModelWindows(bool insideCar)
    {
        var material = insideCar ? _windowInside : _windowOutside;
        foreach (var (mesh, surface) in _windows) mesh.SetSurfaceOverrideMaterial(surface, material);
    }
}
