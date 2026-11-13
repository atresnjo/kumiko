using System.Runtime.InteropServices;
using System.Text.Json;
using Kumiko;

static unsafe class NativeAccessibilityChecks
{
    public static void Run(App app, string title, string output, Action<bool, string> check)
    {
        if (OperatingSystem.IsMacOS())
        {
            // Query this process's hidden test window through AppKit, never the
            // system-wide accessibility service or another application's state.
            var application = Send(Class("NSApplication"), "sharedApplication");
            var windows = Send(application, "windows");
            nint view = 0;
            for (nuint i = 0; i < (nuint)Send(windows, "count"); i++)
            {
                var window = SendAt(windows, i);
                if (Text(Send(window, "title")) == title)
                    view = Send(window, "contentView");
            }

            check(view != 0, "accessibility query selects only the owned hidden window");
            var button = Find(view, "AXButton", "Increment");
            check(button != 0, "AppKit exposes semantic controls in the hidden native tree");
            var disabled = Find(view, "AXButton", "Disabled");
            check(disabled != 0 && Bool(disabled, "isAccessibilityEnabled") == 0 && Bool(disabled, "accessibilityPerformPress") == 0, "native accessibility rejects disabled actions");
            check(Bool(button, "isAccessibilityEnabled") != 0, "native accessibility exposes enabled state");
            check(Bool(button, "accessibilityPerformPress") != 0, "native accessibility press reaches Kumiko input");
            app.RunFor(TimeSpan.FromMilliseconds(40));
            check(Find(view, "AXButton", "Increment") == button, "native accessibility retains keyed elements between frames");
            Write(output, "NSAccessibility", "Increment");
        }
        else if (OperatingSystem.IsWindows())
        {
            var handle = FindWindowW("Kumiko.CSharp", title);
            check(handle != 0, "UI Automation selects the owned hidden window");
            GetWindowThreadProcessId(handle, out var process);
            check(process == (uint)Environment.ProcessId, "UI Automation queries only this process");
            int hr = UiaNodeFromHandle(handle, out var node);
            check(hr >= 0 && node != 0, "UI Automation connects to the hidden window provider");
            try
            {
                hr = UiaGetPropertyValue(node, 30005, out var value);
                try
                {
                    check(
                        hr >= 0 && value.Type == 8 && Marshal.PtrToStringBSTR(value.Pointer) == title,
                        "UI Automation reads the native provider name"
                    );
                }
                finally
                {
                    VariantClear(ref value);
                }
            }
            finally
            {
                UiaNodeRelease(node);
            }

            Write(output, "UIAutomation", title);
        }
    }

    static void Write(string output, string provider, string name)
    {
        using var file = File.Create(Path.Combine(output, "native-accessibility.json"));
        using var writer = new Utf8JsonWriter(file);
        writer.WriteStartObject();
        writer.WriteString("provider", provider);
        writer.WriteString("name", name);
        writer.WriteEndObject();
    }

    static nint Find(nint parent, string role, string name)
    {
        if (Text(Send(parent, "accessibilityRole")) == role && Text(Send(parent, "accessibilityLabel")) == name)
            return parent;
        var children = Send(parent, "accessibilityChildren");
        if (children == 0)
            return 0;
        for (nuint i = 0; i < (nuint)Send(children, "count"); i++)
        {
            var found = Find(SendAt(children, i), role, name);
            if (found != 0)
                return found;
        }

        return 0;
    }

    static byte Bool(nint self, string selector) => ((delegate* unmanaged<nint, nint, byte>)objc_msgSend)(self, Sel(selector));
    static nint Send(nint self, string selector) => ((delegate* unmanaged<nint, nint, nint>)objc_msgSend)(self, Sel(selector));
    static nint SendAt(nint self, nuint index) => ((delegate* unmanaged<nint, nint, nuint, nint>)objc_msgSend)(self, Sel("objectAtIndex:"), index);
    static string Text(nint value) => value == 0 ? "" : Marshal.PtrToStringUTF8(Send(value, "UTF8String")) ?? "";
    static nint objc_msgSend => NativeLibrary.GetExport(ObjCLibrary, "objc_msgSend");

    static readonly nint ObjCLibrary = OperatingSystem.IsMacOS() ? NativeLibrary.Load("/usr/lib/libobjc.A.dylib") : 0;
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_getClass")]
    static extern nint Class([MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "sel_registerName")]
    static extern nint Sel([MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern nint FindWindowW(string className, string title);
    [DllImport("user32.dll")]
    static extern uint GetWindowThreadProcessId(nint window, out uint process);
    [DllImport("UIAutomationCore.dll")]
    static extern int UiaNodeFromHandle(nint window, out nint node);
    [DllImport("UIAutomationCore.dll")]
    static extern int UiaGetPropertyValue(nint node, int property, out NativeVariant value);
    [DllImport("UIAutomationCore.dll")]
    static extern int UiaNodeRelease(nint node);
    [DllImport("oleaut32.dll")]
    static extern int VariantClear(ref NativeVariant value);
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    struct NativeVariant
    {
        [FieldOffset(0)]
        public ushort Type;
        [FieldOffset(8)]
        public nint Pointer;
    }
}
