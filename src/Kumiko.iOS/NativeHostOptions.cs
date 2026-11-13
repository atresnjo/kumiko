using UIKit;

namespace Kumiko.iOS;

/// <summary>How a <see cref="NativeViewController"/> presents its program.</summary>
public sealed record NativeHostOptions
{
    /// <summary>Fills the controller view behind the content. Defaults to the system background.</summary>
    public UIColor? Background
    {
        get; init;
    }

    /// <summary>Overrides light/dark traits for this host. Unspecified follows the system.</summary>
    public UIUserInterfaceStyle InterfaceStyle
    {
        get; init;
    }

    /// <summary>
    /// When true the content fills the whole controller and reads <c>Context.SafeAreaInsets</c> itself; otherwise
    /// the host keeps content inside the safe area.
    /// </summary>
    public bool EdgeToEdge
    {
        get; init;
    }

    /// <summary>
    /// A file that holds this host's model. It is read before the first frame and written each time the
    /// application enters the background. Null disables restoration for this controller.
    /// </summary>
    public string? StatePath
    {
        get; init;
    }

    /// <summary>For <see cref="NativeApplication"/>: give each scene session its own <see cref="StatePath"/>.</summary>
    public bool RestoreState
    {
        get; init;
    }
}
