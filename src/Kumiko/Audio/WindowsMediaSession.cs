using System.Runtime.InteropServices;

namespace Kumiko.Audio;

internal sealed unsafe class WindowsMediaSession(nint window) : IMediaSessionBackend
{
    nint controls, controls2, updater, music;
    long buttonsToken, seekToken;
    MediaComHandler? buttons, seeking;
    bool initialized, disposed;
    MediaMetadata? metadata;
    static nint Slot(nint obj, int index) => (*(nint**)obj)[index];
    static void Check(int result) => Marshal.ThrowExceptionForHR(result);
    static void Release(nint value)
    {
        if (value != 0)
            ((delegate* unmanaged[Stdcall]<nint, uint>)Slot(value, 2))(value);
    }
    static nint Property(nint obj, int index)
    {
        nint value;
        Check(((delegate* unmanaged[Stdcall]<nint, nint*, int>)Slot(obj, index))(obj, &value));
        return value;
    }
    static void Integer(nint obj, int index, int value) => Check(((delegate* unmanaged[Stdcall]<nint, int, int>)Slot(obj, index))(obj, value));
    static void Boolean(nint obj, int index, bool value) => Check(((delegate* unmanaged[Stdcall]<nint, byte, int>)Slot(obj, index))(obj, (byte)(value ? 1 : 0)));
    static nint String(string value)
    {
        Check(WindowsCreateString(value, (uint)value.Length, out nint result));
        return result;
    }
    static void Text(nint obj, int index, string value)
    {
        nint text = String(value);
        try
        {
            Check(((delegate* unmanaged[Stdcall]<nint, nint, int>)Slot(obj, index))(obj, text));
        }
        finally
        {
            WindowsDeleteString(text);
        }
    }
    public void Start(Action<MediaRequest> requested)
    {
        int init = RoInitialize(0);
        if (init < 0 && init != unchecked((int)0x80010106))
            Check(init);
        initialized = init >= 0;
        nint name = String("Windows.Media.SystemMediaTransportControls"), factory = 0;
        try
        {
            var interop = new Guid("ddb0472d-c911-4a1f-86d9-dc3d71a95f5a");
            Check(RoGetActivationFactory(name, ref interop, out factory));
            var iid = new Guid("99fa3ff4-1742-42a6-902e-087d41f965ec");
            nint result;
            Check(((delegate* unmanaged[Stdcall]<nint, nint, Guid*, nint*, int>)Slot(factory, 3))(factory, window, &iid, &result));
            controls = result;
            iid = new("ea98d2f6-7f3c-4af2-a586-72889808efb1");
            Check(((delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)Slot(controls, 0))(controls, &iid, &result));
            controls2 = result;
            updater = Property(controls, 8);
            Integer(updater, 7, 1);
            music = Property(updater, 12);
            buttons = new("SystemMediaTransportControlsButtonPressedEventArgs", "b7f47116-a56f-4dc8-9e11-92031f4a87c2", args =>
            {
                int button;
                Check(((delegate* unmanaged[Stdcall]<nint, int*, int>)Slot(args, 6))(args, &button));
                MediaCommand? command = button switch
                {
                    0 => MediaCommand.Play,
                    1 => MediaCommand.Pause,
                    2 => MediaCommand.Stop,
                    6 => MediaCommand.Next,
                    7 => MediaCommand.Previous,
                    _ => null
                };
                var current = Volatile.Read(ref metadata);
                if (!disposed && current is not null && command is { } action && (action != MediaCommand.Next || current.CanNext) && (action != MediaCommand.Previous || current.CanPrevious))
                    requested(new(action));
            });
            long token;
            Check(((delegate* unmanaged[Stdcall]<nint, nint, long*, int>)Slot(controls, 32))(controls, buttons.Handle, &token));
            buttonsToken = token;
            seeking = new("PlaybackPositionChangeRequestedEventArgs", "b4493f88-eb28-4961-9c14-335e44f3e125", args =>
            {
                long ticks;
                Check(((delegate* unmanaged[Stdcall]<nint, long*, int>)Slot(args, 6))(args, &ticks));
                if (!disposed && Volatile.Read(ref metadata) is { CanSeek: true } && ticks >= 0)
                    requested(new(MediaCommand.Seek, TimeSpan.FromTicks(ticks)));
            });
            Check(((delegate* unmanaged[Stdcall]<nint, nint, long*, int>)Slot(controls2, 13))(controls2, seeking.Handle, &token));
            seekToken = token;
        }
        finally
        {
            Release(factory);
            WindowsDeleteString(name);
        }
    }
    public void Publish(MediaMetadata metadata, PlaybackState state)
    {
        Volatile.Write(ref this.metadata, metadata);
        Text(music, 7, metadata.Title);
        Text(music, 11, metadata.Artist);
        Check(((delegate* unmanaged[Stdcall]<nint, int>)Slot(updater, 17))(updater));
        Integer(controls, 7, state.Phase == PlaybackPhase.Playing ? 3 : state.Phase == PlaybackPhase.Paused ? 4 : 2);
        Boolean(controls, 11, true);
        Boolean(controls, 13, true);
        Boolean(controls, 15, true);
        Boolean(controls, 17, true);
        Boolean(controls, 25, metadata.CanPrevious);
        Boolean(controls, 27, metadata.CanNext);
        nint name = String("Windows.Media.SystemMediaTransportControlsTimelineProperties"), timeline = 0;
        try
        {
            Check(RoActivateInstance(name, out timeline));
            long duration = state.Duration?.Ticks ?? state.Position.Ticks;
            void Set(int index, long ticks) => Check(((delegate* unmanaged[Stdcall]<nint, long, int>)Slot(timeline, index))(timeline, ticks));
            Set(7, 0);
            Set(9, duration);
            Set(11, metadata.CanSeek ? 0 : state.Position.Ticks);
            Set(13, metadata.CanSeek ? duration : state.Position.Ticks);
            Set(15, state.Position.Ticks);
            Check(((delegate* unmanaged[Stdcall]<nint, nint, int>)Slot(controls2, 12))(controls2, timeline));
        }
        finally
        {
            Release(timeline);
            WindowsDeleteString(name);
        }
    }
    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        if (controls != 0)
        {
            if (buttons is not null)
                ((delegate* unmanaged[Stdcall]<nint, long, int>)Slot(controls, 33))(controls, buttonsToken);
            if (seeking is not null && controls2 != 0)
                ((delegate* unmanaged[Stdcall]<nint, long, int>)Slot(controls2, 14))(controls2, seekToken);
            Boolean(controls, 11, false);
        }
        buttons?.Dispose();
        seeking?.Dispose();
        Release(music);
        Release(updater);
        Release(controls2);
        Release(controls);
        if (initialized)
            RoUninitialize();
    }
    [DllImport("combase.dll")]
    static extern int RoInitialize(uint type);
    [DllImport("combase.dll")]
    static extern void RoUninitialize();
    [DllImport("combase.dll", CharSet = CharSet.Unicode)]
    static extern int WindowsCreateString(string text, uint length, out nint result);
    [DllImport("combase.dll")]
    static extern int WindowsDeleteString(nint value);
    [DllImport("combase.dll")]
    static extern int RoGetActivationFactory(nint name, ref Guid iid, out nint factory);
    [DllImport("combase.dll")]
    static extern int RoActivateInstance(nint name, out nint instance);
}
