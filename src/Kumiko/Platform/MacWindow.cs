using System.Runtime.InteropServices;
using Kumiko.Rendering;
using Kumiko.UI;
using Kumiko.Web;
using O = Kumiko.Platform.ObjC;

namespace Kumiko.Platform;

[StructLayout(LayoutKind.Sequential)]
internal readonly record struct NativeRange(nuint Location, nuint Length);
internal sealed unsafe partial class MacWindow : IWindowBackend
{
    static readonly Dictionary<nint, MacWindow> instances = new();
    readonly Window window;
    readonly nint view, windowDelegate;
    readonly MetalRenderer? renderer;
    readonly MacAccessibility? accessibility;
    bool disposed, observing, resizing;
    int markedLength;
    internal nint Handle
    {
        get;
    }
    public bool IsVisible => !disposed && Presentation.IsVisible(Handle);
    internal IMacWindowPresentation Presentation { get; set; } = MacWindowPresentation.Instance;
    internal static MacWindow? Find(nint handle) => instances.GetValueOrDefault(handle);

    internal MacWindow(Window window, PageOptions? page)
    {
        this.window = window;
        using var pool = new AutoreleasePool();
        var options = window.Options;
        nuint mask = (options.Resizable ? 8u : 0) | (options.Frameless ? 0u : 1 | 2 | 4 | (options.HiddenTitleBar ? 32768u : 0));
        mask |= options.Panel ? 1u << 7 : 0;
        var windowClass = O.Define(options.Panel ? "KumikoLauncherPanel" : "KumikoShellWindow", options.Panel ? "NSPanel" : "NSWindow",
            ("canBecomeKeyWindow", (nint)(delegate* unmanaged<nint, nint, byte>)&Yes, "B@:"),
            ("canBecomeMainWindow", (nint)(delegate* unmanaged<nint, nint, byte>)&MainEligibility, "B@:"));
        Handle = O.O(O.O(windowClass, "alloc"), "initWithContentRect:styleMask:backing:defer:", new NativeRect(0, 0, options.Width, options.Height), mask, (nuint)2, (byte)0);
        instances[Handle] = this;
        O.V(Handle, "setReleasedWhenClosed:", (byte)0);
        SetTitle(options.Title);
        O.V(Handle, "setContentMinSize:", new NativeSize(options.MinWidth, options.MinHeight));
        if (options.HiddenTitleBar)
        {
            O.V(Handle, "setTitlebarAppearsTransparent:", (byte)1);
            O.V(Handle, "setTitleVisibility:", (nuint)1);
        }

        if (options.AlwaysOnTop)
            O.V(Handle, "setLevel:", (nuint)3);
        O.V(Handle, "center");
        if (options.X is { } x && options.Y is { } y)
        {
            var screen = O.O(O.Class("NSScreen"), "mainScreen");
            var bounds = O.R(screen, "frame");
            O.V(Handle, "setFrameTopLeftPoint:", new NativePoint(x, bounds.Size.Height - y));
        }

        var delegateClass = O.Define(
            "NanoCSharpWindowDelegate",
            "NSObject",
            ("windowShouldClose:", (nint)(delegate* unmanaged<nint, nint, nint, byte>)&ShouldClose, "c@:@"),
            ("windowWillClose:", (nint)(delegate* unmanaged<nint, nint, nint, void>)&WillClose, "v@:@"),
            ("windowDidResize:", (nint)(delegate* unmanaged<nint, nint, nint, void>)&DidResize, "v@:@"),
            ("windowDidBecomeKey:", (nint)(delegate* unmanaged<nint, nint, nint, void>)&ShellNotification, "v@:@"),
            ("windowDidMove:", (nint)(delegate* unmanaged<nint, nint, nint, void>)&ShellNotification, "v@:@"),
            ("windowDidMiniaturize:", (nint)(delegate* unmanaged<nint, nint, nint, void>)&ShellNotification, "v@:@"),
            ("windowDidDeminiaturize:", (nint)(delegate* unmanaged<nint, nint, nint, void>)&ShellNotification, "v@:@"),
            ("windowWillEnterFullScreen:", (nint)(delegate* unmanaged<nint, nint, nint, void>)&FullScreenWillChange, "v@:@"),
            ("windowWillExitFullScreen:", (nint)(delegate* unmanaged<nint, nint, nint, void>)&FullScreenWillChange, "v@:@"),
            ("windowDidFailToEnterFullScreen:", (nint)(delegate* unmanaged<nint, nint, nint, void>)&FullScreenFailed, "v@:@"),
            ("windowDidFailToExitFullScreen:", (nint)(delegate* unmanaged<nint, nint, nint, void>)&FullScreenFailed, "v@:@"),
            ("windowDidEnterFullScreen:", (nint)(delegate* unmanaged<nint, nint, nint, void>)&ShellNotification, "v@:@"),
            ("windowDidExitFullScreen:", (nint)(delegate* unmanaged<nint, nint, nint, void>)&ShellNotification, "v@:@"),
            ("windowDidResignKey:", (nint)(delegate* unmanaged<nint, nint, nint, void>)&ResignKey, "v@:@"),
            ("windowDidChangeBackingProperties:", (nint)(delegate* unmanaged<nint, nint, nint, void>)&DidResize, "v@:@"),
            ("accessibilityDisplayOptionsChanged:", (nint)(delegate* unmanaged<nint, nint, nint, void>)&DisplayOptionsChanged, "v@:@")
        );
        windowDelegate = O.O(delegateClass, "new");
        instances[windowDelegate] = this;
        O.V(Handle, "setDelegate:", windowDelegate);
        if (!window.App.Options.Hidden)
        {
            // Reduce Motion and Increase Contrast changes arrive through the workspace notification center.
            var center = O.O(O.O(O.Class("NSWorkspace"), "sharedWorkspace"), "notificationCenter");
            O.V(center, "addObserver:selector:name:object:", windowDelegate, O.Sel("accessibilityDisplayOptionsChanged:"), O.TempString("NSWorkspaceAccessibilityDisplayOptionsDidChangeNotification"), (nint)0);
            observing = true;
        }

        if (page is not null)
        {
            view = O.O(O.O(Handle, "contentView"), "retain");
            Configure(options);
            window.Page = new Page(window, page, Handle);
            window.Page.Ready += window.NativePageReady;
            if (options.Visible && !window.App.Options.Hidden)
                Show();
            return;
        }

        var viewClass = DefineView();
        view = O.O(O.O(viewClass, "alloc"), "initWithFrame:", new NativeRect(0, 0, options.Width, options.Height));
        instances[view] = this;
        accessibility = new(window, view);
        renderer = new();
        O.V(view, "setWantsLayer:", (byte)1);
        O.V(view, "setLayer:", renderer.Layer);
        O.V(Handle, "setContentView:", view);
        O.V(Handle, "makeFirstResponder:", view);
        O.V(Handle, "setAcceptsMouseMovedEvents:", (byte)1);
        var area = O.O(O.O(O.Class("NSTrackingArea"), "alloc"), "initWithRect:options:owner:userInfo:", new NativeRect(0, 0, options.Width, options.Height), (nuint)(1 | 2 | 32 | 512), view, (nint)0);
        O.V(view, "addTrackingArea:", area);
        O.Release(area);
        O.V(view, "registerForDraggedTypes:", O.O(O.Class("NSArray"), "arrayWithObject:", O.TempString("public.file-url")));
        if (!window.App.Options.Hidden && window.Context is { } c)
        {
            c.ReadClipboard = Desktop.Clipboard.ReadText;
            c.WriteClipboard = Desktop.Clipboard.WriteText;
            c.ReadRichClipboard = static () => Desktop.Clipboard.Read();
            c.WriteRichClipboard = static data => Desktop.Clipboard.Write(data);
        }

        Configure(options);
        DidResize();
        if (options.Visible && !window.App.Options.Hidden)
            Show();
    }

