using System.Runtime.InteropServices;
using Kumiko.UI;

namespace Kumiko.Platform;

/// <summary>Native ATK objects exported by GTK's AT-SPI bridge.</summary>
internal sealed unsafe partial class LinuxAccessibility : IDisposable
{
    static readonly Dictionary<nint, (LinuxAccessibility Owner, ulong Id)> callbacks = new();
    static nuint widgetType, nodeType, textType, editType, rangeType;
    static int objectClassSize;
    readonly Window window;
    readonly nint view, root;
    readonly Dictionary<ulong, nint> elements = new();
    Dictionary<ulong, AccessibleNode> nodes = new();
    bool disposed;

    public static nint CreateView()
    {
        if (widgetType == 0)
        {
            Atk.g_type_query(Atk.g_object_get_type(), out var gobject);
            objectClassSize = (int)gobject.ClassSize;
            nodeType = Register(Atk.atk_object_get_type(), "KumikoAccessible", (nint)(delegate* unmanaged<nint, nint, void>)&ClassInit);
            AddInterface(nodeType, Atk.atk_component_get_type(), (nint)(delegate* unmanaged<nint, nint, void>)&ComponentInit);
            AddInterface(nodeType, Atk.atk_action_get_type(), (nint)(delegate* unmanaged<nint, nint, void>)&ActionInit);
            textType = Register(nodeType, "KumikoAccessibleText", 0);
            AddInterface(textType, Atk.atk_text_get_type(), (nint)(delegate* unmanaged<nint, nint, void>)&TextInit);
            editType = Register(textType, "KumikoAccessibleEditor", 0);
            AddInterface(editType, Atk.atk_editable_text_get_type(), (nint)(delegate* unmanaged<nint, nint, void>)&EditInit);
            rangeType = Register(nodeType, "KumikoAccessibleRange", 0);
            AddInterface(rangeType, Atk.atk_value_get_type(), (nint)(delegate* unmanaged<nint, nint, void>)&ValueInit);
            var rootType = Register(Atk.gtk_widget_accessible_get_type(), "KumikoAccessibleRoot", (nint)(delegate* unmanaged<nint, nint, void>)&ClassInit);
            widgetType = Register(Atk.gtk_drawing_area_get_type(), "KumikoDrawingArea", (nint)(delegate* unmanaged<nint, nint, void>)&WidgetInit, rootType);
        }
        return Atk.g_object_new(widgetType, 0);
    }

    static nuint accessibleRootType;
    static nuint Register(nuint parent, string name, nint init, nuint rootType = 0)
    {
        Atk.g_type_query(parent, out var size);
        if (rootType != 0)
            accessibleRootType = rootType;
        return Atk.g_type_register_static_simple(parent, name, size.ClassSize, init, size.InstanceSize, 0, 0);
    }
    static void AddInterface(nuint type, nuint iface, nint init)
    {
        var info = new Atk.InterfaceInfo { Init = init };
        Atk.g_type_add_interface_static(type, iface, ref info);
    }
    [UnmanagedCallersOnly]
    static void WidgetInit(nint klass, nint data) => Atk.gtk_widget_class_set_accessible_type(klass, accessibleRootType);
    [UnmanagedCallersOnly]
    static void ClassInit(nint klass, nint data)
    {
        var slots = (nint*)((byte*)klass + objectClassSize);
        slots[3] = (nint)(delegate* unmanaged<nint, int>)&ChildCount;
        slots[4] = (nint)(delegate* unmanaged<nint, int, nint>)&Child;
        slots[5] = (nint)(delegate* unmanaged<nint, int>)&Index;
        slots[10] = (nint)(delegate* unmanaged<nint, nint>)&States;
    }

    public LinuxAccessibility(Window window, nint view)
    {
        this.window = window;
        this.view = view;
        root = Atk.gtk_widget_get_accessible(view);
        callbacks[root] = (this, 0);
        Atk.atk_object_set_name(root, window.Options.Title);
    }

