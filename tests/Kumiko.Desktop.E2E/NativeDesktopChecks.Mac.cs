using System.Runtime.InteropServices;
using Kumiko;

static partial class NativeDesktopChecks
{
    static readonly nint objc = OperatingSystem.IsMacOS() ? NativeLibrary.Load("/usr/lib/libobjc.A.dylib") : 0;
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_getClass")]
    static extern nint Class([MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "sel_registerName")]
    static extern nint Selector([MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    static unsafe nint Send(nint self, string name) => ((delegate* unmanaged<nint, nint, nint>)NativeLibrary.GetExport(objc, "objc_msgSend"))(self, Selector(name));
    static unsafe nint At(nint self, nuint index) => ((delegate* unmanaged<nint, nint, nuint, nint>)NativeLibrary.GetExport(objc, "objc_msgSend"))(self, Selector("objectAtIndex:"), index);
    static string MacText(nint value) => value == 0 ? "" : Marshal.PtrToStringUTF8(Send(value, "UTF8String")) ?? "";
    static unsafe byte Bool(nint self, string name) => ((delegate* unmanaged<nint, nint, byte>)NativeLibrary.GetExport(objc, "objc_msgSend"))(self, Selector(name));
    static unsafe nint MacString(string value)
    {
        var utf8 = Marshal.StringToCoTaskMemUTF8(value);
        try
        {
            return ((delegate* unmanaged<nint, nint, nint, nint>)NativeLibrary.GetExport(objc, "objc_msgSend"))(Class("NSString"), Selector("stringWithUTF8String:"), utf8);
        }
        finally { Marshal.FreeCoTaskMem(utf8); }
    }
    static unsafe void Set(nint self, string name, nint value) => ((delegate* unmanaged<nint, nint, nint, void>)NativeLibrary.GetExport(objc, "objc_msgSend"))(self, Selector(name), value);
    static nint FindMac(nint root, string role, string name)
    {
        if (MacText(Send(root, "accessibilityRole")) == role && MacText(Send(root, "accessibilityLabel")) == name)
            return root;
        var children = Send(root, "accessibilityChildren");
        for (nuint i = 0; i < (nuint)Send(children, "count"); i++)
        {
            var found = FindMac(At(children, i), role, name);
            if (found != 0)
                return found;
        }
        return 0;
    }
    static unsafe void Mac(App app, Window window, Action<bool, string> check)
    {
        var windows = Send(Send(Class("NSApplication"), "sharedApplication"), "windows");
        nint root = 0;
        for (nuint i = 0; i < (nuint)Send(windows, "count"); i++)
        {
            var native = At(windows, i);
            if (MacText(Send(native, "title")) == window.Options.Title)
                root = Send(native, "contentView");
        }
        check(root != 0, "NSAccessibility selects only the owned hidden window");
        var types = Send(root, "registeredDraggedTypes");
        var accepted = Enumerable.Range(0, (int)(nuint)Send(types, "count")).Select(i => MacText(At(types, (nuint)i)));
        check(accepted.Contains("public.file-url"), "the native view accepts files dragged in from Finder");
        var editor = FindMac(root, "AXTextField", "Native editor");
        var slider = FindMac(root, "AXSlider", "Native amount");
        var toggle = FindMac(root, "AXCheckBox", "Native toggle");
        check(editor != 0 && slider != 0 && toggle != 0, "NSAccessibility exposes native editable controls");
        Set(editor, "setAccessibilityValue:", MacString("Native updated 日本語"));
        var number = ((delegate* unmanaged<nint, nint, double, nint>)NativeLibrary.GetExport(objc, "objc_msgSend"))(Class("NSNumber"), Selector("numberWithDouble:"), 65);
        Set(slider, "setAccessibilityValue:", number);
        check(Bool(toggle, "accessibilityPerformPress") != 0, "NSAccessibility activates the checkbox");
        app.RunFor(TimeSpan.FromMilliseconds(80));
        check(MacText(Send(editor, "accessibilityValue")) == "Native updated 日本語", "NSAccessibility returns the updated field value");
        var password = FindMac(root, "AXTextField", "Native password");
        check(Send(password, "accessibilityValue") == 0, "NSAccessibility excludes password contents");
        var disabled = FindMac(root, "AXButton", "Native disabled");
        check(Bool(disabled, "accessibilityPerformPress") == 0, "NSAccessibility rejects disabled native actions");
    }
}
