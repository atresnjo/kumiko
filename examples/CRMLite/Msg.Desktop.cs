namespace CRMLite;

public abstract partial record CrmMsg
{
    /// <summary>A desktop shell event or the outcome of a desktop shell request.</summary>
    public sealed record Desktop(DesktopMsg Message) : CrmMsg;
}

public abstract partial record DesktopMsg
{
    /// <summary>The desktop shell reported whether it can post notifications.</summary>
    public sealed record AccessChanged(NoticeAccess Access, string? Detail) : DesktopMsg;
    /// <summary>CRM Lite became frontmost, or another application did.</summary>
    public sealed record Activated(bool Active) : DesktopMsg;
    /// <summary>The user clicked the notice <paramref name="Id"/>. Unknown or withdrawn notices are ignored.</summary>
    public sealed record Clicked(string Id) : DesktopMsg;
    /// <summary>Posting <paramref name="Id"/> failed; <paramref name="Access"/> is what the failure says about the host.</summary>
    public sealed record PostFailed(string Id, NoticeAccess? Access, string Error) : DesktopMsg;
    /// <summary>A desktop shell request finished: posting, withdrawing, clearing or bringing CRM Lite forward.</summary>
    public sealed record Done(string Operation, string? Error = null) : DesktopMsg;
}
