using Kumiko.UI;

namespace Kumiko.Components;

/// <summary>Command content state.</summary>
/// <param name="Checked">The model checked value.</param>
/// <param name="Disabled">The part is disabled.</param>
/// <param name="Foreground">The text color used by Kit's default renderer in this style and state.</param>
/// <param name="Hovered">The pointer is over this enabled part.</param>
/// <param name="Highlighted">The active pointer/keyboard target, or the item Enter chooses from search.</param>
/// <param name="Focused">The part has keyboard focus.</param>
public readonly record struct CommandItemState(bool Checked, bool Hovered, bool Highlighted, bool Focused, bool Disabled,
    Color Foreground);

/// <summary>Tab content state.</summary>
/// <param name="Selected">The model selected value.</param>
/// <param name="Disabled">The part is disabled.</param>
/// <param name="Foreground">The text color used by Kit's default renderer in this style and state.</param>
/// <param name="Hovered">The pointer is over this enabled part.</param>
/// <param name="Highlighted">The part is hovered or keyboard-current/focused.</param>
/// <param name="Focused">The part has keyboard focus.</param>
public readonly record struct TabItemState(bool Selected, bool Hovered, bool Highlighted, bool Focused, bool Disabled,
    Color Foreground);

/// <summary>Tree content state.</summary>
/// <param name="Selected">The model selected value.</param>
/// <param name="Expanded">The branch is expanded; false for leaves.</param>
/// <param name="Depth">Zero-based nesting depth.</param>
/// <param name="HasChildren">The part has child nodes.</param>
/// <param name="Disabled">The part is disabled.</param>
/// <param name="Foreground">The text color used by Kit's default renderer in this style and state.</param>
/// <param name="Hovered">The pointer is over this enabled part.</param>
/// <param name="Highlighted">The part is hovered or keyboard-current/focused.</param>
/// <param name="Focused">The part has keyboard focus.</param>
public readonly record struct TreeItemState(bool Selected, bool Hovered, bool Highlighted, bool Focused, bool Disabled,
    Color Foreground, bool Expanded, int Depth, bool HasChildren);
