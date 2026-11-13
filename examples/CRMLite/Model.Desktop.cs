using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace CRMLite;

/// <summary>Why a notice was posted; it decides what a click opens and when the notice goes stale.</summary>
public enum NoticeKind
{
    PlanDone,
    PlanFailed,
    SaveFailed
}

/// <summary>A notice CRM Lite posted and has not withdrawn. Only posted notices route a click.</summary>
public sealed record PostedNotice(string Id, NoticeKind Kind, string? DealId = null);

/// <summary>
/// The desktop shell as the model sees it: what the host allows, whether CRM Lite is frontmost and which notices are
/// still posted. Notices go out only while CRM Lite is in the background; in front, the in-app toasts and status say it.
/// </summary>
public sealed record DesktopState(NoticeAccess Access = NoticeAccess.Unknown, string? Detail = null, bool Active = true)
{
    public static DesktopState Default { get; } = new();

    readonly ImmutableArray<PostedNotice> posted = [];

    // Models saved before a notice existed restore with an empty list.
    public ImmutableArray<PostedNotice> Posted
    {
        get => posted.IsDefault ? [] : posted;
        init => posted = value;
    }
}

public sealed partial record CrmModel
{
    /// <summary>
    /// Null until the desktop shell first reports and then omitted from JSON, so recordings made before desktop
    /// integration replay unchanged; later sessions record and restore the state.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DesktopState? DesktopReport
    {
        get; init;
    }

    [JsonIgnore] public DesktopState Desktop => DesktopReport ?? DesktopState.Default;
}
