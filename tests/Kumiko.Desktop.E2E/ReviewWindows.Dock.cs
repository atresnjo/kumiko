using System.Runtime.InteropServices;
using Kumiko.Platform;
using Kumiko.Rendering;
using O = Kumiko.Platform.ObjC;

internal static partial class ReviewWindows
{
    static unsafe void Dock(string output, Action<bool, string> check)
    {
        using var tile = new DockTile();
        using var png = new MemoryStream();
        byte[] pixels = new byte[128 * 128 * 4];
        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = 24;
            pixels[i + 1] = 160;
            pixels[i + 2] = 96;
            pixels[i + 3] = 255;
        }

        Png.Write(png, new Pixels(pixels, 128, 128));
        byte[] encoded = png.ToArray();
        nint image;
        fixed (byte* bytes = encoded)
        {
            var data = O.O(O.Class("NSData"), "dataWithBytes:length:", (nint)bytes, (nuint)encoded.Length);
            image = O.O(O.O(O.Class("NSImage"), "alloc"), "initWithData:", data);
        }

        try
        {
            using var progress = new MacWindowProgress(tile.Handle, image);
            progress.Set(.4);
            var root = tile.Content;
            var children = O.O(root, "subviews");
            check(O.N(children, "count") == 2 && O.O(O.O(children, "objectAtIndex:", (nuint)0), "image") == image,
                "private Dock progress view retains the application image underneath the indicator");
            check(Math.Abs(O.D(O.O(children, "objectAtIndex:", (nuint)1), "doubleValue") - .4) < .001,
                "Dock indicator reflects the requested progress");
            var bounds = O.R(root, "bounds");
            var bitmap = O.O(root, "bitmapImageRepForCachingDisplayInRect:", bounds);
            var send = NativeLibrary.GetExport(NativeLibrary.Load("/usr/lib/libobjc.A.dylib"), "objc_msgSend");
            ((delegate* unmanaged<nint, nint, NativeRect, nint, void>)send)(root, O.Sel("cacheDisplayInRect:toBitmapImageRep:"), bounds, bitmap);
            var data = ((delegate* unmanaged<nint, nint, nuint, nint, nint>)send)(bitmap, O.Sel("representationUsingType:properties:"), 4, O.O(O.Class("NSDictionary"), "dictionary"));
            byte[] bytes = new byte[(int)O.N(data, "length")];
            Marshal.Copy(O.O(data, "bytes"), bytes, 0, bytes.Length);
            File.WriteAllBytes(Path.Combine(output, "review-windows-dock.png"), bytes);
            var capture = Image.Decode(bytes);
            check(capture.Pixels.Bytes.Where((_, i) => i % 4 == 1).Count(v => v > 100) > 8000,
                "offscreen Dock capture contains the retained green icon rather than only a progress bar");
            using var other = new MacWindowProgress(tile.Handle, image);
            other.Set(.8);
            var otherRoot = tile.Content;
            progress.Set(null);
            check(tile.Content == otherRoot, "clearing an older window's progress preserves the current tile owner");
            other.Set(null);
            check(tile.Content == 0 && tile.Displays >= 3, "clearing the active progress restores the default Dock image content");
        }
        finally
        {
            O.Release(image);
        }
    }

    sealed unsafe class DockTile : IDisposable
    {
        static readonly Dictionary<nint, DockTile> instances = new();
        internal readonly nint Handle;
        internal nint Content;
        internal int Displays;
        internal DockTile()
        {
            var cls = O.Define("KumikoReviewDockTile", "NSObject",
                ("contentView", (nint)(delegate* unmanaged<nint, nint, nint>)&GetContent, "@@:"),
                ("setContentView:", (nint)(delegate* unmanaged<nint, nint, nint, void>)&SetContent, "v@:@"),
                ("display", (nint)(delegate* unmanaged<nint, nint, void>)&Display, "v@:"));
            Handle = O.O(cls, "new");
            instances.Add(Handle, this);
        }

        [UnmanagedCallersOnly]
        static nint GetContent(nint self, nint cmd) => instances.TryGetValue(self, out var owner) ? owner.Content : 0;
        [UnmanagedCallersOnly]
        static void SetContent(nint self, nint cmd, nint content)
        {
            if (!instances.TryGetValue(self, out var owner))
                return;
            O.O(content, "retain");
            O.Release(owner.Content);
            owner.Content = content;
        }

        [UnmanagedCallersOnly]
        static void Display(nint self, nint cmd)
        {
            if (instances.TryGetValue(self, out var owner))
                owner.Displays++;
        }

        public void Dispose()
        {
            instances.Remove(Handle);
            O.Release(Content);
            O.Release(Handle);
        }
    }
}
