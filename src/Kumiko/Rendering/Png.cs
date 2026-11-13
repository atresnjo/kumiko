using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace Kumiko.Rendering;

public static class Png
{
    static ReadOnlySpan<byte> Signature => [137, 80, 78, 71, 13, 10, 26, 10];

    static uint Crc(ReadOnlySpan<byte> data, uint crc = uint.MaxValue)
    {
        foreach (byte b in data)
        {
            crc ^= b;
            for (int i = 0; i < 8; i++)
                crc = (crc >> 1) ^ (0xedb88320u & (uint)-(int)(crc & 1));
        }

        return crc;
    }

    static void Chunk(Stream output, string type, ReadOnlySpan<byte> bytes)
    {
        Span<byte> header = stackalloc byte[8];
        BinaryPrimitives.WriteInt32BigEndian(header, bytes.Length);
        Encoding.ASCII.GetBytes(type, header[4..]);
        output.Write(header);
        output.Write(bytes);
        Span<byte> checksum = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(checksum, ~Crc(bytes, Crc(header[4..])));
        output.Write(checksum);
    }

    public static void Write(string path, Pixels pixels)
    {
        using var file = File.Create(path);
        Write(file, pixels);
    }

    public static void Write(Stream stream, Pixels pixels)
    {
        stream.Write(Signature);
        Span<byte> ihdr = stackalloc byte[13];
        ihdr.Clear();
        BinaryPrimitives.WriteInt32BigEndian(ihdr, pixels.Width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr[4..], pixels.Height);
        ihdr[8] = 8;
        ihdr[9] = 6;
        Chunk(stream, "IHDR", ihdr);
        using var compressed = new MemoryStream();
        using (var z = new ZLibStream(compressed, CompressionLevel.Fastest, true))
        {
            var row = new byte[checked(pixels.Width * 4 + 1)];
            for (int y = 0; y < pixels.Height; y++)
            {
                row[0] = 0;
                for (int x = 0; x < pixels.Width; x++)
                {
                    int source = (y * pixels.Width + x) * 4, dest = 1 + x * 4;
                    var a = pixels.Bytes[source + 3];
                    row[dest] = (byte)(a == 0 ? 0 : Math.Min(255, pixels.Bytes[source + 2] * 255 / a));
                    row[dest + 1] = (byte)(a == 0 ? 0 : Math.Min(255, pixels.Bytes[source + 1] * 255 / a));
                    row[dest + 2] = (byte)(a == 0 ? 0 : Math.Min(255, pixels.Bytes[source] * 255 / a));
                    row[dest + 3] = a;
                }

                z.Write(row);
            }
        }

        Chunk(stream, "IDAT", compressed.GetBuffer().AsSpan(0, (int)compressed.Length));
        Chunk(stream, "IEND", []);
    }

    public static Pixels Read(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 33 || !bytes[..8].SequenceEqual(Signature))
            throw new InvalidDataException("Not a PNG.");
        int width = 0, height = 0, color = 0, bitDepth = 0;
        byte[] palette = [], transparency = [];
        bool end = false;
        using var compressed = new MemoryStream();
        for (int offset = 8; offset < bytes.Length;)
        {
            if (bytes.Length - offset < 12)
                throw new InvalidDataException("Truncated PNG chunk.");
            int length = BinaryPrimitives.ReadInt32BigEndian(bytes[offset..]);
            if (length < 0 || length > bytes.Length - offset - 12)
                throw new InvalidDataException("Invalid PNG chunk size.");
            var type = bytes.Slice(offset + 4, 4);
            var chunk = bytes.Slice(offset + 8, length);
            uint crc = BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(offset + 8 + length, 4));
            if (~Crc(chunk, Crc(type)) != crc)
                throw new InvalidDataException("PNG checksum mismatch.");
            switch (Encoding.ASCII.GetString(type))
            {
                case "IHDR":
                    if (length != 13)
                        throw new InvalidDataException("Invalid PNG header.");
                    width = BinaryPrimitives.ReadInt32BigEndian(chunk);
                    height = BinaryPrimitives.ReadInt32BigEndian(chunk[4..]);
                    bitDepth = chunk[8];
                    color = chunk[9];
                    if (bitDepth != 8 || chunk[12] != 0)
                        throw new NotSupportedException("PNG requires 8-bit noninterlaced pixels.");
                    break;
                case "PLTE":
                    palette = chunk.ToArray();
                    break;
                case "tRNS":
                    transparency = chunk.ToArray();
                    break;
                case "IDAT":
                    compressed.Write(chunk);
                    break;
                case "IEND":
                    end = true;
                    break;
            }

            offset += length + 12;
            if (end)
                break;
        }

        if (!end)
            throw new InvalidDataException("PNG has no IEND chunk.");
        var output = Pixels.Create(width, height);
        int channels = color switch
        {
            0 or 3 => 1,
            2 => 3,
            4 => 2,
            6 => 4,
            _ => throw new NotSupportedException("PNG color type.")
        };
        int stride = checked(width * channels);
        compressed.Position = 0;
        using var zlib = new ZLibStream(compressed, CompressionMode.Decompress);
        var row = new byte[stride];
        var previous = new byte[stride];
        for (int y = 0; y < height; y++)
        {
            int filter = zlib.ReadByte();
            zlib.ReadExactly(row);
            for (int x = 0; x < stride; x++)
            {
                int a = x >= channels ? row[x - channels] : 0, b = previous[x], c = x >= channels ? previous[x - channels] : 0;
                int predictor = filter switch
                {
                    0 => 0,
                    1 => a,
                    2 => b,
                    3 => (a + b) / 2,
                    4 => Paeth(a, b, c),
                    _ => throw new InvalidDataException("Invalid PNG filter.")
                };
                row[x] = unchecked((byte)(row[x] + predictor));
            }

            for (int x = 0; x < width; x++)
            {
                int p = x * channels;
                byte r, g, b, a = 255;
                if (color == 3)
                {
                    int entry = row[p];
                    if (entry * 3 + 2 >= palette.Length)
                        throw new InvalidDataException("PNG palette index.");
                    r = palette[entry * 3];
                    g = palette[entry * 3 + 1];
                    b = palette[entry * 3 + 2];
                    if (entry < transparency.Length)
                        a = transparency[entry];
                }
                else if (color is 0 or 4)
                {
                    r = g = b = row[p];
                    if (color == 4)
                        a = row[p + 1];
                }
                else
                {
                    r = row[p];
                    g = row[p + 1];
                    b = row[p + 2];
                    if (color == 6)
                        a = row[p + 3];
                }

                int at = (y * width + x) * 4;
                output.Bytes[at] = (byte)(b * a / 255);
                output.Bytes[at + 1] = (byte)(g * a / 255);
                output.Bytes[at + 2] = (byte)(r * a / 255);
                output.Bytes[at + 3] = a;
            }

            (row, previous) = (previous, row);
        }

        return output;
    }

    static int Paeth(int a, int b, int c)
    {
        int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }
}