    static nint DefineView()
    {
        var cls = O.Define(
            "NanoCSharpView",
            "NSView",
            ("isFlipped", (nint)(delegate* unmanaged<nint, nint, byte>)&Yes, "B@:"),
            ("needsPanelToBecomeKey", (nint)(delegate* unmanaged<nint, nint, byte>)&Yes, "B@:"),
            ("acceptsFirstResponder", (nint)(delegate* unmanaged<nint, nint, byte>)&Yes, "B@:"),
            ("viewDidChangeEffectiveAppearance", (nint)(delegate* unmanaged<nint, nint, void>)&AppearanceChanged, "v@:"),
            ("copy:", (nint)(delegate* unmanaged<nint, nint, nint, void>)&EditAction, "v@:@"),
            ("cut:", (nint)(delegate* unmanaged<nint, nint, nint, void>)&EditAction, "v@:@"),
            ("paste:", (nint)(delegate* unmanaged<nint, nint, nint, void>)&EditAction, "v@:@"),
            ("selectAll:", (nint)(delegate* unmanaged<nint, nint, nint, void>)&EditAction, "v@:@"),
            ("undo:", (nint)(delegate* unmanaged<nint, nint, nint, void>)&EditAction, "v@:@"),
            ("redo:", (nint)(delegate* unmanaged<nint, nint, nint, void>)&EditAction, "v@:@"),
            ("mouseDown:", (nint)(delegate* unmanaged<nint, nint, nint, void>)&MouseDown, "v@:@"),
            ("rightMouseDown:", (nint)(delegate* unmanaged<nint, nint, nint, void>)&MouseDown, "v@:@"),
            ("otherMouseDown:", (nint)(delegate* unmanaged<nint, nint, nint, void>)&MouseDown, "v@:@"),
            ("mouseUp:", (nint)(delegate* unmanaged<nint, nint, nint, void>)&MouseUp, "v@:@"),
            ("rightMouseUp:", (nint)(delegate* unmanaged<nint, nint, nint, void>)&MouseUp, "v@:@"),
            ("otherMouseUp:", (nint)(delegate* unmanaged<nint, nint, nint, void>)&MouseUp, "v@:@"),
            ("mouseMoved:", (nint)(delegate* unmanaged<nint, nint, nint, void>)&MouseMove, "v@:@"),
            ("mouseDragged:", (nint)(delegate* unmanaged<nint, nint, nint, void>)&MouseMove, "v@:@"),
            ("mouseEntered:", (nint)(delegate* unmanaged<nint, nint, nint, void>)&MouseMove, "v@:@"),
            ("mouseExited:", (nint)(delegate* unmanaged<nint, nint, nint, void>)&MouseMove, "v@:@"),
            ("scrollWheel:", (nint)(delegate* unmanaged<nint, nint, nint, void>)&Scroll, "v@:@"),
            ("kumikoDisplayFrame:", (nint)(delegate* unmanaged<nint, nint, nint, void>)&DisplayFrame, "v@:@"),
            ("keyDown:", (nint)(delegate* unmanaged<nint, nint, nint, void>)&KeyDown, "v@:@"),
            ("flagsChanged:", (nint)(delegate* unmanaged<nint, nint, nint, void>)&FlagsChanged, "v@:@"),
            ("insertText:replacementRange:", (nint)(delegate* unmanaged<nint, nint, nint, NativeRange, void>)&InsertText, "v@:@{_NSRange=QQ}"),
            ("setMarkedText:selectedRange:replacementRange:", (nint)(delegate* unmanaged<nint, nint, nint, NativeRange, NativeRange, void>)&SetMarked, "v@:@{_NSRange=QQ}{_NSRange=QQ}"),
            ("unmarkText", (nint)(delegate* unmanaged<nint, nint, void>)&Unmark, "v@:"),
            ("hasMarkedText", (nint)(delegate* unmanaged<nint, nint, byte>)&HasMarked, "B@:"),
            ("markedRange", (nint)(delegate* unmanaged<nint, nint, NativeRange>)&MarkedRange, "{_NSRange=QQ}@:"),
            ("selectedRange", (nint)(delegate* unmanaged<nint, nint, NativeRange>)&SelectedRange, "{_NSRange=QQ}@:"),
            ("validAttributesForMarkedText", (nint)(delegate* unmanaged<nint, nint, nint>)&ValidAttributes, "@@:"),
            ("attributedSubstringForProposedRange:actualRange:", (nint)(delegate* unmanaged<nint, nint, NativeRange, nint, nint>)&Substring, "@@:{_NSRange=QQ}^v"),
            ("characterIndexForPoint:", (nint)(delegate* unmanaged<nint, nint, NativePoint, nuint>)&CharacterIndex, "Q@:{CGPoint=dd}"),
            ("firstRectForCharacterRange:actualRange:", (nint)(delegate* unmanaged<nint, nint, NativeRange, nint, NativeRect>)&FirstRect, "{CGRect={CGPoint=dd}{CGSize=dd}}@:{_NSRange=QQ}^v"),
            ("doCommandBySelector:", (nint)(delegate* unmanaged<nint, nint, nint, void>)&Command, "v@::"),
            ("draggingEntered:", (nint)(delegate* unmanaged<nint, nint, nint, nuint>)&DragUpdated, "Q@:@"),
            ("draggingUpdated:", (nint)(delegate* unmanaged<nint, nint, nint, nuint>)&DragUpdated, "Q@:@"),
            ("draggingExited:", (nint)(delegate* unmanaged<nint, nint, nint, void>)&DragExited, "v@:@"),
            ("performDragOperation:", (nint)(delegate* unmanaged<nint, nint, nint, byte>)&PerformDrag, "B@:@")
        );
        O.class_addProtocol(cls, O.objc_getProtocol("NSTextInputClient"));
        return cls;
    }

