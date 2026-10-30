using Godot;

namespace GrudgeLap.Game;

/// <summary>
/// A triangle mesh collected in managed lists and handed to the engine in one call; the same calls as
/// <see cref="SurfaceTool"/> for the parts this game uses. SurfaceTool costs one engine call per vertex
/// attribute, and for the terrain, the roads and the car lofts (several hundred thousand vertices) that was
/// most of the second the world took to build at start-up.
/// </summary>
internal sealed class MeshBuilder
{
    private readonly List<Vector3> _vertices = [];
    private readonly List<Vector3> _normals = [];
    private readonly List<Color> _colours = [];
    private readonly List<Vector2> _uvs = [];
    private readonly List<Vector2> _uv2s = [];
    private readonly List<float> _tangentData = [];
    private readonly List<int> _indices = [];
    private Vector3 _normal = Vector3.Up;
    private Color _colour = Colors.White;
    private Vector2 _uv;
    private Vector2 _uv2;
    private Vector3 _tangent = Vector3.Right;
    private float _tangentSign = 1f;
    private bool _hasUv;
    private bool _hasUv2;
    private bool _hasTangent;
    private bool _tangents;

    /// <summary>Vertices added so far: the index the next one gets.</summary>
    public int VertexCount => _vertices.Count;

    public void SetNormal(Vector3 normal) => _normal = normal;
    public void SetColor(Color colour) => _colour = colour;

    public void SetUV(Vector2 uv)
    {
        _uv = uv;
        _hasUv = true;
    }

    /// <summary>The second UV set, for a material's detail layer mapped independently of the main texture.</summary>
    public void SetUV2(Vector2 uv)
    {
        _uv2 = uv;
        _hasUv2 = true;
    }

    /// <summary>
    /// A tangent known from the geometry (the u direction of a grid or a road strip) and the sign that orients
    /// the bitangent along v; with these, <see cref="Commit"/> skips the general tangent solver, which cost
    /// a third of a second on the terrain alone.
    /// </summary>
    public void SetTangent(Vector3 tangent, float sign)
    {
        _tangent = tangent;
        _tangentSign = sign;
        _hasTangent = true;
    }

    /// <summary>Adds a vertex with the normal, colour, UV and tangent set last, as SurfaceTool does.</summary>
    public void AddVertex(Vector3 vertex)
    {
        _vertices.Add(vertex);
        _normals.Add(_normal);
        _colours.Add(_colour);
        _uvs.Add(_uv);
        _uv2s.Add(_uv2);
        _tangentData.Add(_tangent.X);
        _tangentData.Add(_tangent.Y);
        _tangentData.Add(_tangent.Z);
        _tangentData.Add(_tangentSign);
    }

    public void AddIndex(int index) => _indices.Add(index);

    /// <summary>Asks <see cref="Commit"/> for tangents (normal-mapped materials); needs UVs.</summary>
    public void GenerateTangents() => _tangents = true;

    public ArrayMesh Commit()
    {
        var mesh = new ArrayMesh();
        if (_vertices.Count == 0) return mesh;
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = _vertices.ToArray();
        arrays[(int)Mesh.ArrayType.Normal] = _normals.ToArray();
        arrays[(int)Mesh.ArrayType.Color] = _colours.ToArray();
        if (_hasUv) arrays[(int)Mesh.ArrayType.TexUV] = _uvs.ToArray();
        if (_hasUv2) arrays[(int)Mesh.ArrayType.TexUV2] = _uv2s.ToArray();
        if (_hasTangent) arrays[(int)Mesh.ArrayType.Tangent] = _tangentData.ToArray();
        if (_indices.Count > 0) arrays[(int)Mesh.ArrayType.Index] = _indices.ToArray();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        if (_hasTangent || !_tangents || !_hasUv) return mesh;
        // MikkTSpace in the engine, on the whole surface at once: the same tangents SurfaceTool made before.
        var tool = new SurfaceTool();
        tool.CreateFrom(mesh, 0);
        tool.GenerateTangents();
        return tool.Commit();
    }
}
