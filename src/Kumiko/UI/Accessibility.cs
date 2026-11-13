namespace Kumiko.UI;

public enum AccessibilityAction
{
    Invoke,
    Focus,
    SetValue,
    SetRange,
    Toggle,
    SetSelection,
    Select,
    Expand,
    Collapse,
    Scroll,
    Reveal
}

public readonly record struct AccessibilityRequest(
    AccessibilityAction Action,
    string? Text = null,
    double Number = 0,
    int Start = 0,
    int End = 0
);

/// <summary>Immutable properties used by native accessibility providers.</summary>
public sealed record AccessibleProperties(
    bool ReadOnly = false,
    bool Password = false,
    bool Selected = false,
    bool? Expanded = null,
    double? Minimum = null,
    double? Maximum = null,
    double? Number = null,
    double Step = 1,
    int SelectionStart = 0,
    int SelectionEnd = 0,
    string? Error = null,
    int Row = -1,
    int Column = -1,
    int RowCount = 0,
    int ColumnCount = 0,
    float TextOffsetX = 0,
    float TextOffsetY = 0,
    float TextWidth = 0,
    bool Multiline = false
)
{
    internal Kumiko.RichText.RichValue? RichValue
    {
        get; init;
    }
    /// <summary>Whether a scroll area scrolls horizontally.</summary>
    public bool Horizontal
    {
        get; init;
    }
    /// <summary>One-based depth of a tree item; zero when not applicable.</summary>
    public int Level
    {
        get; init;
    }
    /// <summary>Supplementary help, such as a tooltip, announced after the name.</summary>
    public string? Description
    {
        get; init;
    }
    /// <summary>Help text for native providers: a validation error takes precedence over the description.</summary>
    internal string? Help => Error ?? Description;
}

public sealed partial class Context
{
    readonly Dictionary<ulong, AccessibilityRequest> accessibilityRequests = new();

    internal bool QueueAccessibility(ulong id, AccessibilityRequest request)
    {
        if (request.Action != AccessibilityAction.SetRange || !double.IsFinite(request.Number))
            return false;
        accessibilityRequests[id] = request;
        return true;
    }

    internal bool TakeAccessibility(ulong id, out AccessibilityRequest request) => accessibilityRequests.Remove(id, out request);

    /// <summary>Runs a native accessibility action on the owning UI thread.</summary>
    public bool PerformAccessibilityAction(ulong id, AccessibilityRequest request)
    {
        return PerformAction(id, request).Succeeded;
    }
}