    static void Guard(nint self, Action<MacWindow> action)
    {
        if (!instances.TryGetValue(self, out var owner) || owner.disposed)
            return;
        try
        {
            using var operation = owner.NativeOperation(callback: true);
            action(owner);
        }
        catch (Exception ex)
        {
            try
            {
                owner.window.App.Report(ex);
            }
            catch
            {
            }
        }
    }

    [UnmanagedCallersOnly]
    static byte Yes(nint self, nint cmd) => 1;
    [UnmanagedCallersOnly]
    static byte MainEligibility(nint self, nint cmd) => instances.TryGetValue(self, out var owner) && owner.window.Options.Panel ? (byte)0 : (byte)1;
    [UnmanagedCallersOnly]
    static void AppearanceChanged(nint self, nint cmd) => Guard(self, w =>
    {
        w.window.SystemPreferencesChanged();
        w.window.Invalidate();
    });
    [UnmanagedCallersOnly]
    static void DisplayOptionsChanged(nint self, nint cmd, nint notification) => Guard(self, w => w.window.SystemPreferencesChanged());
    [UnmanagedCallersOnly]
    static void EditAction(nint self, nint cmd, nint sender) => Guard(self, w =>
    {
        var key = cmd == O.Sel("copy:") ? Key.C : cmd == O.Sel("cut:") ? Key.X : cmd == O.Sel("paste:") ? Key.V : cmd == O.Sel("selectAll:") ? Key.A : cmd == O.Sel("undo:") ? Key.Z : Key.Y;
        w.window.Input(i =>
        {
            i.Key = key;
            i.Modifiers = Modifiers.Primary;
        });
    });
    [UnmanagedCallersOnly]
    static byte ShouldClose(nint self, nint cmd, nint sender)
    {
        if (!instances.TryGetValue(self, out var owner) || owner.disposed)
            return 0;
        try
        {
            using var operation = owner.NativeOperation(callback: true);
            return owner.window.CanNativeClose() ? (byte)1 : (byte)0;
        }
        catch (Exception error)
        {
            try
            {
                owner.window.App.Report(error);
            }
            catch
            {
            }

            return 0;
        }
    }

