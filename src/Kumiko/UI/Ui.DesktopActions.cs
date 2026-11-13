using Kumiko.Desktop;

namespace Kumiko.UI;

public sealed partial class Ui<TMessage>
{
    /// <summary>Binds one window's current action projection. Call once per root render.
    /// Retained projections expire on rebuild, model transition, removal or disposal.</summary>
    public DesktopActions<TMessage> DesktopActions(IEnumerable<DesktopAction<TMessage>> actions, ulong focusOwner = 0)
    {
        Check();
        if (context.DesktopMenuProjection is not null)
            throw new InvalidOperationException("Bind one desktop action set per root render.");
        var binding = new DesktopActions<TMessage>(context, send, actions, focusOwner);
        context.DesktopMenuProjection = binding.MenuItems;
        context.DesktopMenuItems = binding.MenuItems();
        return binding;
    }
}

public sealed partial class Context
{
    internal Func<bool>? DesktopModelCurrent;
    internal Desktop.MenuItem[]? DesktopMenuItems;
    internal Func<Desktop.MenuItem[]>? DesktopMenuProjection;

    internal void WithDesktopActionSource(string id, string label, Action action)
    {
        var previous = accessibilitySource;
        accessibilitySource = new(Kumiko.UI.Role.MenuItem, label, id, "/desktop-actions/" + id);
        try
        {
            action();
        }
        finally
        {
            accessibilitySource = previous;
        }
    }

    internal bool DesktopActionAllowed(DesktopActionScope scope, ulong owner, bool keyboard, bool menuActivation = false)
    {
        CheckThread();
        if (disposed || DebuggerOwnsFocus || NavigationCompositionActive || modalRoot != 0 &&
            (keyboard || !menuActivation && !stack.Any(i => Tree[i].Id == modalRoot)))
            return false;
        if (scope == DesktopActionScope.Focus && (owner == 0 || focus != owner))
            return false;
        // Editor-reserved chords remain editing input, even if an action declares them.
        return !keyboard || !IsEditing || Input.Key is not (UI.Key.A or UI.Key.C or UI.Key.V or UI.Key.X or UI.Key.Z or UI.Key.Y
            or UI.Key.Left or UI.Key.Right or UI.Key.Up or UI.Key.Down or UI.Key.Home or UI.Key.End
            or UI.Key.Backspace or UI.Key.Delete or UI.Key.Enter or UI.Key.Tab or UI.Key.Space);
    }
}
