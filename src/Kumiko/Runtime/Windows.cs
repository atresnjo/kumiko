using Kumiko.UI;

namespace Kumiko.Runtime;

/// <summary>A pure projection of the model into windows with stable, serializable identities.</summary>
public interface IKumikoWindows<TModel, TMessage, TWindow> where TModel : notnull where TMessage : notnull where TWindow : notnull
{
    static abstract IEnumerable<WindowSpec<TWindow, TMessage>> Windows(TModel model);
    static abstract NavigationRoot<TMessage> WindowRoot(TModel model, TWindow window);
}

/// <summary>Desired native window. Placement is applied when opened; title follows the model.</summary>
public sealed record WindowSpec<TWindow, TMessage>(TWindow Id, string Title, TMessage OnClose) where TWindow : notnull where TMessage : notnull
{
    public float Width { get; init; } = 800;
    public float Height { get; init; } = 600;
    public float MinWidth { get; init; } = 200;
    public float MinHeight { get; init; } = 150;
    public bool Resizable { get; init; } = true;
    public bool HiddenTitleBar
    {
        get; init;
    }
    public bool AlwaysOnTop
    {
        get; init;
    }
    public string? RememberAs
    {
        get; init;
    }
    public Theme? Theme
    {
        get; init;
    }
}

/// <summary>Inspection metadata; Key is the identity's canonical generated JSON.</summary>
public sealed record ProgramWindow(string Key, string Title, float Width, float Height);

/// <summary>A render-local root that explicitly selects a view for one window identity.</summary>
public abstract class WindowScreen<TModel, TMessage, TWindow>(TModel model, TWindow window)
    : Screen<TModel, TMessage>(model) where TModel : notnull where TMessage : notnull where TWindow : notnull
{
    protected TWindow Window { get; } = window;
}

public partial interface IProgram
{
    internal bool UsesWindows
    {
        get;
    }
    internal IReadOnlyList<ProgramWindow> WindowList();
    internal string WindowKey(object identity);
    internal void ViewWindow(Context context, string key);
    internal void ForgetContext(Context context);
    internal void AttachWindows(App app, WindowOptions defaults);
}