    [UnmanagedCallersOnly]
    static void WillClose(nint self, nint cmd, nint notification) => Guard(self, w => w.window.NativeClosed());
    [UnmanagedCallersOnly]
    static void DidResize(nint self, nint cmd, nint notification) => Guard(self, w => w.DidResize());
    void DidResize()
    {
        if (disposed)
            return;
        ApplyTrafficLights();
        if (!IsMaximized && !IsFullScreen && fullScreenTarget is null && Handle != 0)
            normalState = State;
        window.NativeShellChanged();
        if (disposed)
            return;
        var r = O.R(view, "bounds");
        resizing = true;
        try
        {
            window.ResizeFromNative((float)r.Size.Width, (float)r.Size.Height, (float)O.D(Handle, "backingScaleFactor"), synchronous: true);
        }
        finally
        {
            resizing = false;
        }
    }

    static Point Position(MacWindow w, nint e)
    {
        var p = O.P(w.view, "convertPoint:fromView:", O.P(e, "locationInWindow"), 0);
        return new((float)p.X, (float)p.Y);
    }

    [UnmanagedCallersOnly]
    static void MouseDown(nint self, nint cmd, nint e) => Guard(self, w =>
    {
        var p = Position(w, e);
        var button = O.N(e, "buttonNumber");
        if (button >= 3)
        {
            if (button is 3 or 4)
                w.window.Input(i => i.Navigation = button == 3 ? NavigationDirection.Back : NavigationDirection.Forward);
            return;
        }

        if (O.N(e, "buttonNumber") == 0 && w.window.Context?.IsTitleBarBackground(p) == true)
        {
            O.V(w.Handle, "performWindowDragWithEvent:", e);
            return;
        }

        w.window.Input(i =>
        {
            i.Pointer = p;
            i.Down = i.Pressed = true;
            i.Button = button == 1 ? PointerButton.Secondary : button == 2 ? PointerButton.Middle : PointerButton.Primary;
            i.ClickCount = (int)O.N(e, "clickCount");
            i.Modifiers = Mods(e);
        });
    });
    [UnmanagedCallersOnly]
    static void MouseUp(nint self, nint cmd, nint e) => Guard(self, w =>
    {
        var button = O.N(e, "buttonNumber");
        if (button >= 3)
            return;
        var p = Position(w, e);
        w.window.Input(i =>
        {
            i.Pointer = p;
            i.Down = false;
            i.Released = true;
            i.Button = button == 1 ? PointerButton.Secondary : button == 2 ? PointerButton.Middle : PointerButton.Primary;
            i.Modifiers = Mods(e);
            i.ClickCount = (int)O.N(e, "clickCount");
        });
    });
    [UnmanagedCallersOnly]
    static void MouseMove(nint self, nint cmd, nint e) => Guard(self, w =>
    {
        var p = Position(w, e);
        w.window.Input(i => i.Pointer = p, coalesce: true);
    });
    [UnmanagedCallersOnly]
    static void Scroll(nint self, nint cmd, nint e) => Guard(self, w =>
    {
        var p = Position(w, e);
        w.window.Input(i =>
        {
            i.Pointer = p;
            // Trackpads report points and their own momentum. Wheels report lines, which glide; 40 points per
            // line matches Chromium on macOS.
            var precise = O.B(e, "hasPreciseScrollingDeltas") != 0;
            var unit = precise ? 1 : 40;
            // Events coalesced into one frame add up.
            i.ScrollY += -(float)O.D(e, "scrollingDeltaY") * unit;
            i.ScrollX += -(float)O.D(e, "scrollingDeltaX") * unit;
            i.ScrollPrecise = precise;
            i.Modifiers = Mods(e);
        }, coalesce: true);
    });
    [UnmanagedCallersOnly]
    static void KeyDown(nint self, nint cmd, nint e) => Guard(self, w => w.HandleKey(e));
    [UnmanagedCallersOnly]
    static void FlagsChanged(nint self, nint cmd, nint e) => Guard(self, w =>
    {
        var mods = Mods(e);
        w.window.Input(i => i.HeldModifiers = mods);
    });
    /// <summary>Releases arrive in another application after the window loses key status.</summary>
    [UnmanagedCallersOnly]
    static void ResignKey(nint self, nint cmd, nint notification) => Guard(self, w =>
    {
        w.window.Input(i => i.HeldModifiers = Modifiers.None);
        w.window.NativeShellChanged();
    });
    static Modifiers Mods(nint e)
    {
        var flags = O.N(e, "modifierFlags");
        return ((flags & (1u << 17)) != 0 ? Modifiers.Shift : 0) | ((flags & (1u << 18)) != 0 ? Modifiers.Control : 0) | ((flags & (1u << 19)) != 0 ? Modifiers.Alt : 0) | ((flags & (1u << 20)) != 0 ? Modifiers.Meta : 0);
    }

