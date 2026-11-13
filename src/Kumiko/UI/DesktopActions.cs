using Kumiko.Components;
using Kumiko.Desktop;
using Kumiko.Runtime;

namespace Kumiko.UI;

/// <summary>Frame-bound adapters for one typed action set. No service work runs here.</summary>
public sealed class DesktopActions<TMessage>
{
    readonly Context context;
    readonly Dispatch<TMessage> send;
    readonly DesktopAction<TMessage>[] actions;
    readonly long frame, viewVersion;
    readonly ulong focusOwner;
    readonly Func<bool>? modelCurrent;
    bool invoked;

    internal DesktopActions(Context context, Dispatch<TMessage> send, IEnumerable<DesktopAction<TMessage>> actions, ulong focusOwner)
    {
        this.context = context;
        this.send = send;
        this.actions = actions.ToArray();
        this.focusOwner = focusOwner;
        frame = context.FrameId;
        viewVersion = context.ProgramViewVersion;
        modelCurrent = context.DesktopModelCurrent;
        if (this.actions.Any(a => string.IsNullOrWhiteSpace(a.Id) || string.IsNullOrWhiteSpace(a.Label)))
            throw new ArgumentException("Actions need nonempty identities and labels.", nameof(actions));
        if (this.actions.GroupBy(a => a.Id, StringComparer.Ordinal).Any(g => g.Count() > 1))
            throw new ArgumentException("Duplicate desktop action identity.", nameof(actions));
        if (this.actions.Where(a => a.Key != Key.None).GroupBy(a => (a.Key, Normalize(a.Modifiers))).Any(g => g.Count() > 1))
            throw new ArgumentException("Desktop action shortcut collision.", nameof(actions));
    }

    static Modifiers Normalize(Modifiers value) => (value & Modifiers.Primary) == 0 ? value :
        (value & ~Modifiers.Primary) | (OperatingSystem.IsMacOS() ? Modifiers.Meta : Modifiers.Control);

    bool Available(DesktopAction<TMessage> action, bool keyboard = false, bool menuActivation = false) =>
        !invoked && context.FrameId == frame && context.ProgramViewVersion == viewVersion && (modelCurrent?.Invoke() ?? true) && action.Enabled &&
        context.DesktopActionAllowed(action.Scope, focusOwner, keyboard, menuActivation);

    /// <summary>Invokes at most once from this projection. Unknown, stale or disabled identities return false.</summary>
    public bool Invoke(string id)
    {
        return InvokeCore(id, false);
    }

    bool InvokeCore(string id, bool menuActivation)
    {
        context.CheckThread();
        var action = actions.FirstOrDefault(a => a.Id == id);
        if (action is null || !Available(action, menuActivation: menuActivation))
            return false;
        invoked = true;
        context.WithDesktopActionSource(action.Id, action.Label, () => send(action.Message));
        return true;
    }

    string Hint(DesktopAction<TMessage> action) => action.Key == Key.None ? "" : Kit.ShortcutLabel(KeyName(action.Key), action.Modifiers);

    // Digit and punctuation keys read as the character printed on the key, not the enum name.
    static string KeyName(Key key) => key switch
    {
        >= Key.D1 and <= Key.D9 => ((char)('1' + (key - Key.D1))).ToString(),
        Key.D0 => "0",
        Key.Backslash => "\\",
        Key.BracketLeft => "[",
        Key.BracketRight => "]",
        _ => key.ToString()
    };

    /// <summary>Toolbar button with the same enabled and checked projection as menu rows.</summary>
    public void Button(string id, NodeKey key = default)
    {
        var action = actions.First(a => a.Id == id);
        var hit = context.Button((action.Checked ? "✓ " : "") + action.Label, new(Disabled: !Available(action)), key);
        var index = Array.FindIndex(context.Tree, 0, context.Count, node => node.Id == hit.Id);
        context.Tree[index].Name = action.Label;
        context.Tree[index].Checked = action.Checked;
        if (hit.Clicked)
            Invoke(id);
    }

    /// <summary>A row for a Kit popover/context menu. Selection uses the same guarded dispatcher.</summary>
    public void MenuItem(string id, NodeKey key = default)
    {
        var action = actions.First(a => a.Id == id);
        if (context.Kit.MenuItem(action.Label, new(Shortcut: Hint(action), Disabled: !Available(action), Checked: action.Checked), key).Clicked)
            Invoke(id);
    }

    /// <summary>Small context/menu popup using the same action rows and close message.</summary>
    public void Menu(TMessage close, Rect? anchor = null, NodeKey key = default)
    {
        using (context.Kit.Popover(anchor, () => send(close), key: key))
            foreach (var action in actions)
                MenuItem(action.Id, action.Id);
        if (invoked)
            send(close);
    }

    /// <summary>Palette entries carry the same enabled/checked state. Selection must call Invoke.</summary>
    public IReadOnlyList<CommandItem> PaletteItems() => actions
        .Select(a => new CommandItem(a.Id, a.Label, Hint: Hint(a))
        {
            Disabled = !Available(a),
            Checked = a.Checked
        }).ToArray();

    /// <summary>Existing searchable command palette. Only this palette's selection may cross its modal boundary.
    /// The caller closes it through a message and restores the saved opener focus when it disappears.</summary>
    public void Palette(string query, Func<string, TMessage> queryChanged, TMessage close, Rect? anchor = null,
        NodeKey key = default)
    {
        context.Kit.CommandMenu(anchor, query, value => send(queryChanged(value)), PaletteItems(),
            id =>
            {
                if (InvokeCore(id, true))
                    send(close);
            }, () => send(close), key: key);
    }

    /// <summary>Callback-menu adapter. No native key equivalents: keyboard routing runs after editors and overlays.</summary>
    public MenuItem[] MenuItems() => actions.Select(action => new MenuItem(action.Label +
        (Hint(action).Length > 0 ? "   " + Hint(action) : ""), Id: action.Id, Click: _ => Invoke(action.Id))
    {
        Disabled = !Available(action),
        Checked = action.Checked
    }).ToArray();

    /// <summary>Call after editors, modal content and palettes. Exact modifiers match; editor chords and IME win.</summary>
    public bool Shortcuts()
    {
        context.CheckThread();
        foreach (var action in actions)
            if (action.Key != Key.None && Available(action, true) && context.Shortcut(action.Key, action.Modifiers))
                return Invoke(action.Id);
        return false;
    }
}
