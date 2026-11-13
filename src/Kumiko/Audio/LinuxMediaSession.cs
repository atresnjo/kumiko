using System.Runtime.InteropServices;
using Kumiko.Desktop;
using S = Kumiko.Desktop.SecretVariant;

namespace Kumiko.Audio;

/// <summary>Owned MPRIS service on a private session-bus connection and blocking GLib context.</summary>
internal sealed unsafe class LinuxMediaSession(string identity) : IMediaSessionBackend
{
    const string Path = "/org/mpris/MediaPlayer2";
    const string Root = "org.mpris.MediaPlayer2";
    const string Player = "org.mpris.MediaPlayer2.Player";
    readonly object sync = new();
    nint connection, info, vtable;
    readonly List<uint> registrations = [];
    GCHandle handle;
    GioSignalContext? context;
    Action<MediaRequest>? requested;
    MediaMetadata metadata = new("none", "");
    PlaybackState state = new(PlaybackPhase.Stopped, TimeSpan.Zero, null, 1, false);
    bool disposed;
    const string Xml = """
        <node><interface name="org.mpris.MediaPlayer2">
        <method name="Raise"/><method name="Quit"/>
        <property name="CanQuit" type="b" access="read"/><property name="CanRaise" type="b" access="read"/>
        <property name="HasTrackList" type="b" access="read"/><property name="Identity" type="s" access="read"/>
        <property name="SupportedUriSchemes" type="as" access="read"/><property name="SupportedMimeTypes" type="as" access="read"/>
        </interface><interface name="org.mpris.MediaPlayer2.Player">
        <method name="Play"/><method name="Pause"/><method name="PlayPause"/><method name="Stop"/><method name="Next"/><method name="Previous"/>
        <method name="Seek"><arg type="x" direction="in"/></method>
        <method name="SetPosition"><arg type="o" direction="in"/><arg type="x" direction="in"/></method>
        <method name="OpenUri"><arg type="s" direction="in"/></method>
        <property name="PlaybackStatus" type="s" access="read"/><property name="LoopStatus" type="s" access="readwrite"/>
        <property name="Rate" type="d" access="readwrite"/><property name="Shuffle" type="b" access="readwrite"/>
        <property name="Metadata" type="a{sv}" access="read"/><property name="Volume" type="d" access="readwrite"/>
        <property name="Position" type="x" access="read"/><property name="MinimumRate" type="d" access="read"/>
        <property name="MaximumRate" type="d" access="read"/><property name="CanGoNext" type="b" access="read"/>
        <property name="CanGoPrevious" type="b" access="read"/><property name="CanPlay" type="b" access="read"/>
        <property name="CanPause" type="b" access="read"/><property name="CanSeek" type="b" access="read"/>
        <property name="CanControl" type="b" access="read"/>
        <signal name="Seeked"><arg type="x"/></signal>
        </interface></node>
        """;
    public void Start(Action<MediaRequest> requested)
    {
        if (string.IsNullOrEmpty(identity) || identity.Length > 80 || identity.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_') || char.IsDigit(identity[0]))
            throw new ArgumentException("MPRIS identity must start with a letter/underscore and contain ASCII letters, digits or underscores.");
        this.requested = requested;
        context = new(Setup, Cleanup);
    }
    void Setup()
    {
        nint address = g_dbus_address_get_for_bus_sync(2, 0, out nint error);
        SecretBusException.Throw(error);
        try
        {
            connection = g_dbus_connection_new_for_address_sync(Marshal.PtrToStringUTF8(address)!, 1 | 8, 0, 0, out error);
            SecretBusException.Throw(error);
        }
        finally
        {
            g_free(address);
        }
        info = g_dbus_node_info_new_for_xml(Xml, out error);
        SecretBusException.Throw(error);
        handle = GCHandle.Alloc(this);
        vtable = (nint)NativeMemory.AllocZeroed(8, (nuint)sizeof(nint));
        ((nint*)vtable)[0] = (nint)(delegate* unmanaged<nint, nint, nint, nint, nint, nint, nint, nint, void>)&Method;
        ((nint*)vtable)[1] = (nint)(delegate* unmanaged<nint, nint, nint, nint, nint, nint, nint, nint>)&Get;
        ((nint*)vtable)[2] = (nint)(delegate* unmanaged<nint, nint, nint, nint, nint, nint, nint, nint, int>)&Set;
        foreach (string iface in new[] { Root, Player })
        {
            uint id = g_dbus_connection_register_object(connection, Path, g_dbus_node_info_lookup_interface(info, iface), vtable, GCHandle.ToIntPtr(handle), 0, out error);
            SecretBusException.Throw(error);
            if (id == 0)
                throw new IOException("Could not register MPRIS interface.");
            registrations.Add(id);
        }
        using var name = S.String(Root + "." + identity + ".instance" + Environment.ProcessId);
        using var flags = new S(g_variant_new_uint32(4));
        using var arguments = S.Tuple(name, flags);
        nint reply = g_dbus_connection_call_sync(connection, "org.freedesktop.DBus", "/org/freedesktop/DBus", "org.freedesktop.DBus", "RequestName", arguments.Handle, 0, 0, 5000, 0, out error);
        SecretBusException.Throw(error);
        using var response = S.Adopt(reply);
        using var code = response.Child(0);
        if (code.UInt32 != 1)
            throw new IOException("MPRIS name is already owned.");
    }
    static LinuxMediaSession Owner(nint data) => (LinuxMediaSession)GCHandle.FromIntPtr(data).Target!;
    [UnmanagedCallersOnly]
    static void Method(nint connection, nint sender, nint path, nint iface, nint method, nint parameters, nint invocation, nint data)
    {
        try
        {
            var owner = Owner(data);
            string name = Marshal.PtrToStringUTF8(method) ?? "";
            MediaRequest? request;
            lock (owner.sync)
            {
                if (owner.disposed)
                    throw new ObjectDisposedException(nameof(LinuxMediaSession));
                request = name switch
                {
                    "Play" => new(MediaCommand.Play),
                    "Pause" => new(MediaCommand.Pause),
                    "PlayPause" => new(MediaCommand.Toggle),
                    "Stop" => new(MediaCommand.Stop),
                    "Next" when owner.metadata.CanNext => new(MediaCommand.Next),
                    "Previous" when owner.metadata.CanPrevious => new(MediaCommand.Previous),
                    _ => null
                };
                if (owner.metadata.CanSeek && name is "Seek" or "SetPosition")
                {
                    using var tuple = new S(parameters);
                    using var amount = tuple.Child(name == "Seek" ? 0u : 1u);
                    long micros = g_variant_get_int64(amount.Handle);
                    TimeSpan position = name == "Seek" ? owner.state.Position + TimeSpan.FromTicks(checked(micros * 10)) : TimeSpan.FromTicks(checked(micros * 10));
                    if (name == "SetPosition")
                    {
                        using var track = tuple.Child(0);
                        if (track.Text != owner.TrackId())
                            position = owner.state.Position;
                    }
                    position = TimeSpan.FromTicks(Math.Clamp(position.Ticks, 0, owner.state.Duration?.Ticks ?? long.MaxValue));
                    request = new(MediaCommand.Seek, position);
                }
                if (name == "OpenUri")
                    throw new NotSupportedException("Opening arbitrary URIs is not supported.");
            }
            if (request is not null)
                owner.requested?.Invoke(request);
            g_dbus_method_invocation_return_value(invocation, g_variant_new_tuple(0, 0));
        }
        catch (Exception error)
        {
            g_dbus_method_invocation_return_dbus_error(invocation, "org.mpris.MediaPlayer2.Error.NotSupported", error.Message);
        }
    }
    string TrackId() => "/kumiko/track/" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(metadata.Id)));
    [UnmanagedCallersOnly]
    static nint Get(nint connection, nint sender, nint path, nint iface, nint property, nint error, nint data)
    {
        try
        {
            var owner = Owner(data);
            lock (owner.sync)
                return owner.Property(Marshal.PtrToStringUTF8(property) ?? "");
        }
        catch
        {
            return 0;
        }
    }
    [UnmanagedCallersOnly]
    static int Set(nint connection, nint sender, nint path, nint iface, nint property, nint value, nint error, nint data)
    {
        try
        {
            var owner = Owner(data);
            string name = Marshal.PtrToStringUTF8(property) ?? "";
            MediaRequest? request = null;
            lock (owner.sync)
            {
                if (owner.disposed)
                    throw new ObjectDisposedException(nameof(LinuxMediaSession));
                if (name == "Volume")
                {
                    double volume = g_variant_get_double(value);
                    if (!double.IsFinite(volume) || volume < 0)
                        throw new ArgumentException("Volume must be finite and nonnegative.");
                    request = new(MediaCommand.SetVolume)
                    {
                        Volume = (float)Math.Min(1, volume)
                    };
                }
                else if (name == "LoopStatus")
                {
                    using var status = new S(value);
                    if (status.Text is not ("None" or "Track"))
                        throw new NotSupportedException("Playlist looping is not supported.");
                    request = new(MediaCommand.SetLoop)
                    {
                        Loop = status.Text == "Track"
                    };
                }
                else if (name == "Rate" && g_variant_get_double(value) == 1 || name == "Shuffle" && g_variant_get_boolean(value) == 0)
                    return 1;
                else
                    throw new NotSupportedException("This media property cannot be changed.");
            }
            owner.requested?.Invoke(request);
            return 1;
        }
        catch (Exception failure)
        {
            g_set_error_literal(error, g_dbus_error_quark(), 16, failure.Message);
            return 0;
        }
    }
    nint Property(string name) => name switch
    {
        "CanQuit" or "CanRaise" or "HasTrackList" or "Shuffle" => g_variant_new_boolean(0),
        "CanPlay" or "CanPause" or "CanControl" => g_variant_new_boolean(1),
        "CanSeek" => g_variant_new_boolean(metadata.CanSeek ? 1 : 0),
        "CanGoNext" => g_variant_new_boolean(metadata.CanNext ? 1 : 0),
        "CanGoPrevious" => g_variant_new_boolean(metadata.CanPrevious ? 1 : 0),
        "Identity" => g_variant_new_string(identity),
        "PlaybackStatus" => g_variant_new_string(state.Phase == PlaybackPhase.Playing ? "Playing" : state.Phase == PlaybackPhase.Paused ? "Paused" : "Stopped"),
        "LoopStatus" => g_variant_new_string(state.Loop ? "Track" : "None"),
        "Rate" or "MinimumRate" or "MaximumRate" => g_variant_new_double(1),
        "Volume" => g_variant_new_double(state.Volume),
        "Position" => g_variant_new_int64(state.Position.Ticks / 10),
        "SupportedUriSchemes" or "SupportedMimeTypes" => g_variant_new_strv(0, 0),
        "Metadata" => Metadata(),
        _ => 0
    };
    nint Metadata()
    {
        using var id = S.Path(TrackId());
        using var title = S.String(metadata.Title);
        using var album = S.String(metadata.Album);
        using var artist = S.String(metadata.Artist);
        nint str = Marshal.StringToCoTaskMemUTF8(metadata.Artist);
        nint array;
        try
        {
            array = g_variant_new_strv((nint)(&str), 1);
        }
        finally
        {
            Marshal.FreeCoTaskMem(str);
        }
        using var artistArray = new S(array);
        using var length = new S(g_variant_new_int64((state.Duration?.Ticks ?? 0) / 10));
        using var idBox = S.Box(id);
        using var titleBox = S.Box(title);
        using var albumBox = S.Box(album);
        using var artistBox = S.Box(artistArray);
        using var lengthBox = S.Box(length);
        using var dictionary = S.Dictionary("{sv}", ("mpris:trackid", idBox), ("xesam:title", titleBox), ("xesam:album", albumBox), ("xesam:artist", artistBox), ("mpris:length", lengthBox));
        return g_variant_ref(dictionary.Handle);
    }
    public void Publish(MediaMetadata metadata, PlaybackState state)
    {
        lock (sync)
        {
            this.metadata = metadata;
            this.state = state;
            if (disposed)
                throw new ObjectDisposedException(nameof(LinuxMediaSession));
            var properties = new List<S>();
            try
            {
                var entries = new List<(string, S)>();
                foreach (string name in new[] { "Metadata", "PlaybackStatus", "LoopStatus", "Volume", "CanGoNext", "CanGoPrevious", "CanSeek" })
                {
                    var value = name == "Metadata" ? S.Adopt(Property(name)) : new S(Property(name));
                    var box = S.Box(value);
                    properties.Add(value);
                    properties.Add(box);
                    entries.Add((name, box));
                }
                using var changed = S.Dictionary("{sv}", entries.ToArray());
                using var iface = S.String(Player);
                using var empty = new S(g_variant_new_strv(0, 0));
                using var args = S.Tuple(iface, changed, empty);
                if (g_dbus_connection_emit_signal(connection, 0, Path, "org.freedesktop.DBus.Properties", "PropertiesChanged", args.Handle, out nint error) == 0)
                    SecretBusException.Throw(error);
            }
            finally
            {
                foreach (var property in properties)
                    property.Dispose();
            }
        }
    }
    void Cleanup()
    {
        foreach (uint id in registrations)
            g_dbus_connection_unregister_object(connection, id);
        registrations.Clear();
        if (connection != 0)
        {
            g_dbus_connection_close_sync(connection, 0, out nint error);
            if (error != 0)
                g_error_free(error);
            g_object_unref(connection);
            connection = 0;
        }
        if (info != 0)
            g_dbus_node_info_unref(info);
        if (vtable != 0)
            NativeMemory.Free((void*)vtable);
        if (handle.IsAllocated)
            handle.Free();
    }
    public void Dispose()
    {
        lock (sync)
            disposed = true;
        context?.Dispose();
    }
    [DllImport(S.Gio)] static extern nint g_dbus_address_get_for_bus_sync(int type, nint cancellation, out nint error);
    [DllImport(S.Gio)] static extern nint g_dbus_connection_new_for_address_sync([MarshalAs(UnmanagedType.LPUTF8Str)] string address, int flags, nint observer, nint cancellation, out nint error);
    [DllImport(S.Gio)] static extern nint g_dbus_node_info_new_for_xml([MarshalAs(UnmanagedType.LPUTF8Str)] string xml, out nint error);
    [DllImport(S.Gio)] static extern nint g_dbus_node_info_lookup_interface(nint info, [MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [DllImport(S.Gio)] static extern uint g_dbus_connection_register_object(nint connection, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, nint info, nint table, nint data, nint destroy, out nint error);
    [DllImport(S.Gio)] static extern int g_dbus_connection_unregister_object(nint connection, uint id);
    [DllImport(S.Gio)] static extern void g_dbus_node_info_unref(nint info);
    [DllImport(S.Gio)] static extern nint g_dbus_connection_call_sync(nint connection, [MarshalAs(UnmanagedType.LPUTF8Str)] string bus, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, [MarshalAs(UnmanagedType.LPUTF8Str)] string iface, [MarshalAs(UnmanagedType.LPUTF8Str)] string method, nint args, nint type, int flags, int timeout, nint cancel, out nint error);
    [DllImport(S.Gio)] static extern int g_dbus_connection_emit_signal(nint connection, nint destination, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, [MarshalAs(UnmanagedType.LPUTF8Str)] string iface, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, nint args, out nint error);
    [DllImport(S.Gio)] static extern void g_dbus_method_invocation_return_value(nint invocation, nint result);
    [DllImport(S.Gio)] static extern void g_dbus_method_invocation_return_dbus_error(nint invocation, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, [MarshalAs(UnmanagedType.LPUTF8Str)] string message);
    [DllImport(S.Gio)] static extern int g_dbus_connection_close_sync(nint connection, nint cancellation, out nint error);
    [DllImport(S.Gio)] static extern uint g_dbus_error_quark();
    [DllImport(S.GLib)] static extern void g_set_error_literal(nint error, uint domain, int code, [MarshalAs(UnmanagedType.LPUTF8Str)] string message);
    [DllImport(S.GLib)] static extern double g_variant_get_double(nint value);
    [DllImport(S.GLib)] static extern int g_variant_get_boolean(nint value);
    [DllImport(S.GLib)] static extern nint g_variant_new_tuple(nint items, nuint count);
    [DllImport(S.GLib)] static extern nint g_variant_new_string([MarshalAs(UnmanagedType.LPUTF8Str)] string value);
    [DllImport(S.GLib)] static extern nint g_variant_new_boolean(int value);
    [DllImport(S.GLib)] static extern nint g_variant_new_double(double value);
    [DllImport(S.GLib)] static extern nint g_variant_new_int64(long value);
    [DllImport(S.GLib)] static extern nint g_variant_new_uint32(uint value);
    [DllImport(S.GLib)] static extern long g_variant_get_int64(nint value);
    [DllImport(S.GLib)] static extern nint g_variant_new_strv(nint values, nint length);
    [DllImport(S.GLib)] static extern nint g_variant_ref(nint value);
    [DllImport(S.GLib)] static extern void g_free(nint value);
    [DllImport(S.GLib)] static extern void g_error_free(nint value);
    [DllImport(S.GObject)] static extern void g_object_unref(nint value);
}
