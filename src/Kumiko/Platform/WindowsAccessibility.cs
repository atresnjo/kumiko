using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Kumiko.UI;

namespace Kumiko.Platform;
// Queries read an immutable snapshot; actions dispatch to the window's owning UI
// thread. Source-generated COM vtables work with NativeAOT without runtime COM.
internal sealed class WindowsAccessibility(Window window, nint handle) : IDisposable
{
    internal sealed record Snapshot(AccessibleNode[] Nodes, float Scale, int X, int Y)
    {
        internal IReadOnlyDictionary<ulong, UiaTextGeometry> Text { get; init; } = new Dictionary<ulong, UiaTextGeometry>();
    }
    internal volatile Snapshot Current = new([], 1, 0, 0);
    internal volatile bool Disposed;
    readonly ConcurrentDictionary<ulong, UiaElement> providers = new();
    internal UiaElement Element(ulong id) => providers.GetOrAdd(id, key => new(this, key));
    internal Window Window => window;
    internal nint Handle => handle;

    public unsafe void Update(Context context)
    {
        var origin = new UiaNative.ScreenPoint();
        UiaNative.ClientToScreen(handle, ref origin);
        var previous = Current;
        Current = new(context.Accessibility.Where(n => !n.Hidden).ToArray(), context.Scale, origin.X, origin.Y)
        {
            Text = UiaTextGeometry.Capture(context)
        };
        if (UiaNative.UiaClientsAreListening() != 0)
        {
            bool structure = !previous.Nodes.Select(n => n.Id).SequenceEqual(Current.Nodes.Select(n => n.Id));
            var focused = Current.Nodes.FirstOrDefault(n => n.Focused);
            bool focusChanged = focused is not null && !previous.Nodes.Any(n => n.Id == focused.Id && n.Focused);
            if (structure || focusChanged)
            {
                var pointer = (nint)ComInterfaceMarshaller<IUiaSimple>.ConvertToUnmanaged(Element(focusChanged ? focused!.Id : 0));
                try
                {
                    if (structure)
                        UiaNative.UiaRaiseStructureChangedEvent(pointer, 2, 0, 0);
                    if (focusChanged)
                        UiaNative.UiaRaiseAutomationEvent(pointer, 20005);
                }
                finally
                {
                    Marshal.Release(pointer);
                }
            }
        }

        var live = Current.Nodes.Select(n => n.Id).ToHashSet();
        foreach (var id in providers.Keys)
            if (id != 0 && !live.Contains(id))
                providers.TryRemove(id, out _);
    }

    public unsafe nint GetObject(nuint w, nint l)
    {
        var pointer = (nint)ComInterfaceMarshaller<IUiaSimple>.ConvertToUnmanaged(Element(0));
        try
        {
            return UiaNative.UiaReturnRawElementProvider(handle, w, l, pointer);
        }
        finally
        {
            Marshal.Release(pointer);
        }
    }

    public void Dispose()
    {
        Disposed = true;
        Current = new([], 1, 0, 0);
        providers.Clear();
    }
}

[GeneratedComClass]
internal sealed partial class UiaElement(WindowsAccessibility owner, ulong id) : IUiaSimple, IUiaFragment, IUiaRoot, IUiaInvoke
{
    const int Unavailable = unchecked((int)0x80040201);
    AccessibleNode? Node => owner.Current.Nodes.FirstOrDefault(n => n.Id == id);

    bool Alive => !owner.Disposed && (id == 0 || Node is not null);

    static unsafe nint Simple(UiaElement value) => (nint)ComInterfaceMarshaller<IUiaSimple>.ConvertToUnmanaged(value);
    static unsafe nint Fragment(UiaElement value) => (nint)ComInterfaceMarshaller<IUiaFragment>.ConvertToUnmanaged(value);
    public int GetOptions(out int options)
    {
        options = 2;
        return 0;
    }

