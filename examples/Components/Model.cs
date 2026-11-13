using System.Collections.Immutable;

namespace Components;

/// <summary>Everything the showcase displays. Components receive values and report changes as messages.</summary>
public sealed record ShowcaseModel(
    bool Dark = false,
    string Accent = "neutral",
    float Radius = 6,
    string Section = "overview",
    string Tab = "account",
    string Period = "week",
    string Filter = "active",
    int Clicks = 0,
    string Name = "Ada Lovelace",
    string Email = "",
    bool Terms = false,
    bool Marketing = true,
    bool Airplane = false,
    bool Wifi = true,
    string Plan = "pro",
    float Volume = 40,
    string Layout = "grid",
    string? Expanded = "shipping",
    bool DialogOpen = false,
    string LastAction = "Nothing yet",
    int Page = 1,
    int Rating = 3,
    bool AccountOpen = false,
    bool SearchOpen = false,
    string Query = "",
    DateOnly? Due = null,
    bool DueOpen = false,
    TimeOnly? Start = null,
    bool StartOpen = false,
    double? Budget = 1250.5,
    ImmutableHashSet<string>? Folders = null,
    string? File = "src/app",
    float Pane = 220
)
{
    /// <summary>The showcase pins today's date so its screenshots are repeatable.</summary>
    public static DateOnly Today => new(2026, 10, 20);
}
