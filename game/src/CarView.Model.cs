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
    /// colour, or the key colour a paint texture was masked by) and whether it is drawn from a texture.
    /// </summary>
    private readonly record struct PaintSurface(ShaderMaterial Material, Color Factory, bool Textured);

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
    /// body's; the model's own colour when none is chosen), the windows tinted as the lofted glass from outside and
    /// nearly clear from the seat, the lamp lenses clear. Everything else keeps the model's own.
    /// </summary>
    private void DressModel(Node3D model)
    {
        // One paint material per paint material of the model, shared by its surfaces: the cabin's frame is updated
        // on each every frame (_Process), and a model has one to three.
        var paints = new Dictionary<Material, ShaderMaterial>();
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
        // The model's own colour, for what takes the car's colour from Paint (the debris of a crash).
        if (_paintChoice is null && _paints.Count > 0) _paint = _paints[0].Factory;
    }

    /// <summary>The model materials already corrected: the imported scene is cached, so its materials are shared by every car drawn with it.</summary>
    private static readonly HashSet<BaseMaterial3D> _corrected = [];

    /// <summary>
    /// Physically plausible values for a model's own materials where the exporter left none (0.19). Many of the CC BY
    /// models carry the glTF defaults (metallic 1, roughness 1, no texture to drive them): a matt plastic or a dial
    /// then shades as rough grey metal, a mirror's glass as a white blot. Tyres were exported glossy (roughness 0.15 on
    /// the Testarossa, a third metallic on the Renault) and read as plastic toys' wheels. The material is corrected in
    /// place, once, as every instance of the cached scene shares it.
    /// </summary>
    private static void CorrectModelMaterial(BaseMaterial3D material)
    {
        if (!_corrected.Add(material)) return;
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

    /// <summary>
    /// The paint for one of the model's paint materials: the texture paint when it has a texture and the mask the
    /// preparation made for it (emission slot), the flat paint otherwise, in the chosen colour or the model's own.
    /// </summary>
    private ShaderMaterial PaintFor(Material source)
    {
        PaintSurface surface;
        if (source is BaseMaterial3D { AlbedoTexture: { } albedo, EmissionTexture: { } mask } own)
        {
            // The key colour the texture was masked by (linear, in the material's extras): the factory paint's colour.
            var factory = DefaultPaint;
            if (own.HasMeta("extras") && own.GetMeta("extras").AsGodotDictionary() is { } extras
                && extras.TryGetValue("glp_paint_key", out var key) && key.AsFloat32Array() is { Length: 3 } k)
            {
                factory = new Color(k[0], k[1], k[2]).LinearToSrgb();
            }
            surface = new PaintSurface(TexturePaint(own, albedo, mask), factory, Textured: true);
        }
        else
        {
            // A flat paint's own colour; one with a texture but no mask (a model prepared before the masks) as before.
            var factory = source is BaseMaterial3D flat ? flat.AlbedoColor : DefaultPaint;
            surface = new PaintSurface(ModelPaint(factory), factory, Textured: false);
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
    /// so the texture's shading, panel lines, grime and lettering stay in any colour, under the flat paint's coat
    /// (metallic 0.05, roughness 0.38, clearcoat); everything else is the model's own material (its texture,
    /// roughness, metallic and normal maps) untouched. The factory paint is the texture itself under the coat. The
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
                uniform bool factory = false;
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
                        vec3 painted = factory ? texel : min(paint * (mask.g * 2.0), vec3(1.0));
                        ALBEDO = mix(texel, painted, coat);
                        float own_roughness = dot(texture(roughness_texture, UV), roughness_channel) * roughness;
                        float own_metallic = dot(texture(metallic_texture, UV), metallic_channel) * metallic;
                        ROUGHNESS = mix(own_roughness, 0.38, coat);
                        METALLIC = mix(own_metallic, 0.05, coat);
                        CLEARCOAT = coat;
                        CLEARCOAT_ROUGHNESS = 0.04;
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

    /// <summary>A texture channel as the vector that picks it out of a texel (BaseMaterial3D's own convention).</summary>
    private static Vector4 Channel(BaseMaterial3D.TextureChannel channel) => channel switch
    {
        BaseMaterial3D.TextureChannel.Red => new Vector4(1f, 0f, 0f, 0f),
        BaseMaterial3D.TextureChannel.Green => new Vector4(0f, 1f, 0f, 0f),
        BaseMaterial3D.TextureChannel.Blue => new Vector4(0f, 0f, 1f, 0f),
        BaseMaterial3D.TextureChannel.Alpha => new Vector4(0f, 0f, 0f, 1f),
        _ => new Vector4(1f / 3f, 1f / 3f, 1f / 3f, 0f),
    };

    /// <summary>The chosen colour, or the model's own, on one of its paints.</summary>
    private void ApplyPaint(PaintSurface surface)
    {
        surface.Material.SetShaderParameter("paint", _paintChoice ?? surface.Factory);
        // A paint texture's own colour is the texture itself, not its key's one hue.
        if (surface.Textured) surface.Material.SetShaderParameter("factory", _paintChoice is null);
    }

    /// <summary>
    /// Repaints the car in place (the menu's PAINT): a colour, or null for the model's own. A lofted body has its
    /// colour built into its meshes and is not repainted here (false): the caller builds it again.
    /// </summary>
    public bool Repaint(Color? paint)
    {
        if (_paints.Count == 0) return false;
        _paintChoice = paint;
        _paint = paint ?? _paints[0].Factory;
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
    }

    /// <summary>The cabin's ambient light (BuildCabinShade): about a quarter of the open sky's, a little warm from the trim it bounces off.</summary>
    private static readonly Color CabinAmbient = new(0.26f, 0.247f, 0.234f);

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