    public unsafe int GetPattern(int pattern, out nint provider)
    {
        provider = pattern switch
        {
            10000 when Node?.Supports(AccessibilityAction.Invoke) == true => (nint)ComInterfaceMarshaller<IUiaInvoke>.ConvertToUnmanaged(this),
            10002 when Node?.Role == Role.TextField => (nint)ComInterfaceMarshaller<IUiaValue>.ConvertToUnmanaged(this),
            10003 when Node?.Supports(AccessibilityAction.SetRange) == true => (nint)ComInterfaceMarshaller<IUiaRange>.ConvertToUnmanaged(this),
            10004 when Node?.Supports(AccessibilityAction.Scroll) == true => (nint)ComInterfaceMarshaller<IUiaScroll>.ConvertToUnmanaged(this),
            10017 when Node?.Supports(AccessibilityAction.Reveal) == true => (nint)ComInterfaceMarshaller<IUiaScrollItem>.ConvertToUnmanaged(this),
            10005 when Node?.Properties?.Expanded is not null => (nint)ComInterfaceMarshaller<IUiaExpand>.ConvertToUnmanaged(this),
            10010 when Node?.Supports(AccessibilityAction.Select) == true => (nint)ComInterfaceMarshaller<IUiaSelectionItem>.ConvertToUnmanaged(this),
            10014 when Node?.Role == Role.TextField && Node.Properties?.Password != true => (nint)ComInterfaceMarshaller<IUiaText>.ConvertToUnmanaged(this),
            10015 when Node?.Role is Role.Checkbox or Role.Switch => (nint)ComInterfaceMarshaller<IUiaToggle>.ConvertToUnmanaged(this),
            _ => 0
        };
        return Alive ? 0 : Unavailable;
    }

    public int GetProperty(int property, out UiaValue value)
    {
        value = default;
        if (!Alive)
            return Unavailable;
        var node = Node;
        value = property switch
        {
            30003 => UiaValue.Number(node?.Role switch
            {
                Role.Button => 50000,
                Role.Checkbox or Role.Switch => 50002,
                Role.TextField => 50004,
                Role.Image => 50006,
                Role.ListItem => 50007,
                Role.List => 50008,
                Role.ComboBox => 50003,
                Role.Grid => 50028,
                Role.Row => 50029,
                Role.Cell => 50029,
                Role.ColumnHeader => 50035,
                Role.Menu => 50009,
                Role.MenuItem => 50011,
                Role.Option => 50007,
                Role.Slider => 50015,
                Role.Text or Role.Heading => 50020,
                Role.Tree => 50023,
                Role.TreeItem => 50024,
                Role.Tooltip => 50022,
                Role.Separator => 50038,
                _ => id == 0 ? 50032 : 50026
            }),
            30005 => UiaValue.Text(node?.Name ?? owner.Window.Options.Title),
            30008 => UiaValue.Bool(node?.Focused == true),
            30009 => UiaValue.Bool(node?.Role is Role.Button or Role.Checkbox or Role.Switch or Role.Slider or Role.TextField or Role.Cell or Role.Option or Role.ColumnHeader or Role.ComboBox or Role.MenuItem or Role.TreeItem or Role.Separator),
            30010 => UiaValue.Bool(node?.Enabled != false),
            30011 => UiaValue.Text(id.ToString("x16")),
            30016 or 30017 => UiaValue.Bool(true),
            30022 => UiaValue.Bool(node?.Offscreen == true),
            30019 => UiaValue.Bool(node?.Properties?.Password == true),
            30013 => UiaValue.Text(node?.Properties?.Help ?? ""),
            30024 => UiaValue.Text("Kumiko"),
            30028 => UiaValue.Bool(node?.Properties?.Expanded is not null),
            30031 => UiaValue.Bool(node?.Supports(AccessibilityAction.Invoke) == true),
            30033 => UiaValue.Bool(node?.Supports(AccessibilityAction.SetRange) == true),
            30034 => UiaValue.Bool(node?.Supports(AccessibilityAction.Scroll) == true),
            30035 => UiaValue.Bool(node?.Supports(AccessibilityAction.Reveal) == true),
            30036 => UiaValue.Bool(node?.Supports(AccessibilityAction.Select) == true),
            30040 => UiaValue.Bool(node?.Role == Role.TextField && node.Properties?.Password != true),
            30041 => UiaValue.Bool(node?.Role is Role.Checkbox or Role.Switch),
            30043 => UiaValue.Bool(node?.Role == Role.TextField),
            30154 when node?.Properties?.Level > 0 => UiaValue.Number(node.Properties.Level),
            _ => default
        };
        return 0;
    }

