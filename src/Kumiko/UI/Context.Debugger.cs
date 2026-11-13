namespace Kumiko.UI;

public sealed partial class Context
{
    readonly HashSet<ulong> debuggerNodes = [];
    Rect? debuggerRect;
    int debuggerRoot = -1;
    internal bool DebuggerOwnsFocus => debuggerNodes.Contains(focus);
    internal void ClearDebugger()
    {
        debuggerNodes.Clear();
        debuggerRect = null;
        debuggerRoot = -1;
    }

    internal void SetDebugger(int root, Rect rect)
    {
        debuggerRoot = root;
        debuggerRect = rect;
        debuggerNodes.Clear();
        for (var i = root; i < Count; i++)
            debuggerNodes.Add(Tree[i].Id);
    }

    internal bool DifferentDebuggerLayers(int left, int right) => debuggerRoot >= 0 &&
        (left >= debuggerRoot) != (right >= debuggerRoot);

    internal bool DebuggerBlocksScroll(ulong id) => debuggerRect is { } rect && rect.Contains(Input.Pointer) && !debuggerNodes.Contains(id);

    internal readonly record struct DebuggerInput(Input? Saved, ulong[]? Hover, ulong Focus);
    internal DebuggerInput HideDebuggerInput()
    {
        bool pointer = debuggerRect is { } rect && rect.Contains(Input.Pointer) && (active == 0 || debuggerNodes.Contains(active));
        bool keyboard = DebuggerOwnsFocus && (Input.Key != UI.Key.None || Input.Text.Length > 0 || Input.Composition is not null || Input.Navigation != NavigationDirection.None);
        if (!pointer && !keyboard)
            return default;
        var saved = new DebuggerInput(Input, hotPath.ToArray(), DebuggerOwnsFocus ? focus : 0);
        Input = new()
        {
            Pointer = new(-float.MaxValue, -float.MaxValue)
        };
        hotPath.Clear();
        return saved;
    }

    internal void RestoreDebuggerInput(DebuggerInput saved)
    {
        if (saved.Saved is not { } input)
            return;
        Input = input;
        hotPath.UnionWith(saved.Hover!);
        if (saved.Focus != 0)
            focus = saved.Focus;
    }
}
