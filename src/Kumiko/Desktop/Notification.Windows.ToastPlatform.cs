using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Kumiko.Audio;

namespace Kumiko.Desktop;
// Runtime class names, IIDs and vtable slots are checked against windows.ui.notifications.h and
// windows.data.xml.dom.h (mingw-w64, generated from SDK metadata) and cross-checked with the
// windows-rs vtables. WinRT slots count IUnknown (0-2) and IInspectable (3-5), so each interface's
// first declared method is slot 6. Delegate IIDs equal both the header constants and the pinterface
// SHA-1 derivation of TypedEventHandler<ToastNotification, T>.
[SupportedOSPlatform("windows")]
internal sealed unsafe class WindowsToastPlatform : IToastPlatform
{
    const string ManagerClass = "Windows.UI.Notifications.ToastNotificationManager";
    const string ToastClass = "Windows.UI.Notifications.ToastNotification";
    const string XmlClass = "Windows.Data.Xml.Dom.XmlDocument";
    static readonly Guid ManagerStatics = new("50ac103f-d235-4598-bbef-98fe4d1a3ad4");
    static readonly Guid ManagerStatics2 = new("7ab93c52-0e48-4750-ba9d-1a4113981847");
    static readonly Guid ToastFactory = new("04124b20-82c6-4229-b109-fd9ed4662b53");
    static readonly Guid Toast2 = new("9dfb9fd1-143a-490e-90bf-b9fba7132de7");
    static readonly Guid XmlDocument = new("f7f3a506-1e87-42d6-bcfb-b8c809fa5494");
    static readonly Guid XmlDocumentIO = new("6cd0e74e-ee65-4489-9ebf-ca43e87ba637");
    static readonly Guid ActivatedHandler = new("ab54de2d-97d9-5528-b6ad-105afe156530");
    static readonly Guid DismissedHandler = new("61c2402f-0ed0-5a18-ab69-59f4aa99a368");
    static readonly Guid FailedHandler = new("95e3e803-c969-5e3a-9753-ea2ad22a9a33");
    // IToastNotificationManagerStatics, IToastNotificationManagerStatics2.get_History, IToastNotificationFactory.
    const int CreateToastNotifier = 6, CreateToastNotifierWithId = 7, GetHistory = 6, CreateToastNotification = 6;
    // IToastNotifier: Show, Hide, get_Setting.
    const int NotifierShow = 6, NotifierHide = 7, NotifierSetting = 8;
    // IToastNotification events and IToastNotification2 put_Tag/put_Group.
    const int AddDismissed = 9, RemoveDismissed = 10, AddActivated = 11, RemoveActivated = 12, AddFailed = 13, RemoveFailed = 14;
    const int PutTag = 6, PutGroup = 8;
    // IToastNotificationHistory overloads in metadata order.
    const int RemoveGroupedTagWithId = 8, RemoveGroupedTag = 9, HistoryClear = 11, HistoryClearWithId = 12;
    // IXmlDocumentIO.LoadXml, IToastDismissedEventArgs.get_Reason, IToastFailedEventArgs.get_ErrorCode.
    const int LoadXml = 6, EventValue = 6;
    const int ChangedMode = unchecked((int)0x80010106);
    nint notifier, history, factory;
    string? applicationId;
    bool initialized;
    WindowsToastPlatform()
    {
    }

    /// <summary>Throws PlatformNotSupportedException, with the reason, when toasts cannot be used.</summary>
    internal static WindowsToastPlatform Create(App app)
    {
        var platform = new WindowsToastPlatform();
        try
        {
            platform.Start(app);
            return platform;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            platform.Dispose();
            throw new PlatformNotSupportedException("The WinRT runtime is unavailable: " + ex.Message, ex);
        }
        catch
        {
            platform.Dispose();
            throw;
        }
    }

    void Start(App app)
    {
        int init = RoInitialize(0);
        if (init < 0 && init != ChangedMode)
            throw Unavailable("RoInitialize", init);
        initialized = init >= 0;
        nint statics = 0, statics2 = 0, probe = 0;
        try
        {
            // Probe every runtime class before touching the AppUserModelID registration.
            statics = Factory(ManagerClass, ManagerStatics);
            statics2 = Factory(ManagerClass, ManagerStatics2);
            factory = Factory(ToastClass, ToastFactory);
            using (var name = new HString(XmlClass))
            {
                int status = RoActivateInstance(name.Handle, &probe);
                if (status < 0)
                    throw Unavailable(XmlClass, status);
            }

            if (!Packaged())
                applicationId = WindowsToastIdentity.Prepare(app);
            nint result = 0;
            int created;
            if (applicationId is null)
                created = ((delegate* unmanaged[Stdcall]<nint, nint*, int>)Slot(statics, CreateToastNotifier))(statics, &result);
            else
            {
                using var id = new HString(applicationId);
                created = ((delegate* unmanaged[Stdcall]<nint, nint, nint*, int>)Slot(statics, CreateToastNotifierWithId))(statics, id.Handle, &result);
            }

            if (created < 0 || result == 0)
                throw Unavailable("CreateToastNotifier", created);
            notifier = result;
            result = 0;
            int historyStatus = ((delegate* unmanaged[Stdcall]<nint, nint*, int>)Slot(statics2, GetHistory))(statics2, &result);
            if (historyStatus < 0 || result == 0)
                throw Unavailable("ToastNotificationManager.History", historyStatus);
            history = result;
        }
        finally
        {
            Release(probe);
            Release(statics2);
            Release(statics);
        }
    }