    public int GetHost(out nint provider)
    {
        provider = 0;
        return id == 0 && Alive ? UiaNative.UiaHostProviderFromHwnd(owner.Handle, out provider) : 0;
    }

    public int Navigate(int direction, out nint provider)
    {
        provider = 0;
        if (!Alive)
            return Unavailable;
        var nodes = owner.Current.Nodes;
        ulong? target = null;
        var node = Node;
        if (direction == 0 && id != 0)
            target = node?.Parent ?? 0;
        if (direction is 3 or 4)
        {
            var children = nodes.Where(n => n.Parent == id).ToArray();
            if (children.Length > 0)
                target = direction == 3 ? children[0].Id : children[^1].Id;
        }

        if (direction is 1 or 2 && node is not null)
        {
            var siblings = nodes.Where(n => n.Parent == node.Parent).ToArray();
            int index = Array.FindIndex(siblings, n => n.Id == id) + (direction == 1 ? 1 : -1);
            if (index >= 0 && index < siblings.Length)
                target = siblings[index].Id;
        }

        if (target is { } found)
            provider = Fragment(owner.Element(found));
        return 0;
    }

    public int RuntimeId(out nint array)
    {
        array = 0;
        if (!Alive)
            return Unavailable;
        array = UiaNative.SafeArrayCreateVector(3, 0, 3);
        if (array == 0)
            return unchecked((int)0x8007000e);
        var values = new[]
        {
            3,
            (int)(id & 0xffffffff),
            (int)(id >> 32)
        };
        for (int i = 0; i < values.Length; i++)
        {
            int index = i, value = values[i];
            UiaNative.SafeArrayPutElement(array, ref index, ref value);
        }

        return Alive ? 0 : Unavailable;
    }

    public int Bounds(out UiaRect rect)
    {
        var snapshot = owner.Current;
        var r = Node?.Rect ?? snapshot.Nodes.FirstOrDefault()?.Rect ?? default;
        rect = new()
        {
            Left = snapshot.X + r.X * snapshot.Scale,
            Top = snapshot.Y + r.Y * snapshot.Scale,
            Width = r.Width * snapshot.Scale,
            Height = r.Height * snapshot.Scale
        };
        return Alive ? 0 : Unavailable;
    }

    public int EmbeddedRoots(out nint array)
    {
        array = 0;
        return Alive ? 0 : Unavailable;
    }

    public int SetFocus() => Action(new(AccessibilityAction.Focus));

    public unsafe int FragmentRoot(out nint provider)
    {
        provider = 0;
        if (!Alive)
            return Unavailable;
        provider = (nint)ComInterfaceMarshaller<IUiaRoot>.ConvertToUnmanaged(owner.Element(0));
        return Alive ? 0 : Unavailable;
    }

    public int FromPoint(double x, double y, out nint provider)
    {
        var snapshot = owner.Current;
        var point = new Point((float)(x - snapshot.X) / snapshot.Scale, (float)(y - snapshot.Y) / snapshot.Scale);
        var found = snapshot.Nodes.LastOrDefault(n => !n.Offscreen && n.Rect.Contains(point));
        provider = found is null ? 0 : Fragment(owner.Element(found.Id));
        return Alive ? 0 : Unavailable;
    }

    public int GetFocus(out nint provider)
    {
        var node = owner.Current.Nodes.FirstOrDefault(n => n.Focused);
        provider = node is null ? 0 : Fragment(owner.Element(node.Id));
        return Alive ? 0 : Unavailable;
    }

    public int Invoke() => Action(new(AccessibilityAction.Invoke));
}
