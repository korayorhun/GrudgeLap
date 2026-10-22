using System.IO.Compression;

namespace GrudgeLap.Physics.Tests;

/// <summary>
/// Minimal RGB PNG encoder so tests can draw a top-down track map without any imaging dependency.
/// </summary>
internal sealed class PngWriter
{
    private readonly byte[] _pixels;

    public PngWriter(int width, int height, (byte R, byte G, byte B) background)
    {
        Width = width;
        Height = height;
        _pixels = new byte[width * height * 3];
        for (var i = 0; i < width * height; i++)
        {
            _pixels[i * 3] = background.R;
            _pixels[i * 3 + 1] = background.G;
            _pixels[i * 3 + 2] = background.B;
        }
    }

    public int Width { get; }
    public int Height { get; }

    public void Set(int x, int y, (byte R, byte G, byte B) c)
    {
        if (x < 0 || y < 0 || x >= Width || y >= Height) return;
        var i = (y * Width + x) * 3;
        _pixels[i] = c.R;
        _pixels[i + 1] = c.G;
        _pixels[i + 2] = c.B;
    }

    public void Disc(int cx, int cy, int r, (byte R, byte G, byte B) c)
    {
        for (var y = -r; y <= r; y++)
        for (var x = -r; x <= r; x++)
            if (x * x + y * y <= r * r) Set(cx + x, cy + y, c);
    }

    public void Save(string path)
    {
        using var ms = new MemoryStream();
        // PNG signature as raw bytes: a UTF-8 string literal would re-encode 0x89 as two bytes.
        ms.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        WriteChunk(ms, "IHDR", BuildIhdr());
        WriteChunk(ms, "IDAT", Deflate());
        WriteChunk(ms, "IEND", []);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, ms.ToArray());
    }

    private byte[] BuildIhdr()
    {
        var b = new byte[13];
        WriteBigEndian(b, 0, Width);
        WriteBigEndian(b, 4, Height);
        b[8] = 8;  // bit depth
        b[9] = 2;  // colour type RGB
        return b;
    }

    private byte[] Deflate()
    {
        using var raw = new MemoryStream();
        var stride = Width * 3;
        for (var y = 0; y < Height; y++)
        {
            raw.WriteByte(0); // filter: none
            raw.Write(_pixels, y * stride, stride);
        }
        using var compressed = new MemoryStream();
        using (var z = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            raw.Position = 0;
            raw.CopyTo(z);
        }
        return compressed.ToArray();
    }

    private static void WriteChunk(Stream s, string type, byte[] data)
    {
        var len = new byte[4];
        WriteBigEndian(len, 0, data.Length);
        s.Write(len);
        var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        s.Write(typeBytes);
        s.Write(data);
        var crc = Crc32(typeBytes, data);
        var crcBytes = new byte[4];
        WriteBigEndian(crcBytes, 0, (int)crc);
        s.Write(crcBytes);
    }

    private static void WriteBigEndian(byte[] b, int offset, int value)
    {
        b[offset] = (byte)(value >> 24);
        b[offset + 1] = (byte)(value >> 16);
        b[offset + 2] = (byte)(value >> 8);
        b[offset + 3] = (byte)value;
    }

    private static readonly uint[] CrcTable = BuildCrcTable();

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[n] = c;
        }
        return table;
    }

    private static uint Crc32(byte[] a, byte[] b)
    {
        var c = 0xFFFFFFFFu;
        foreach (var x in a) c = CrcTable[(c ^ x) & 0xFF] ^ (c >> 8);
        foreach (var x in b) c = CrcTable[(c ^ x) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFFu;
    }
}