    public void Update(Context context)
    {
        if (disposed)
            return;
        var previous = nodes;
        nodes = context.Accessibility.ToDictionary(n => n.Id);
        foreach (var id in elements.Keys.Where(id => !nodes.ContainsKey(id)).ToArray())
        {
            var pointer = elements[id];
            if (previous.TryGetValue(id, out var oldNode))
            {
                var siblings = previous.Values.Where(n => n.Parent == oldNode.Parent && !n.Hidden).ToArray();
                int index = Array.FindIndex(siblings, n => n.Id == id);
                if (index >= 0)
                    Atk.ChildChanged(elements.GetValueOrDefault(oldNode.Parent, root), "children-changed::remove", (uint)index, pointer, 0);
            }
            Atk.atk_object_notify_state_change(pointer, Atk.atk_state_type_for_name("defunct"), 1);
            callbacks.Remove(pointer);
            Atk.g_object_unref(pointer);
            elements.Remove(id);
        }
        foreach (var node in nodes.Values)
        {
            if (!elements.TryGetValue(node.Id, out var pointer))
            {
                var type = node.Role switch
                {
                    Role.TextField => editType,
                    Role.Text or Role.Heading => textType,
                    Role.Slider or Role.Separator or Role.ScrollArea => rangeType,
                    _ when node.Supports(AccessibilityAction.SetRange) => rangeType,
                    _ => nodeType
                };
                elements[node.Id] = pointer = Atk.g_object_new(type, 0);
                callbacks[pointer] = (this, node.Id);
            }
            Atk.atk_object_set_name(pointer, node.Name);
            Atk.atk_object_set_description(pointer, node.Properties?.Help ?? "");
            Atk.atk_object_set_role(pointer, Atk.atk_role_for_name(node.Role switch
            {
                Role.Dialog => "dialog",
                Role.Form => "form",
                Role.ScrollArea => "scroll pane",
                Role.Button => "push button",
                Role.Checkbox or Role.Switch => "check box",
                Role.TextField => node.Properties?.Password == true ? "password text" : "entry",
                Role.Slider => "slider",
                Role.Image => "image",
                Role.List => "list",
                Role.Option or Role.ListItem => "list item",
                Role.Grid => "table",
                Role.Row => "table row",
                Role.Cell => "table cell",
                Role.ColumnHeader => "table column header",
                Role.ComboBox => "combo box",
                Role.Menu => "popup menu",
                Role.MenuItem => "menu item",
                Role.Alert => "alert",
                Role.Text => "text",
                Role.Heading => "heading",
                Role.Tree => "tree",
                Role.TreeItem => "tree item",
                Role.Tooltip => "tool tip",
                Role.Separator => "split pane",
                _ => "panel"
            }));
        }
        foreach (var node in nodes.Values)
        {
            var pointer = elements[node.Id];
            Atk.atk_object_set_parent(pointer, elements.GetValueOrDefault(node.Parent, root));
            previous.TryGetValue(node.Id, out var old);
            if (old?.Focused != node.Focused)
                Atk.atk_object_notify_state_change(pointer, Atk.atk_state_type_for_name("focused"), node.Focused ? 1 : 0);
            if (old?.Checked != node.Checked)
                Atk.atk_object_notify_state_change(pointer, Atk.atk_state_type_for_name("checked"), node.Checked ? 1 : 0);
            if (old?.Properties?.Selected != node.Properties?.Selected)
                Atk.atk_object_notify_state_change(pointer, Atk.atk_state_type_for_name("selected"), node.Properties?.Selected == true ? 1 : 0);
            if (old?.Properties?.Expanded != node.Properties?.Expanded && node.Properties?.Expanded is { } expanded)
                Atk.atk_object_notify_state_change(pointer, Atk.atk_state_type_for_name("expanded"), expanded ? 1 : 0);
            if (old is not null && old.Value != node.Value && node.Properties?.Password != true && node.Role == Role.TextField)
            {
                Atk.TextChanged(pointer, "text-changed::delete", 0, (old.Value ?? "").EnumerateRunes().Count(), 0);
                Atk.TextChanged(pointer, "text-changed::insert", 0, (node.Value ?? "").EnumerateRunes().Count(), 0);
            }
            if (old is not null && node.Role == Role.TextField && (old.Properties?.SelectionStart != node.Properties?.SelectionStart || old.Properties?.SelectionEnd != node.Properties?.SelectionEnd))
                Atk.Changed(pointer, "text-selection-changed", 0);
            if (old is null)
                Atk.ChildChanged(elements.GetValueOrDefault(node.Parent, root), "children-changed::add", (uint)Children(node.Parent).FindIndex(n => n.Id == node.Id), pointer, 0);
        }
    }