    static string Plain(nint text) => O.B(text, "isKindOfClass:", O.Class("NSString")) != 0 ? O.Text(text) : O.Text(O.O(text, "string"));
    [UnmanagedCallersOnly]
    static void InsertText(nint self, nint cmd, nint text, NativeRange range) => Guard(self, w =>
    {
        var value = Plain(text);
        w.markedLength = 0;
        w.window.Input(i =>
        {
            i.Composition = value;
            i.CommitComposition = true;
        });
    });
    [UnmanagedCallersOnly]
    static void SetMarked(nint self, nint cmd, nint text, NativeRange selected, NativeRange range) => Guard(self, w =>
    {
        var value = Plain(text);
        w.markedLength = value.Length;
        w.window.Input(i => i.Composition = value);
    });
    [UnmanagedCallersOnly]
    static void Unmark(nint self, nint cmd) => Guard(self, w =>
    {
        w.markedLength = 0;
        w.window.Input(i => i.Composition = "");
    });
    [UnmanagedCallersOnly]
    static byte HasMarked(nint self, nint cmd) => (byte)(instances.TryGetValue(self, out var w) && w.markedLength > 0 ? 1 : 0);
    [UnmanagedCallersOnly]
    static NativeRange MarkedRange(nint self, nint cmd) => instances.TryGetValue(self, out var w) && w.markedLength > 0 ? new(0, (nuint)w.markedLength) : new(nuint.MaxValue, 0);
    [UnmanagedCallersOnly]
    static NativeRange SelectedRange(nint self, nint cmd)
    {
        if (instances.TryGetValue(self, out var w) && w.window.Context is { } c && c.Edits.TryGetValue(c.Focus, out var edit))
            return new((nuint)Math.Min(edit.Cursor, edit.Anchor), (nuint)Math.Abs(edit.Cursor - edit.Anchor));
        return new(nuint.MaxValue, 0);
    }

