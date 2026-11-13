using System.Runtime.InteropServices;

namespace Kumiko.Audio;

internal sealed class LinuxAudioOutput : IAudioOutput
{
    nint device;
    int channels;
    public void Open(AudioFormat format)
    {
        format.Validate();
        Check(snd_pcm_open(out device, "default", 0, 0));
        channels = format.Channels;
        try
        {
            Check(snd_pcm_set_params(device, 14, 3, (uint)channels, (uint)format.SampleRate, 1, 20000));
        }
        catch
        {
            Dispose();
            throw;
        }
    }
    public ValueTask WriteAsync(ReadOnlyMemory<float> samples, CancellationToken token) => new(Task.Run(() => Write(samples, token)));
    unsafe void Write(ReadOnlyMemory<float> samples, CancellationToken token)
    {
        if (device == 0)
            throw new InvalidOperationException("Output is not open.");
        token.ThrowIfCancellationRequested();
        // The worker owns the memory pin until native I/O returns; the player joins it before reset/disposal.
        using var registration = token.Register(() => snd_pcm_drop(device));
        using var memory = samples.Pin();
        int offset = 0, failures = 0;
        while (offset < samples.Length)
        {
            token.ThrowIfCancellationRequested();
            long frames = snd_pcm_writei(device, (nint)((float*)memory.Pointer + offset), (nuint)((samples.Length - offset) / channels));
            token.ThrowIfCancellationRequested();
            if (frames < 0)
            {
                if (++failures > 3)
                    throw new IOException("ALSA device repeatedly failed to recover.");
                int result = snd_pcm_recover(device, (int)frames, 1);
                token.ThrowIfCancellationRequested();
                Check(result);
            }
            else if (frames == 0)
                throw new IOException("ALSA output made no progress.");
            else
            {
                failures = 0;
                offset += checked((int)frames * channels);
            }
        }
    }
    public ValueTask DrainAsync(CancellationToken token) => new(Task.Run(() =>
    {
        using var registration = token.Register(() => snd_pcm_drop(device));
        token.ThrowIfCancellationRequested();
        int result = snd_pcm_drain(device);
        token.ThrowIfCancellationRequested();
        Check(result);
        Check(snd_pcm_prepare(device));
        token.ThrowIfCancellationRequested();
    }));
    public void Reset()
    {
        if (device == 0)
            return;
        Check(snd_pcm_drop(device));
        Check(snd_pcm_prepare(device));
    }
    public void Dispose()
    {
        if (device == 0)
            return;
        snd_pcm_drop(device);
        snd_pcm_close(device);
        device = 0;
    }
    static void Check(int result)
    {
        if (result < 0)
            throw new IOException("ALSA output failed: " + Marshal.PtrToStringUTF8(snd_strerror(result)));
    }
    [DllImport("libasound.so.2")]
    static extern int snd_pcm_open(out nint pcm, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, int stream, int mode);
    [DllImport("libasound.so.2")]
    static extern int snd_pcm_set_params(nint pcm, int format, int access, uint channels, uint rate, int resample, uint latency);
    [DllImport("libasound.so.2")]
    static extern long snd_pcm_writei(nint pcm, nint buffer, nuint frames);
    [DllImport("libasound.so.2")]
    static extern int snd_pcm_recover(nint pcm, int error, int silent);
    [DllImport("libasound.so.2")]
    static extern int snd_pcm_drop(nint pcm);
    [DllImport("libasound.so.2")]
    static extern int snd_pcm_drain(nint pcm);
    [DllImport("libasound.so.2")]
    static extern int snd_pcm_prepare(nint pcm);
    [DllImport("libasound.so.2")]
    static extern int snd_pcm_close(nint pcm);
    [DllImport("libasound.so.2")]
    static extern nint snd_strerror(int error);
}
