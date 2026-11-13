using System.Diagnostics;
using System.Runtime.InteropServices;
using Kumiko;

static partial class NativeDesktopChecks
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern nint FindWindowW(string className, string title);
    [DllImport("ole32.dll")] static extern int CoInitializeEx(nint reserved, uint model);
    [DllImport("ole32.dll")] static extern void CoUninitialize();
    [DllImport("ole32.dll")] static extern int CoCreateInstance(ref Guid cls, nint outer, uint context, ref Guid iid, out nint value);
    [DllImport("ole32.dll")] static extern int CoGetApartmentType(out int type, out int qualifier);
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    struct Variant
    {
        [FieldOffset(0)] public ushort Type;
        [FieldOffset(8)] public nint Pointer;
    }
    static unsafe nint Method(nint value, int index) => (*(nint**)value)[index];
    static unsafe nint FindUia(nint automation, nint root, string name)
    {
        var value = new Variant { Type = 8, Pointer = Marshal.StringToBSTR(name) };
        nint condition = 0, found = 0;
        try
        {
            Marshal.ThrowExceptionForHR(((delegate* unmanaged<nint, int, Variant, nint*, int>)Method(automation, 23))(automation, 30005, value, &condition));
            Marshal.ThrowExceptionForHR(((delegate* unmanaged<nint, int, nint, nint*, int>)Method(root, 5))(root, 4, condition, &found));
            return found;
        }
        finally
        {
            Marshal.FreeBSTR(value.Pointer);
            if (condition != 0)
                Marshal.Release(condition);
        }
    }
    static unsafe nint Pattern(nint element, int id, string iid)
    {
        var guid = new Guid(iid);
        nint pointer = 0;
        Marshal.ThrowExceptionForHR(((delegate* unmanaged<nint, int, Guid*, nint*, int>)Method(element, 14))(element, id, &guid, &pointer));
        return pointer;
    }
    static void Windows(App app, Window window, Action<bool, string> check)
    {
        // This program has top-level statements, so .NET started its main thread in the multithreaded
        // apartment. OLE drag and drop, the clipboard and IME need the App's thread single-threaded.
        Marshal.ThrowExceptionForHR(CoGetApartmentType(out int apartment, out _));
        check(apartment is 0 or 3, "the App's thread joins a single-threaded apartment from .NET's default multithreaded one");
        var worker = Task.Run(() => WindowsWorker(window.Options.Title, check));
        var elapsed = Stopwatch.StartNew();
        while (!worker.IsCompleted && elapsed.Elapsed < TimeSpan.FromSeconds(15))
            app.RunFor(TimeSpan.FromMilliseconds(20));
        check(worker.IsCompleted, "native UIA client finishes while the main event loop remains responsive");
        worker.GetAwaiter().GetResult();
    }
    static unsafe void WindowsWorker(string title, Action<bool, string> check)
    {
        Marshal.ThrowExceptionForHR(CoInitializeEx(0, 0));
        nint automation = 0, root = 0, editor = 0, slider = 0, toggle = 0;
        var patterns = new List<nint>();
        try
        {
            var cls = new Guid("ff48dba4-60ef-4201-aa87-54103eef594e");
            var iid = new Guid("30cbe57d-d9d0-452a-ab13-7ac5ac4825ee");
            Marshal.ThrowExceptionForHR(CoCreateInstance(ref cls, 0, 1, ref iid, out automation));
            var handle = FindWindowW("Kumiko.CSharp", title);
            Marshal.ThrowExceptionForHR(((delegate* unmanaged<nint, nint, nint*, int>)Method(automation, 6))(automation, handle, &root));
            editor = FindUia(automation, root, "Native editor");
            slider = FindUia(automation, root, "Native amount");
            toggle = FindUia(automation, root, "Native toggle");
            check(editor != 0 && slider != 0 && toggle != 0, "UIA client finds controls in the owned hidden window");
            var value = Pattern(editor, 10002, "a94cd8b1-0844-4cd6-9d2d-640537ab39e9");
            patterns.Add(value);
            fixed (char* text = "Native updated 日本語")
                Marshal.ThrowExceptionForHR(((delegate* unmanaged<nint, char*, int>)Method(value, 3))(value, text));
            var range = Pattern(slider, 10003, "59213f4f-7346-49e5-b120-80555987a148");
            patterns.Add(range);
            Marshal.ThrowExceptionForHR(((delegate* unmanaged<nint, double, int>)Method(range, 3))(range, 65));
            var toggler = Pattern(toggle, 10015, "94cf8058-9b8d-4ab9-8bfd-4cd0a33c8c70");
            patterns.Add(toggler);
            Marshal.ThrowExceptionForHR(((delegate* unmanaged<nint, int>)Method(toggler, 3))(toggler));
            check(true, "native UIA Value, RangeValue and Toggle patterns accept actions");
            var textPattern = Pattern(editor, 10014, "32eba289-3583-42c9-9c59-3b6d9a1e9b6a");
            patterns.Add(textPattern);
            nint document = 0;
            Marshal.ThrowExceptionForHR(((delegate* unmanaged<nint, nint*, int>)Method(textPattern, 7))(textPattern, &document));
            patterns.Add(document);
            nint contents = 0;
            Marshal.ThrowExceptionForHR(((delegate* unmanaged<nint, int, nint*, int>)Method(document, 12))(document, -1, &contents));
            try
            {
                check(Marshal.PtrToStringBSTR(contents).Length > 0, "UIA TextPattern exposes a real document range");
            }
            finally { Marshal.FreeBSTR(contents); }
        }
        finally
        {
            foreach (var pointer in patterns.Concat(new[] { editor, slider, toggle, root, automation }))
                if (pointer != 0)
                    Marshal.Release(pointer);
            CoUninitialize();
        }
    }
}
