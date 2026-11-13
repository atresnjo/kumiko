using Kumiko.UI;

namespace Kumiko.Desktop;

/// <summary>App actions carry app messages; Window actions carry explicit window identity;
/// Focus actions additionally require the bound focus owner.</summary>
public enum DesktopActionScope
{
    App,
    Window,
    Focus
}

/// <summary>A typed UI intent, not a Cmd effect. Recreate from the immutable model each render.</summary>
public sealed record DesktopAction<TMessage>(string Id, string Label, TMessage Message,
    Key Key = Key.None, Modifiers Modifiers = Modifiers.Primary,
    bool Enabled = true, bool Checked = false, DesktopActionScope Scope = DesktopActionScope.Window);