    // APPMODEL_ERROR_NO_PACKAGE (15700) means unpackaged; a packaged process reports a short buffer.
    static bool Packaged()
    {
        uint length = 0;
        return GetCurrentPackageFullName(&length, null) != 15700;
    }

    static nint Factory(string runtimeClass, Guid iid)
    {
        using var name = new HString(runtimeClass);
        nint result = 0;
        int status = RoGetActivationFactory(name.Handle, &iid, &result);
        if (status < 0 || result == 0)
            throw Unavailable(runtimeClass, status);
        return result;
    }

    static PlatformNotSupportedException Unavailable(string what, int status) => new($"WinRT toast notifications are unavailable: {what} failed (HRESULT 0x{status:X8}).");
    public IToastHandle Show(string xml, string tag, string group, Action<ToastEvent> callback)
    {
        CheckSetting();
        nint inspectable = 0, io = 0, document = 0, toast = 0, toast2 = 0;
        Handle? handle = null;
        try
        {
            using (var name = new HString(XmlClass))
                Check(RoActivateInstance(name.Handle, &inspectable), "XmlDocument activation");
            io = Query(inspectable, XmlDocumentIO);
            using (var text = new HString(xml))
                Check(((delegate* unmanaged[Stdcall]<nint, nint, int>)Slot(io, LoadXml))(io, text.Handle), "Toast XML load");
            document = Query(inspectable, XmlDocument);
            Check(((delegate* unmanaged[Stdcall]<nint, nint, nint*, int>)Slot(factory, CreateToastNotification))(factory, document, &toast), "Toast creation");
            toast2 = Query(toast, Toast2);
            SetText(toast2, PutTag, tag, "Toast tag");
            SetText(toast2, PutGroup, group, "Toast group");
            handle = new Handle(this, toast);
            toast = 0;
            handle.Subscribe(callback);
            Check(((delegate* unmanaged[Stdcall]<nint, nint, int>)Slot(notifier, NotifierShow))(notifier, handle.Toast), "Toast display");
            var shown = handle;
            handle = null;
            return shown;
        }
        finally
        {
            handle?.Dispose();
            Release(toast2);
            Release(toast);
            Release(document);
            Release(io);
            Release(inspectable);
        }
    }

    // A readable NotificationSetting other than Enabled is reported like a denied authorization.
    void CheckSetting()
    {
        int setting = 0;
        if (((delegate* unmanaged[Stdcall]<nint, int*, int>)Slot(notifier, NotifierSetting))(notifier, &setting) < 0 || setting == 0)
            return;
        throw new InvalidOperationException(setting switch
        {
            1 => "Windows notifications are turned off for this application.",
            2 => "Windows notifications are turned off for this user.",
            3 => "Windows notifications are disabled by group policy.",
            _ => "Windows notifications are disabled by the application manifest."
        });
    }

    public void Remove(string tag, string group)
    {
        using var tagText = new HString(tag);
        using var groupText = new HString(group);
        if (applicationId is null)
            ((delegate* unmanaged[Stdcall]<nint, nint, nint, int>)Slot(history, RemoveGroupedTag))(history, tagText.Handle, groupText.Handle);
        else
        {
            using var id = new HString(applicationId);
            ((delegate* unmanaged[Stdcall]<nint, nint, nint, nint, int>)Slot(history, RemoveGroupedTagWithId))(history, tagText.Handle, groupText.Handle, id.Handle);
        }
    }

    public void Clear()
    {
        if (applicationId is null)
            ((delegate* unmanaged[Stdcall]<nint, int>)Slot(history, HistoryClear))(history);
        else
        {
            using var id = new HString(applicationId);
            ((delegate* unmanaged[Stdcall]<nint, nint, int>)Slot(history, HistoryClearWithId))(history, id.Handle);
        }
    }

    public void Dispose()
    {
        Release(history);
        Release(notifier);
        Release(factory);
        history = notifier = factory = 0;
        if (initialized)
            RoUninitialize();
        initialized = false;
    }

