using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace DouYing.Server;

public static class MediaGenerator
{
    private static readonly byte[][] Palettes =
    [
        [254, 44, 85, 123, 47, 247], [37, 244, 238, 29, 38, 113],
        [255, 154, 68, 252, 96, 118], [67, 233, 123, 56, 249, 215],
        [250, 112, 154, 254, 225, 64], [48, 207, 208, 51, 8, 103],
        [255, 94, 247, 28, 7, 64]
    ];

    public static void Generate(string seed, string filePath, bool avatar = false)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        var digest = SHA1.HashData(Encoding.UTF8.GetBytes(seed));
        var palette = Palettes[digest[0] % Palettes.Length];
        var width = avatar ? 256 : 360;
        var height = avatar ? 256 : 640;
        var raw = new byte[(width * 3 + 1) * height];
        var angle = 0.35 + digest[1] / 255d * 0.5;
        for (var y = 0; y < height; y++)
        {
            var row = y * (width * 3 + 1);
            raw[row] = 0;
            for (var x = 0; x < width; x++)
            {
                var nx = x / (double)width;
                var ny = y / (double)height;
                var t = Math.Clamp((nx * 0.35 + ny * 0.65) * 0.9 + 0.05, 0, 1);
                var band = Math.Sin((nx * Math.Cos(angle) + ny * Math.Sin(angle)) * 9) * 0.5 + 0.5;
                var vignette = Math.Max(0, 1 - ((nx - 0.5) * (nx - 0.5) + (ny - 0.5) * (ny - 0.5)) * 1.35);
                var light = 18 * band;
                var offset = row + 1 + x * 3;
                raw[offset] = Color(palette[0], palette[3], t, light, vignette);
                raw[offset + 1] = Color(palette[1], palette[4], t, light, vignette);
                raw[offset + 2] = Color(palette[2], palette[5], t, light, vignette);
            }
        }

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
            zlib.Write(raw);

        using var output = File.Create(filePath);
        output.Write([137, 80, 78, 71, 13, 10, 26, 10]);
        Span<byte> header = stackalloc byte[13];
        WriteUInt32(header, 0, (uint)width);
        WriteUInt32(header, 4, (uint)height);
        header[8] = 8;
        header[9] = 2;
        WriteChunk(output, "IHDR", header);
        WriteChunk(output, "IDAT", compressed.ToArray());
        WriteChunk(output, "IEND", []);
    }

    private static byte Color(int start, int end, double t, double light, double vignette) =>
        (byte)Math.Clamp(((start + (end - start) * t + light) * (0.55 + vignette * 0.45)), 0, 255);

    private static void WriteChunk(Stream output, string name, ReadOnlySpan<byte> data)
    {
        Span<byte> size = stackalloc byte[4];
        WriteUInt32(size, 0, (uint)data.Length);
        output.Write(size);
        var type = Encoding.ASCII.GetBytes(name);
        output.Write(type);
        output.Write(data);
        var crcInput = new byte[type.Length + data.Length];
        type.CopyTo(crcInput, 0);
        data.CopyTo(crcInput.AsSpan(type.Length));
        Span<byte> crc = stackalloc byte[4];
        WriteUInt32(crc, 0, Crc32(crcInput));
        output.Write(crc);
    }

    private static uint Crc32(ReadOnlySpan<byte> data)
    {
        var crc = 0xffffffffu;
        foreach (var value in data)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
                crc = (crc & 1) != 0 ? 0xedb88320u ^ (crc >> 1) : crc >> 1;
        }
        return crc ^ 0xffffffffu;
    }

    private static void WriteUInt32(Span<byte> destination, int offset, uint value)
    {
        destination[offset] = (byte)(value >> 24);
        destination[offset + 1] = (byte)(value >> 16);
        destination[offset + 2] = (byte)(value >> 8);
        destination[offset + 3] = (byte)value;
    }
}
