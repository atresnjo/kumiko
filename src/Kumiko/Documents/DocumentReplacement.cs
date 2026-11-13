using System.Runtime.InteropServices;
using Kumiko.Platform;
using O = Kumiko.Platform.ObjC;

namespace Kumiko.Documents;

internal static unsafe class DocumentReplacement
{
    static readonly Lazy<nint> Send = new(() => NativeLibrary.GetExport(NativeLibrary.Load("/usr/lib/libobjc.A.dylib"), "objc_msgSend"));
    internal static void Commit(string temporary, string path, bool exists)
    {
        if (!exists)
        {
            File.Move(temporary, path, overwrite: false);
            return;
        }
        if (OperatingSystem.IsWindows())
        {
            File.Replace(temporary, path, null);
            return;
        }
        if (!OperatingSystem.IsMacOS())
        {
            File.Move(temporary, path, overwrite: true);
            return;
        }
        using var pool = new AutoreleasePool();
        nint old = O.O(O.Class("NSURL"), "fileURLWithPath:", O.TempString(path));
        nint fresh = O.O(O.Class("NSURL"), "fileURLWithPath:", O.TempString(temporary));
        nint manager = O.O(O.Class("NSFileManager"), "defaultManager"), error = 0;
        byte success = ((delegate* unmanaged<nint, nint, nint, nint, nint, nuint, nint, nint*, byte>)Send.Value)(manager,
            O.Sel("replaceItemAtURL:withItemAtURL:backupItemName:options:resultingItemURL:error:"), old, fresh, 0, 0, 0, &error);
        if (success == 0)
            throw new IOException(error == 0 ? "Could not replace document." : O.Text(O.O(error, "localizedDescription")));
    }
}