    /// <summary>Owns one ToastNotification and its agile event delegates (see MediaComHandler).</summary>
    sealed class Handle(WindowsToastPlatform owner, nint toast) : IToastHandle
    {
        MediaComHandler? activated, dismissed, failed;
        long activatedToken, dismissedToken, failedToken;
        internal nint Toast => toast;
        internal void Subscribe(Action<ToastEvent> callback)
        {
            activated = Add(AddActivated, Delegate(ActivatedHandler, callback, _ => new(ToastEventKind.Activated)), out activatedToken);
            dismissed = Add(AddDismissed, Delegate(DismissedHandler, callback, args => new(ToastEventKind.Dismissed, Value(args))), out dismissedToken);
            failed = Add(AddFailed, Delegate(FailedHandler, callback, args => new(ToastEventKind.Failed, Value(args))), out failedToken);
        }

        // Callbacks arrive on toast worker threads; nothing may escape into the native caller.
        static MediaComHandler Delegate(Guid iid, Action<ToastEvent> callback, Func<nint, ToastEvent> read) => new(iid, (_, args) =>
        {
            try
            {
                callback(read(args));
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
            }
        });
        // Both event argument types expose one 32-bit property at slot 6; -1 marks an unreadable value.
        static int Value(nint args)
        {
            int value = -1;
            if (args == 0 || ((delegate* unmanaged[Stdcall]<nint, int*, int>)Slot(args, EventValue))(args, &value) < 0)
                return -1;
            return value;
        }

        // The toast holds its own delegate reference once added; ours is dropped on failure or Dispose.
        MediaComHandler Add(int slot, MediaComHandler handler, out long token)
        {
            long value = 0;
            int status = ((delegate* unmanaged[Stdcall]<nint, nint, long*, int>)Slot(toast, slot))(toast, handler.Handle, &value);
            if (status < 0)
            {
                handler.Dispose();
                Check(status, "Toast event subscription");
            }

            token = value;
            return handler;
        }

        void Remove(int slot, ref MediaComHandler? handler, long token)
        {
            if (handler is null)
                return;
            ((delegate* unmanaged[Stdcall]<nint, long, int>)Slot(toast, slot))(toast, token);
            handler.Dispose();
            handler = null;
        }

        public void Hide()
        {
            if (toast != 0)
                ((delegate* unmanaged[Stdcall]<nint, nint, int>)Slot(owner.notifier, NotifierHide))(owner.notifier, toast);
        }

        public void Dispose()
        {
            if (toast == 0)
                return;
            Remove(RemoveActivated, ref activated, activatedToken);
            Remove(RemoveDismissed, ref dismissed, dismissedToken);
            Remove(RemoveFailed, ref failed, failedToken);
            Release(toast);
            toast = 0;
        }
    }

    readonly struct HString : IDisposable
    {
        internal nint Handle
        {
            get;
        }

        internal HString(string value)
        {
            nint result;
            fixed (char* text = value)
                Check(WindowsCreateString(text, (uint)value.Length, &result), "HSTRING creation");
            Handle = result;
        }

        public void Dispose()
        {
            if (Handle != 0)
                WindowsDeleteString(Handle);
        }
    }

    static void SetText(nint value, int slot, string text, string what)
    {
        using var native = new HString(text);
        Check(((delegate* unmanaged[Stdcall]<nint, nint, int>)Slot(value, slot))(value, native.Handle), what);
    }

    static nint Query(nint value, Guid iid)
    {
        nint result = 0;
        Check(((delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)Slot(value, 0))(value, &iid, &result), "QueryInterface " + iid);
        return result;
    }

    static void* Slot(nint value, int index) => (*(void***)value)[index];
    static void Release(nint value)
    {
        if (value != 0)
            ((delegate* unmanaged[Stdcall]<nint, uint>)Slot(value, 2))(value);
    }

    static void Check(int status, string what)
    {
        if (status < 0)
            throw new IOException($"{what} failed (HRESULT 0x{status:X8}).");
    }

    [DllImport("combase.dll")]
    static extern int RoInitialize(uint type);
    [DllImport("combase.dll")]
    static extern void RoUninitialize();
    [DllImport("combase.dll")]
    static extern int WindowsCreateString(char* text, uint length, nint* result);
    [DllImport("combase.dll")]
    static extern int WindowsDeleteString(nint value);
    [DllImport("combase.dll")]
    static extern int RoGetActivationFactory(nint name, Guid* iid, nint* factory);
    [DllImport("combase.dll")]
    static extern int RoActivateInstance(nint name, nint* instance);
    [DllImport("kernel32.dll")]
    static extern int GetCurrentPackageFullName(uint* length, char* name);
}
