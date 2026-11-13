using System.Runtime.InteropServices;
using Kumiko.UI;

namespace Kumiko.Platform;

internal sealed unsafe partial class WindowsWindow
{
    [StructLayout(LayoutKind.Sequential)]
    struct CompositionForm
    {
        public uint Style;
        public int X, Y, Left, Top, Right, Bottom;
    }
    [DllImport("imm32.dll")] static extern nint ImmGetContext(nint window);
    [DllImport("imm32.dll")] static extern int ImmReleaseContext(nint window, nint context);
    [DllImport("imm32.dll")] static extern int ImmGetCompositionStringW(nint context, uint index, void* buffer, uint bytes);
    [DllImport("imm32.dll")] static extern int ImmSetCompositionWindow(nint context, ref CompositionForm form);

    bool Ime(uint message, nint flags)
    {
        if (window.Context?.IsEditing != true)
            return false;
        if (message == 0x286)
            return true;
        if (message is 0x10d or 0x10e)
        {
            window.Input(input => input.Composition = "");
            return true;
        }
        if (message != 0x10f)
            return false;
        var context = ImmGetContext(handle);
        if (context == 0)
            return false;
        try
        {
            bool commit = ((long)flags & 0x800) != 0;
            uint index = commit ? 0x800u : 8u;
            int bytes = ImmGetCompositionStringW(context, index, null, 0);
            if (bytes < 0)
                return true;
            var chars = new char[bytes / 2];
            fixed (char* buffer = chars)
                if (ImmGetCompositionStringW(context, index, buffer, (uint)bytes) < 0)
                    return true;
            var text = new string(chars);
            window.Input(input =>
            {
                input.Composition = text;
                input.CommitComposition = commit;
            });
            return true;
        }
        finally
        {
            ImmReleaseContext(handle, context);
        }
    }

    void PositionIme(Context view)
    {
        if (view.CaretRectangle is not { } caret)
            return;
        var context = ImmGetContext(handle);
        if (context == 0)
            return;
        try
        {
            var form = new CompositionForm { Style = 2, X = (int)(caret.X * view.Scale), Y = (int)(caret.Bottom * view.Scale) };
            ImmSetCompositionWindow(context, ref form);
        }
        finally
        {
            ImmReleaseContext(handle, context);
        }
    }
}
