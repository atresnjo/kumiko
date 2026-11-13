using System.Runtime.InteropServices;

namespace Kumiko.Audio;

internal sealed class WindowsAudioOutput : IAudioOutput
{
    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    struct Format
    {
        internal ushort Tag, Channels;
        internal uint Rate, BytesPerSecond;
        internal ushort FrameBytes, Bits, Extra;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct Header
    {
        internal nint Data;
        internal uint Length, Recorded;
        internal nuint User;
        internal uint Flags, Loops;
        internal nint Next;
        internal nuint Reserved;
    }
    readonly AutoResetEvent ready = new(false);
    nint device;
    bool disposed;
    public void Open(AudioFormat format)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        format.Validate();
        var native = new Format
        {
            Tag = 3,
            Channels = (ushort)format.Channels,
            Rate = (uint)format.SampleRate,
            BytesPerSecond = (uint)(format.SampleRate * format.Channels * 4),
            FrameBytes = (ushort)(format.Channels * 4),
            Bits = 32
        };
        Check(waveOutOpen(out device, uint.MaxValue, ref native, ready.SafeWaitHandle.DangerousGetHandle(), 0, 0x50000));
    }
    public ValueTask WriteAsync(ReadOnlyMemory<float> samples, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return new(Task.Run(() => Write(samples, token), token));
    }
    void Write(ReadOnlyMemory<float> samples, CancellationToken token)
    {
        if (device == 0)
            throw new InvalidOperationException("Output is not open.");
        nint data = Marshal.AllocHGlobal(checked(samples.Length * 4));
        nint header = Marshal.AllocHGlobal(Marshal.SizeOf<Header>());
        bool prepared = false;
        try
        {
            Marshal.Copy(samples.ToArray(), 0, data, samples.Length);
            Marshal.StructureToPtr(new Header { Data = data, Length = (uint)(samples.Length * 4) }, header, false);
            Check(waveOutPrepareHeader(device, header, (uint)Marshal.SizeOf<Header>()));
            prepared = true;
            Check(waveOutWrite(device, header, (uint)Marshal.SizeOf<Header>()));
            var handles = new WaitHandle[] { ready, token.WaitHandle };
            while ((Marshal.PtrToStructure<Header>(header).Flags & 1) == 0)
            {
                if (WaitHandle.WaitAny(handles) == 1)
                {
                    Check(waveOutReset(device));
                    token.ThrowIfCancellationRequested();
                }
            }
        }
        finally
        {
            if (prepared)
            {
                waveOutReset(device);
                waveOutUnprepareHeader(device, header, (uint)Marshal.SizeOf<Header>());
            }
            Marshal.FreeHGlobal(header);
            Marshal.FreeHGlobal(data);
        }
    }
    public void Reset()
    {
        if (device != 0)
            Check(waveOutReset(device));
    }
    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        if (device != 0)
        {
            waveOutReset(device);
            waveOutClose(device);
            device = 0;
        }
        ready.Dispose();
    }
    static void Check(uint result)
    {
        if (result != 0)
            throw new IOException("Windows wave output failed: " + result);
    }
    [DllImport("winmm.dll")]
    static extern uint waveOutOpen(out nint handle, uint id, ref Format format, nint callback, nuint user, uint flags);
    [DllImport("winmm.dll")]
    static extern uint waveOutPrepareHeader(nint handle, nint header, uint size);
    [DllImport("winmm.dll")]
    static extern uint waveOutWrite(nint handle, nint header, uint size);
    [DllImport("winmm.dll")]
    static extern uint waveOutUnprepareHeader(nint handle, nint header, uint size);
    [DllImport("winmm.dll")]
    static extern uint waveOutReset(nint handle);
    [DllImport("winmm.dll")]
    static extern uint waveOutClose(nint handle);
}
