namespace Flint;

public sealed partial class FlintView
{
    const float ResizeThickness = 6;
    float SidebarMaximum => MathF.Max(220, C.Size.Width - 700);
    float SidebarPaneWidth => Math.Clamp(Model.SidebarSize, 220, SidebarMaximum);
}