    [UnmanagedCallersOnly]
    static nint ValidAttributes(nint self, nint cmd) => O.O(O.Class("NSArray"), "array");
    [UnmanagedCallersOnly]
    static nint Substring(nint self, nint cmd, NativeRange range, nint actual)
    {
        if (!instances.TryGetValue(self, out var w) || w.window.Context is not { } c || !c.Edits.TryGetValue(c.Focus, out var edit) || edit.Buffer is not { } buffer || range.Location > (nuint)buffer.Length)
            return 0;
        int start = (int)range.Location, length = (int)Math.Min(range.Length, (nuint)(buffer.Length - start));
        if (actual != 0)
            *(NativeRange*)actual = new((nuint)start, (nuint)length);
        return O.O(O.O(O.O(O.Class("NSAttributedString"), "alloc"), "initWithString:", O.TempString(buffer.Text.Substring(start, length))), "autorelease");
    }

    [UnmanagedCallersOnly]
    static nuint CharacterIndex(nint self, nint cmd, NativePoint point) => 0;
    [UnmanagedCallersOnly]
    static NativeRect FirstRect(nint self, nint cmd, NativeRange range, nint actual)
    {
        if (actual != 0)
            *(NativeRange*)actual = range;
        if (!instances.TryGetValue(self, out var w))
            return default;
        try
        {
            var caret = w.window.Context?.CaretRectangle ?? new Rect(16, 16, 1, 20);
            var local = O.R(w.view, "convertRect:toView:", new NativeRect(caret.X, caret.Y, caret.Width, caret.Height), 0);
            return O.R(w.Handle, "convertRectToScreen:", local);
        }
        catch (Exception ex)
        {
            w.window.App.Report(ex);
            return default;
        }
    }

    [UnmanagedCallersOnly]
    static void Command(nint self, nint cmd, nint selector)
    {
    }

    public void Present(Context context)
    {
        if (disposed)
            return;
        accessibility?.Update(context);
        if (IsVisible)
            renderer?.Present(context.Instances, context.TextSystem.Atlas, context.Size, context.Scale, context.Theme.Background, resizing);
        if (window.App.Options.Hidden)
            return;
        var cursor = O.O(O.Class("NSCursor"), context.Cursor switch
        {
            Cursor.Hand => "pointingHandCursor",
            Cursor.Text => "IBeamCursor",
            Cursor.ResizeHorizontal => "resizeLeftRightCursor",
            Cursor.ResizeVertical => "resizeUpDownCursor",
            _ => "arrowCursor"
        });
        O.V(cursor, "set");
    }

    public Pixels Capture(Context c) => renderer!.Render(c.Instances, c.TextSystem.Atlas, c.Size, c.Scale, c.Theme.Background);
}
