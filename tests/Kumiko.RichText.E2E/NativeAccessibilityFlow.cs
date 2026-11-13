using System.Runtime.InteropServices;
using Kumiko;
using Kumiko.Platform;
using Kumiko.RichText;
using Kumiko.Runtime;

static unsafe class NativeAccessibilityFlow
{
    internal static void Run(Action<bool, string> check)
    {
        if (!OperatingSystem.IsMacOS())
            return;
        var initial = EditorApp.Init with
        {
            Editor = RichDocument.Create("Accessible rich text").Select(0, 10).Apply(new RichCommand.Toggle(RichFormat.Bold))
        };
        using var program = EditorApp.CreateProgram(initial);
        using var app = new App(new(Name: "Rich accessibility fixture", Hidden: true));
        var errors = new List<Exception>();
        app.UnhandledException += errors.Add;
        using var window = app.NewWindow(new(Title: "Rich accessibility fixture", Visible: false), program);
        app.RunFor(TimeSpan.FromMilliseconds(80));
        nint view = ObjC.O(window.NativeHandle, "contentView");
        nint editor = Find(view, "Editor");
        check(editor != 0, "hidden AppKit provider exposes native rich editor");
        ObjC.V(editor, "retain");
        try
        {
            nint attributed = ((delegate* unmanaged<nint, nint, NativeRange, nint>)Send)(editor, ObjC.Sel("accessibilityAttributedStringForRange:"), new(0, 10));
            string description = ObjC.Text(ObjC.O(attributed, "description"));
            check(description.Contains("Bold", StringComparison.Ordinal), "native AppKit attributed-range query announces bold formatting");
            ((delegate* unmanaged<nint, nint, NativeRange, void>)Send)(editor, ObjC.Sel("setAccessibilitySelectedTextRange:"), new(0, 5));
            app.RunFor(TimeSpan.FromMilliseconds(40));
            check(program.Model.Editor.Selection is { Start: 0, End: 5 }, "native accessibility selection dispatches model-owned command");
            window.Dispose();
            nint stale = ((delegate* unmanaged<nint, nint, NativeRange, nint>)Send)(editor, ObjC.Sel("accessibilityAttributedStringForRange:"), new(0, 5));
            check(stale == 0 && errors.Count == 0, "retained accessibility element is inert after hidden window disposal");
        }
        finally
        {
            ObjC.Release(editor);
        }
    }
    static nint Find(nint element, string label)
    {
        if (ObjC.Text(ObjC.O(element, "accessibilityLabel")) == label)
            return element;
        nint children = ObjC.O(element, "accessibilityChildren");
        for (nuint i = 0; i < (nuint)ObjC.N(children, "count"); i++)
        {
            nint found = Find(ObjC.O(children, "objectAtIndex:", i), label);
            if (found != 0)
                return found;
        }
        return 0;
    }
    static nint Send => NativeLibrary.GetExport(NativeLibrary.Load("/usr/lib/libobjc.A.dylib"), "objc_msgSend");
}
