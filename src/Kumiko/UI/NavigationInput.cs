namespace Kumiko.UI;

/// <summary>Platform-normalized screen history input, separate from pointer activation and debugger history.</summary>
public enum NavigationDirection
{
    None,
    Back,
    Forward
}

public sealed partial class Context
{
    sealed record NavigationStack(ulong Owner, ulong Layer, string Name, Action? Back);
    readonly List<NavigationStack> navigationStacks = [];

    internal void RegisterNavigationStack(ulong owner, string name, Action? back)
    {
        if (!Tree[stack[^1]].LayoutHidden)
            navigationStacks.Add(new(owner, OverlayLayer(stack[^1]), name, back));
    }

    NavigationStack? ActiveNavigationStack()
    {
        // Stacks register after their children. Prefer the deepest focused stack;
        // without focus, only an unambiguous chain may claim window-level Back.
        var candidates = navigationStacks.Where(candidate => candidate.Layer == modalRoot).ToArray();
        var selected = candidates.FirstOrDefault(candidate => FocusWithin(candidate.Owner));
        if (selected is null)
        {
            selected = candidates.FirstOrDefault();
            if (selected is null || candidates.Any(candidate => !Within(selected.Owner, candidate.Owner)))
                return null;
        }
        return candidates.FirstOrDefault(candidate => candidate.Back is not null && Within(selected.Owner, candidate.Owner));
    }

    /// <summary>Consumes native Forward intent, Command+] on macOS or Alt+Right elsewhere, outside active IME composition.</summary>
    public bool ForwardShortcut()
    {
        if (NavigationCompositionActive)
            return false;
        if (Input.Navigation == NavigationDirection.Forward)
        {
            Input.Navigation = NavigationDirection.None;
            return true;
        }
        return OperatingSystem.IsMacOS() ? Shortcut(UI.Key.BracketRight) : Shortcut(UI.Key.Right, Modifiers.Alt);
    }

    internal bool NavigationCompositionActive => Input.Composition is not null || Input.CommitComposition ||
        Edits.TryGetValue(focus, out var edit) && edit.Composition.Length > 0;

    internal NavigationDirection TakeNavigation()
    {
        var native = Input.Navigation;
        Input.Navigation = NavigationDirection.None;
        if (DebuggerOwnsFocus || NavigationCompositionActive)
            return NavigationDirection.None;
        try
        {
            var direction = native != NavigationDirection.None ? native : BackShortcut() ? NavigationDirection.Back :
                ForwardShortcut() ? NavigationDirection.Forward : NavigationDirection.None;
            if (direction == NavigationDirection.Back && ActiveNavigationStack() is { } target)
            {
                WithNavigationSource(target.Name, direction, target.Back!);
                return NavigationDirection.None;
            }
            return modalRoot == 0 ? direction : NavigationDirection.None;
        }
        finally
        {
            navigationStacks.Clear();
        }
    }

    internal void WithNavigationSource(string stack, NavigationDirection direction, Action action)
    {
        var previous = accessibilitySource;
        accessibilitySource = new(Role.Button, direction.ToString(), stack, "/navigation/" + stack);
        try
        {
            action();
        }
        finally
        {
            accessibilitySource = previous;
        }
    }
}

public sealed partial class Tester
{
    /// <summary>Injects the same history intent as native mouse buttons, without system input.</summary>
    public void Navigate(NavigationDirection direction)
    {
        Context.Input.Navigation = direction;
        Frame();
    }
}
