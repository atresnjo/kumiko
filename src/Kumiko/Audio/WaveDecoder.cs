using System.Buffers.Binary;

namespace Kumiko.Audio;

/// <summary>Bounded RIFF/WAVE streaming decoder: little-endian PCM 8/16/24/32 and IEEE float 32/64.
/// Compressed WAV, RF64 and extensible channel layouts require a separately supplied decoder.</summary>
public sealed class WaveDecoder : IAudioDecoder
{
    readonly Stream input;
    readonly bool leaveOpen;
    readonly long dataStart, dataBytes;
    readonly int bytesPerSample, frameBytes;
    readonly ushort encoding, bits;
    bool disposed;
    public AudioFormat Format
    {
        get;
    }
    public long? LengthFrames => dataBytes / frameBytes;
    public long PositionFrames
    {
        get; private set;
    }
    public bool CanSeek => true;

    public WaveDecoder(Stream input, bool leaveOpen = false)
    {
        this.input = input;
        this.leaveOpen = leaveOpen;
        try
        {
            if (!input.CanRead || !input.CanSeek)
                throw new ArgumentException("WAV requires a readable seekable stream.", nameof(input));
            Span<byte> header = stackalloc byte[12];
            Exact(header);
            if (!header[..4].SequenceEqual("RIFF"u8) || !header[8..].SequenceEqual("WAVE"u8))
                throw new InvalidDataException("Not a little-endian RIFF/WAVE file.");
            long end = checked(input.Position - 4 + BinaryPrimitives.ReadUInt32LittleEndian(header[4..]));
            if (end > input.Length || end < input.Position)
                throw new InvalidDataException("Truncated RIFF container.");
            AudioFormat? format = null;
            long start = -1, length = 0;
            ushort tag = 0, depth = 0, block = 0;
            int chunks = 0;
            Span<byte> chunk = stackalloc byte[8];
            Span<byte> fmt = stackalloc byte[16];
            while (input.Position < end)
            {
                if (++chunks > 65536 || end - input.Position < 8)
                    throw new InvalidDataException("Invalid WAV chunk table.");
                Exact(chunk);
                long size = BinaryPrimitives.ReadUInt32LittleEndian(chunk[4..]);
                long next = checked(input.Position + size + (size & 1));
                if (next > end)
                    throw new InvalidDataException("Truncated WAV chunk.");
                if (chunk[..4].SequenceEqual("fmt "u8))
                {
                    if (size < 16 || format is not null)
                        throw new InvalidDataException("Invalid or duplicate WAV format.");
                    Exact(fmt);
                    tag = BinaryPrimitives.ReadUInt16LittleEndian(fmt);
                    int channels = BinaryPrimitives.ReadUInt16LittleEndian(fmt[2..]);
                    uint rate = BinaryPrimitives.ReadUInt32LittleEndian(fmt[4..]);
                    block = BinaryPrimitives.ReadUInt16LittleEndian(fmt[12..]);
                    depth = BinaryPrimitives.ReadUInt16LittleEndian(fmt[14..]);
                    if (rate is < 8000 or > 384000 || channels is < 1 or > 8 ||
                        !(tag == 1 && depth is 8 or 16 or 24 or 32 || tag == 3 && depth is 32 or 64) ||
                        block != channels * (depth / 8) || BinaryPrimitives.ReadUInt32LittleEndian(fmt[8..]) != rate * block)
                        throw new InvalidDataException("Unsupported WAV sample format or inconsistent frame size.");
                    format = new((int)rate, channels);
                }
                else if (chunk[..4].SequenceEqual("data"u8))
                {
                    if (start >= 0)
                        throw new InvalidDataException("Multiple data chunks are unsupported.");
                    start = input.Position;
                    length = size;
                }
                input.Position = next;
            }
            if (format is null || start < 0 || length % block != 0)
                throw new InvalidDataException("Missing format/data or incomplete WAV frame.");
            Format = format.Value;
            encoding = tag;
            bits = depth;
            bytesPerSample = bits / 8;
            frameBytes = block;
            dataStart = start;
            dataBytes = length;
            input.Position = dataStart;
        }
        catch (EndOfStreamException error)
        {
            if (!leaveOpen)
                input.Dispose();
            throw new InvalidDataException("Truncated WAV stream.", error);
        }
        catch
        {
            if (!leaveOpen)
                input.Dispose();
            throw;
        }
    }
    void Exact(Span<byte> buffer) => input.ReadExactly(buffer);
    public int Read(Span<float> samples)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        int count = (int)Math.Min(samples.Length / Format.Channels, LengthFrames!.Value - PositionFrames) * Format.Channels;
        Span<byte> sample = stackalloc byte[8];
        for (int i = 0; i < count; i++)
        {
            Exact(sample[..bytesPerSample]);
            float value;
            if (encoding == 3)
                value = bits == 32 ? BinaryPrimitives.ReadSingleLittleEndian(sample) : (float)BinaryPrimitives.ReadDoubleLittleEndian(sample);
            else
                value = bits switch
                {
                    8 => (sample[0] - 128) / 128f,
                    16 => BinaryPrimitives.ReadInt16LittleEndian(sample) / 32768f,
                    24 => ((sample[0] | sample[1] << 8 | sample[2] << 16) << 8 >> 8) / 8388608f,
                    _ => BinaryPrimitives.ReadInt32LittleEndian(sample) / 2147483648f
                };
            samples[i] = float.IsFinite(value) ? Math.Clamp(value, -1, 1) : 0;
        }
        PositionFrames += count / Format.Channels;
        return count;
    }
    public void Seek(long frame)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentOutOfRangeException.ThrowIfNegative(frame);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(frame, LengthFrames!.Value);
        input.Position = dataStart + frame * frameBytes;
        PositionFrames = frame;
    }
    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        if (!leaveOpen)
            input.Dispose();
    }
}
