using System.Runtime.InteropServices;
using Kumiko.UI;

// Traversal and actions use the actual AppKit provider, not Context or model access.
sealed unsafe class NativeTree
{
    readonly nint root;
    readonly Dictionary<string, nint> current = new();
    public NativeTree(string title)
    {
        var windows = Send(Send(Class("NSApplication"), "sharedApplication"), "windows");
        for (nuint i = 0; i < (nuint)Send(windows, "count"); i++)
        {
            var window = At(windows, i);
            if (Text(Send(window, "title")) == title)
                root = Send(window, "contentView");
        }
        if (root == 0)
            throw new InvalidOperationException("Owned hidden native window not found");
    }
    public ProbeNode[] Snapshot()
    {
        current.Clear();
        var nodes = new List<ProbeNode>();
        Visit(root, "", nodes);
        return nodes.ToArray();
    }
    void Visit(nint element, string parent, List<ProbeNode> nodes)
    {
        var id = Text(Send(element, "accessibilityIdentifier"));
        if (id.Length > 0)
        {
            current[id] = element;
            var value = Send(element, "accessibilityValue");
            nodes.Add(new(id, parent, Text(Send(element, "accessibilityRole")), Text(Send(element, "accessibilityLabel")),
                value == 0 ? null : Text(Send(value, "description")), Bool(element, "isAccessibilityEnabled") != 0,
                Bool(element, "isAccessibilityExpanded") != 0, Text(Send(element, "accessibilityHelp"))));
            parent = id;
        }
        var children = Send(element, "accessibilityChildren");
        for (nuint i = 0; i < (nuint)Send(children, "count"); i++)
            Visit(At(children, i), parent, nodes);
    }
    public bool Action(string id, string action, string? value)
    {
        var element = current[id];
        if (action == "press")
            return Bool(element, "accessibilityPerformPress") != 0;
        if (action == "end")
            ((delegate* unmanaged<nint, nint, nint, void>)Message)(element, Sel("setAccessibilityValue:"), Send(element, "accessibilityMaxValue"));
        else if (action == "expand")
            ((delegate* unmanaged<nint, nint, byte, void>)Message)(element, Sel("setAccessibilityExpanded:"), 1);
        else
            ((delegate* unmanaged<nint, nint, nint, void>)Message)(element, Sel("setAccessibilityValue:"), String(value!));
        // Objective-C setters return void; callers must verify the observable result.
        return true;
    }
    public RetainedPress RetainPress(string id)
    {
        var element = Send(current[id], "retain");
        return new(() => Bool(element, "accessibilityPerformPress") != 0, () => Send(element, "release"));
    }
    public static string RoleName(Role role) => role switch
    {
        Role.ScrollArea => "AXScrollArea",
        Role.Button => "AXButton",
        Role.TextField => "AXTextField",
        Role.ComboBox => "AXComboBox",
        Role.MenuItem => "AXMenuItem",
        Role.Row or Role.Option or Role.ListItem or Role.TreeItem => "AXRow",
        Role.Text or Role.Heading => "AXStaticText",
        Role.Checkbox or Role.Switch => "AXCheckBox",
        Role.Image => "AXImage",
        Role.Grid => "AXTable",
        Role.Cell => "AXCell",
        Role.ColumnHeader => "AXColumn",
        Role.Menu => "AXMenu",
        Role.Separator => "AXSplitter",
        _ => "AXGroup"
    };
    static nint String(string text)
    {
        var utf8 = Marshal.StringToCoTaskMemUTF8(text);
        try
        {
            return ((delegate* unmanaged<nint, nint, nint, nint>)Message)(Class("NSString"), Sel("stringWithUTF8String:"), utf8);
        }
        finally
        {
            Marshal.FreeCoTaskMem(utf8);
        }
    }
    static byte Bool(nint self, string selector) => ((delegate* unmanaged<nint, nint, byte>)Message)(self, Sel(selector));
    static nint Send(nint self, string selector) => ((delegate* unmanaged<nint, nint, nint>)Message)(self, Sel(selector));
    static nint At(nint self, nuint index) => ((delegate* unmanaged<nint, nint, nuint, nint>)Message)(self, Sel("objectAtIndex:"), index);
    static string Text(nint value) => value == 0 ? "" : Marshal.PtrToStringUTF8(Send(value, "UTF8String")) ?? "";
    static readonly nint Library = OperatingSystem.IsMacOS() ? NativeLibrary.Load("/usr/lib/libobjc.A.dylib") : 0;
    static nint Message => NativeLibrary.GetExport(Library, "objc_msgSend");
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_getClass")]
    static extern nint Class([MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "sel_registerName")]
    static extern nint Sel([MarshalAs(UnmanagedType.LPUTF8Str)] string name);
}