    List<AccessibleNode> Children(ulong parent) => nodes.Values.Where(n => n.Parent == parent && !n.Hidden).ToList();
    static AccessibleNode? Node(nint self) => callbacks.TryGetValue(self, out var entry) ? entry.Owner.nodes.GetValueOrDefault(entry.Id) : null;
    static bool Act(nint self, AccessibilityRequest request)
    {
        if (!callbacks.TryGetValue(self, out var entry) || entry.Owner.disposed || Node(self) is not { Enabled: true, Hidden: false } node ||
            !node.Supports(request.Action) || node.Offscreen && request.Action != AccessibilityAction.Reveal)
            return false;
        if (request.Action == AccessibilityAction.SetRange && (!double.IsFinite(request.Number) || request.Number < node.Properties?.Minimum || request.Number > node.Properties?.Maximum))
            return false;
        if (request.Action == AccessibilityAction.SetValue && node.Properties?.ReadOnly == true)
            return false;
        entry.Owner.window.App.Dispatch(() =>
        {
            if (!entry.Owner.disposed && entry.Owner.window.Context?.PerformAccessibilityAction(entry.Id, request) == true)
                entry.Owner.window.Invalidate();
        });
        return true;
    }
    [UnmanagedCallersOnly]
    static int ChildCount(nint self) => callbacks.TryGetValue(self, out var entry) ? entry.Owner.Children(entry.Id).Count : 0;
    [UnmanagedCallersOnly]
    static nint Child(nint self, int index)
    {
        if (!callbacks.TryGetValue(self, out var entry))
            return 0;
        var children = entry.Owner.Children(entry.Id);
        return index >= 0 && index < children.Count ? Atk.g_object_ref(entry.Owner.elements[children[index].Id]) : 0;
    }
    [UnmanagedCallersOnly]
    static int Index(nint self)
    {
        var node = Node(self);
        return node is not null && callbacks.TryGetValue(self, out var entry) ? entry.Owner.Children(node.Parent).FindIndex(n => n.Id == node.Id) : -1;
    }
    [UnmanagedCallersOnly]
    static nint States(nint self)
    {
        var set = Atk.atk_state_set_new();
        void Add(string name) => Atk.atk_state_set_add_state(set, Atk.atk_state_type_for_name(name));
        if (!callbacks.ContainsKey(self))
        {
            Add("defunct");
            return set;
        }
        if (Node(self) is not { } node)
            return set;
        if (node.Enabled)
        {
            Add("enabled");
            Add("sensitive");
        }
        if (!node.Offscreen)
        {
            Add("visible");
            Add("showing");
        }
        if (node.Focused)
            Add("focused");
        if (node.Checked)
            Add("checked");
        if (node.Properties?.Selected == true)
            Add("selected");
        if (node.Properties?.Expanded is { } expanded)
        {
            Add("expandable");
            if (expanded)
                Add("expanded");
        }
        if (node.Role is Role.Button or Role.TextField or Role.Cell or Role.ComboBox or Role.MenuItem or Role.Option or Role.Slider or Role.Checkbox or Role.Switch or Role.TreeItem or Role.Separator)
            Add("focusable");
        if (node.Role == Role.TextField && node.Properties?.ReadOnly != true)
            Add("editable");
        if (node.Role is Role.Cell or Role.Option or Role.Row or Role.TreeItem)
            Add("selectable");
        return set;
    }

    public void Dispose()
    {
        disposed = true;
        callbacks.Remove(root);
        foreach (var pointer in elements.Values)
        {
            callbacks.Remove(pointer);
            Atk.g_object_unref(pointer);
        }
        elements.Clear();
        nodes.Clear();
    }
}
