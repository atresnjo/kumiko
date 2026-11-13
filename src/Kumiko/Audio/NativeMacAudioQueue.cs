using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Kumiko.Audio;

internal sealed class NativeMacAudioQueue : IMacAudioQueue
{
    const string Library = "/System/Library/Frameworks/AudioToolbox.framework/AudioToolbox";
    [StructLayout(LayoutKind.Sequential)]
    struct Description
    {
        internal double Rate;
        internal uint Format, Flags, BytesPerPacket, FramesPerPacket, BytesPerFrame, Channels, Bits, Reserved;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct Buffer
    {
        internal uint Capacity;
        internal nint Data;
        internal uint Size;
        internal nint User;
        internal uint PacketCapacity;
        internal nint Packets;
        internal uint PacketCount;
    }
    const uint Running = 0x6171726e;
    nint queue, buffer;
    GCHandle context;
    Action? returned, runningChanged;
    bool listening;
    public unsafe bool IsRunning
    {
        get
        {
            if (queue == 0)
                return false;
            uint value = 0, size = sizeof(uint);
            Check(AudioQueueGetProperty(queue, Running, (nint)(&value), ref size));
            return value != 0;
        }
    }
    public unsafe void Open(AudioFormat format, Action returned, Action runningChanged)
    {
        if (queue != 0)
            throw new InvalidOperationException("Output already opened.");
        format.Validate();
        this.returned = returned;
        this.runningChanged = runningChanged;
        context = GCHandle.Alloc(this);
        var description = new Description
        {
            Rate = format.SampleRate,
            Format = 0x6c70636d,
            Flags = 1 | 8,
            BytesPerPacket = (uint)format.Channels * 4,
            FramesPerPacket = 1,
            BytesPerFrame = (uint)format.Channels * 4,
            Channels = (uint)format.Channels,
            Bits = 32
        };
        try
        {
            Check(AudioQueueNewOutput(ref description, &Returned, GCHandle.ToIntPtr(context), 0, 0, 0, out queue));
            Check(AudioQueueAddPropertyListener(queue, Running, &Changed, GCHandle.ToIntPtr(context)));
            listening = true;
            Check(AudioQueueAllocateBuffer(queue, 1024 * 1024, out buffer));
        }
        catch
        {
            Dispose();
            throw;
        }
    }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    static void Returned(nint user, nint queue, nint buffer)
    {
        try
        {
            ((NativeMacAudioQueue)GCHandle.FromIntPtr(user).Target!).returned?.Invoke();
        }
        catch { }
    }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    static void Changed(nint user, nint queue, uint property)
    {
        try
        {
            ((NativeMacAudioQueue)GCHandle.FromIntPtr(user).Target!).runningChanged?.Invoke();
        }
        catch { }
    }
    public unsafe void Submit(ReadOnlySpan<float> samples)
    {
        if (queue == 0)
            throw new InvalidOperationException("Output is not open.");
        var native = (Buffer*)buffer;
        int bytes = checked(samples.Length * 4);
        if (bytes > native->Capacity)
            throw new ArgumentOutOfRangeException(nameof(samples));
        samples.CopyTo(new Span<float>((void*)native->Data, samples.Length));
        native->Size = (uint)bytes;
        Check(AudioQueueEnqueueBuffer(queue, buffer, 0, 0));
    }
    public void Start() => Check(AudioQueueStart(queue, 0));
    public void Stop(bool immediate) => Check(AudioQueueStop(queue, (byte)(immediate ? 1 : 0)));
    public void Reset() => Check(AudioQueueReset(queue));
    public unsafe void Dispose()
    {
        if (queue != 0)
        {
            AudioQueueStop(queue, 1);
            if (listening)
                AudioQueueRemovePropertyListener(queue, Running, &Changed, GCHandle.ToIntPtr(context));
            // Do not release callback context unless disposal has synchronously quiesced the queue.
            Check(AudioQueueDispose(queue, 1));
            queue = 0;
            buffer = 0;
        }
        if (context.IsAllocated)
            context.Free();
    }
    static void Check(int status)
    {
        if (status != 0)
            throw new IOException("AudioQueue failed: " + status);
    }
    [DllImport(Library)]
    static extern unsafe int AudioQueueNewOutput(ref Description format, delegate* unmanaged[Cdecl]<nint, nint, nint, void> callback, nint user, nint loop, nint mode, uint flags, out nint queue);
    [DllImport(Library)]
    static extern int AudioQueueAllocateBuffer(nint queue, uint capacity, out nint buffer);
    [DllImport(Library)]
    static extern int AudioQueueEnqueueBuffer(nint queue, nint buffer, uint count, nint packets);
    [DllImport(Library)]
    static extern int AudioQueueStart(nint queue, nint time);
    [DllImport(Library)]
    static extern int AudioQueueStop(nint queue, byte immediate);
    [DllImport(Library)]
    static extern int AudioQueueReset(nint queue);
    [DllImport(Library)]
    static extern int AudioQueueDispose(nint queue, byte immediate);
    [DllImport(Library)]
    static extern unsafe int AudioQueueAddPropertyListener(nint queue, uint property, delegate* unmanaged[Cdecl]<nint, nint, uint, void> listener, nint user);
    [DllImport(Library)]
    static extern unsafe int AudioQueueRemovePropertyListener(nint queue, uint property, delegate* unmanaged[Cdecl]<nint, nint, uint, void> listener, nint user);
    [DllImport(Library)]
    static extern int AudioQueueGetProperty(nint queue, uint property, nint value, ref uint size);
}
