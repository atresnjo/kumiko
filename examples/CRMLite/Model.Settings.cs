using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace CRMLite;

public enum SettingsSection
{
    General,
    Appearance,
    Notifications,
    Security,
    Members,
    Billing
}

/// <summary>How deal cards lean while dragged on the board.</summary>
public enum CardTilt
{
    Flat,
    Subtle,
    Playful
}

public enum Accent
{
    Blue,
    Violet,
    Green,
    Orange,
    Rose
}

public enum MemberRole
{
    Owner,
    Admin,
    Member
}

/// <summary>
/// A request to scroll the settings to a section; a new <paramref name="Request"/> scrolls again. Instant jumps
/// (opening the sheet) skip the glide.
/// </summary>
public sealed record SettingsJump(SettingsSection Section, long Request, bool Arrived = false, bool Instant = false);

/// <summary>Workspace and personal preferences edited on the settings page.</summary>
public sealed record Preferences(
    string Workspace = "Harbor",
    string Language = "English (US)",
    string TimeZone = "Pacific Time (US & Canada)",
    string StartPage = "Companies",
    Accent Accent = Accent.Blue,
    bool Mentions = true,
    bool Assigned = true,
    bool StageChanges = true,
    bool WeeklyDigest = false,
    string Delivery = "Hourly digest",
    bool TwoStep = true,
    bool Saml = false,
    bool DeviceApproval = false,
    string SessionLength = "24 hours",
    string Domains = "harbor.app",
    string BillingEmail = "finance@harbor.app",
    ImmutableSortedDictionary<string, MemberRole>? Roles = null,
    CardTilt Tilt = CardTilt.Subtle
)
{
    /// <summary>Stored inverted and omitted while on, so recordings made before this setting replay unchanged.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool DesktopAlertsOff
    {
        get; init;
    }

    /// <summary>Desktop notifications for agent plans and failed saves, on unless turned off.</summary>
    [JsonIgnore] public bool DesktopAlerts => !DesktopAlertsOff;

    public static Preferences Default { get; } = new();

    public MemberRole RoleOf(string member) => Roles?.GetValueOrDefault(member, Fallback(member)) ?? Fallback(member);

    static MemberRole Fallback(string member) => member switch
    {
        "maya" => MemberRole.Owner,
        "sara" => MemberRole.Admin,
        _ => MemberRole.Member
    };
}

/// <summary>The settings sheet's state: open flag, search, the select that is open and the scroll position.</summary>
public sealed record SettingsState(
    bool Open = false,
    string Query = "",
    string? Menu = null,
    SettingsSection Section = SettingsSection.General,
    SettingsJump? Jump = null
);
