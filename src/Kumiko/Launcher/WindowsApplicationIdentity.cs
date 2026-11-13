using System.Runtime.InteropServices;

namespace Kumiko.Desktop;

internal static unsafe class WindowsApplicationIdentity
{
    // Read the link without resolving it (which could contact network drives or show UI).
    // Preserve arguments in identity: differently configured launch targets are distinct apps.
    internal static string Read(string path, string fallback)
    {
        var initialized = CoInitializeEx(0, 2);
        nint link = 0, file = 0;
        try
        {
            var cls = new Guid("00021401-0000-0000-C000-000000000046");
            var iid = new Guid("000214F9-0000-0000-C000-000000000046");
            if (CoCreateInstance(ref cls, 0, 1, ref iid, out link) < 0)
                return fallback;
            var persist = new Guid("0000010B-0000-0000-C000-000000000046");
            var table = *(nint**)link;
            if (((delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)table[0])(link, &persist, &file) < 0)
                return fallback;
            fixed (char* name = path)
                if (((delegate* unmanaged[Stdcall]<nint, char*, uint, int>)(*(nint**)file)[5])(file, name, 0) < 0)
                    return fallback;
            var target = new char[32768];
            var arguments = new char[32768];
            fixed (char* destination = target)
            fixed (char* args = arguments)
            {
                if (((delegate* unmanaged[Stdcall]<nint, char*, int, nint, uint, int>)table[3])(link, destination, target.Length, 0, 4) < 0 || destination[0] == 0)
                    return fallback;
                if (((delegate* unmanaged[Stdcall]<nint, char*, int, int>)table[10])(link, args, arguments.Length) < 0)
                    return fallback;
                return "win32:" + new string(destination).ToUpperInvariant() + "|" + new string(args);
            }
        }
        finally
        {
            Release(file);
            Release(link);
            if (initialized >= 0)
                CoUninitialize();
        }
    }

    static void Release(nint value)
    {
        if (value != 0)
            ((delegate* unmanaged[Stdcall]<nint, uint>)(*(nint**)value)[2])(value);
    }
    [DllImport("ole32.dll")]
    static extern int CoInitializeEx(nint reserved, uint model);
    [DllImport("ole32.dll")]
    static extern void CoUninitialize();
    [DllImport("ole32.dll")]
    static extern int CoCreateInstance(ref Guid cls, nint outer, uint context, ref Guid iid, out nint instance);
}
