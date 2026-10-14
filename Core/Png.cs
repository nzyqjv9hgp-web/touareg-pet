using System.Buffers.Binary;
using System.IO.Compression;

namespace Touareg.Core;

// Minimal PNG reader for the car textures: 8-bit RGB or RGBA, not interlaced. Returns raw BGRA bytes with no
// colour management, so data maps (metallic/roughness) arrive exactly as baked. GDI+ would apply gAMA/sRGB chunks.
public static class Png
{
    public static (int Width, int Height, byte[] Bgra) Decode(byte[] file)
    {
        if (file.Length < 8 || BinaryPrimitives.ReadUInt64BigEndian(file) != 0x89504E470D0A1A0A) throw new InvalidDataException("Not a PNG");
        int width = 0, height = 0, channels = 0;
        using var compressed = new MemoryStream();
        for (int at = 8; at + 8 <= file.Length;)
        {
            int length = BinaryPrimitives.ReadInt32BigEndian(file.AsSpan(at));
            string type = System.Text.Encoding.ASCII.GetString(file, at + 4, 4);
            var data = file.AsSpan(at + 8, length);
            if (type == "IHDR")
            {
                width = BinaryPrimitives.ReadInt32BigEndian(data); height = BinaryPrimitives.ReadInt32BigEndian(data[4..]);
                byte depth = data[8], colour = data[9], interlace = data[12];
                channels = colour switch { 2 => 3, 6 => 4, _ => 0 };
                if (depth != 8 || channels == 0 || interlace != 0) throw new InvalidDataException("Only 8-bit RGB/RGBA non-interlaced PNG is supported");
            }
            else if (type == "IDAT") compressed.Write(data);
            else if (type == "IEND") break;
            at += 12 + length;
        }
        compressed.Position = 0;
        using var inflate = new ZLibStream(compressed, CompressionMode.Decompress);
        int stride = width * channels;
        var raw = new byte[(stride + 1) * height];
        inflate.ReadExactly(raw);

        var pixels = new byte[stride * height];
        for (int y = 0; y < height; y++)
        {
            byte filter = raw[y * (stride + 1)];
            var line = raw.AsSpan(y * (stride + 1) + 1, stride);
            var row = pixels.AsSpan(y * stride, stride);
            var up = y > 0 ? pixels.AsSpan((y - 1) * stride, stride) : new byte[stride];
            for (int i = 0; i < stride; i++)
            {
                int a = i >= channels ? row[i - channels] : 0, b = up[i], c = i >= channels ? up[i - channels] : 0;
                row[i] = (byte)(line[i] + filter switch
                {
                    0 => 0,
                    1 => a,
                    2 => b,
                    3 => (a + b) / 2,
                    4 => Paeth(a, b, c),
                    _ => throw new InvalidDataException("Bad PNG filter"),
                });
            }
        }
        var bgra = new byte[width * height * 4];
        for (int i = 0, o = 0; i < pixels.Length; i += channels, o += 4)
        {
            bgra[o] = pixels[i + 2]; bgra[o + 1] = pixels[i + 1]; bgra[o + 2] = pixels[i];
            bgra[o + 3] = channels == 4 ? pixels[i + 3] : (byte)255;
        }
        return (width, height, bgra);
    }

    static int Paeth(int a, int b, int c)
    {
        int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }
}
