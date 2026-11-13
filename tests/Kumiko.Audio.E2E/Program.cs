using System.Buffers.Binary;
using System.Text.Json;
using Kumiko.Audio;

var output = Path.GetFullPath(args.FirstOrDefault() ?? "artifacts/audio");
Directory.CreateDirectory(output);
File.Delete(Path.Combine(output, "report.json"));
var checks = new List<string>();
void Check(bool value, string name)
{
    if (!value)
        throw new InvalidOperationException(name);
    checks.Add(name);
}
byte[] Wave(short[] samples)
{
    using var stream = new MemoryStream();
    using var writer = new BinaryWriter(stream);
    writer.Write("RIFF"u8);
    writer.Write(36 + samples.Length * 2);
    writer.Write("WAVEfmt "u8);
    writer.Write(16);
    writer.Write((ushort)1);
    writer.Write((ushort)1);
    writer.Write(8000);
    writer.Write(16000);
    writer.Write((ushort)2);
    writer.Write((ushort)16);
    writer.Write("data"u8);
    writer.Write(samples.Length * 2);
    foreach (var sample in samples)
        writer.Write(sample);
    return stream.ToArray();
}
var wave = Wave(Enumerable.Range(0, 800).Select(i => (short)(i % 2 == 0 ? 16384 : -16384)).ToArray());
using (var decoder = new WaveDecoder(new MemoryStream(wave)))
{
    var samples = new float[4];
    Check(decoder.Read(samples) == 4 && samples[0] == .5f && samples[1] == -.5f, "signed PCM decodes into normalized interleaved frames");
    decoder.Seek(0);
    Check(decoder.PositionFrames == 0 && decoder.LengthFrames == 800, "stream seeks by complete frames");
}
foreach (var bytes in new[] { wave[..20], wave[..^1], new byte[48] })
{
    try
    {
        using var invalid = new WaveDecoder(new MemoryStream(bytes));
        Check(false, "malformed WAV rejected");
    }
    catch (InvalidDataException)
    {
        Check(true, "malformed WAV rejected without reading past input");
    }
}
var oversized = (byte[])wave.Clone();
BinaryPrimitives.WriteUInt32LittleEndian(oversized.AsSpan(40), uint.MaxValue);
try
{
    using var invalid = new WaveDecoder(new MemoryStream(oversized));
    Check(false, "overflowing chunk rejected");
}
catch (InvalidDataException)
{
    Check(true, "overflowing chunk rejected");
}
await NativeWriteFlows.Run(output, Check);
await DrainReviewFlows.Run(output, Check);
MediaFlows.Run(Check);
await ControlFlows.Run(Check);
await QueueDrainFlows.Run(Check);
await LifetimeFlows.Run(Check);
var sink = new MemoryOutput();
var decoderOwned = new WaveDecoder(new MemoryStream(wave));
await using (var player = new AudioPlayer(decoderOwned, sink))
{
    await player.SetVolumeAsync(.5f);
    await player.PlayAsync();
    await player.Completion.WaitAsync(TimeSpan.FromSeconds(5));
    Check(player.State.Phase == PlaybackPhase.Ended && sink.Samples.Count == 800, "stream reaches end and reports completion");
    Check(sink.Samples[0] == .25f, "volume is applied before output");
    await player.SeekAsync(TimeSpan.FromMilliseconds(50));
    Check(player.State.Position == TimeSpan.FromMilliseconds(50) && sink.Resets > 0, "seek flushes queued output and publishes position");
    await player.StopAsync();
    Check(player.State.Phase == PlaybackPhase.Stopped && player.State.Position == TimeSpan.Zero, "stop resets to start");
    await player.SetLoopAsync(true);
    await player.PlayAsync();
    await sink.Written.Task.WaitAsync(TimeSpan.FromSeconds(5));
    await player.PauseAsync();
    Check(player.State.Phase == PlaybackPhase.Paused, "pause is serialized with decode/output");
}
Check(sink.Disposed, "player disposal releases owned output");
var zero = new MemoryOutput();
await using (var empty = new AudioPlayer(new WaveDecoder(new MemoryStream(Wave([]))), zero))
{
    await empty.SetLoopAsync(true);
    await empty.PlayAsync();
    await empty.Completion.WaitAsync(TimeSpan.FromSeconds(5));
    Check(empty.State.Phase == PlaybackPhase.Ended, "empty looping stream terminates without spinning");
}
using var report = new Utf8JsonWriter(File.Create(Path.Combine(output, "report.json")), new() { Indented = true });
report.WriteStartObject();
report.WriteString("status", "passed");
report.WriteString("boundary", "silent decoder/output/media fixtures and private native queue/C substitution; no native device or system media session opened");
report.WriteStartArray("checks");
foreach (var check in checks)
    report.WriteStringValue(check);
report.WriteEndArray();
report.WriteEndObject();
Console.WriteLine($"Passed {checks.Count} silent audio checks: {output}");

sealed class MemoryOutput : IAudioOutput
{
    public List<float> Samples { get; } = [];
    public TaskCompletionSource Written { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public bool Disposed
    {
        get; private set;
    }
    public int Resets
    {
        get; private set;
    }
    public void Open(AudioFormat format)
    {
    }
    public ValueTask WriteAsync(ReadOnlyMemory<float> samples, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        Samples.AddRange(samples.ToArray());
        Written.TrySetResult();
        return ValueTask.CompletedTask;
    }
    public void Reset() => Resets++;
    public void Dispose() => Disposed = true;
}
