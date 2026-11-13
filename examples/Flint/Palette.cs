using Kumiko.Icons;
using Kumiko.UI;

namespace Flint;

/// <summary>
/// The original's light, Fork-inspired palette (src/ui/theme.ts). Text tones that fail WCAG contrast in the original
/// (textFaint, white on bright status colours) use the nearest accessible tone instead.
/// </summary>
static class Palette
{
    public static Color Canvas => Color.White;
    public static Color Sidebar => Color.Hex(0xf5f5f7);
    public static Color Toolbar => Color.Hex(0xf7f7f9);
    public static Color Pane => Color.Hex(0xfafafc);
    public static Color Border => Color.Hex(0xe3e3e8);
    public static Color BorderStrong => Color.Hex(0xd2d2d8);
    public static Color Text => Color.Hex(0x1d1d1f);
    public static Color Muted => Color.Hex(0x6e6e73);
    /// <summary>textMuted on tinted surfaces (pill, tab bar, selection) where #6e6e73 drops below 4.5:1.</summary>
    public static Color MutedStrong => Color.Hex(0x5c5c61);
    /// <summary>The original's textFaint (#a1a1a6) for icons and rules; accessible text uses Muted.</summary>
    public static Color Faint => Color.Hex(0xa1a1a6);
    public static Color Hover => Color.Hex(0xebebf0);
    public static Color Pressed => Color.Hex(0xe1e1e8);
    public static Color Accent => Color.Hex(0x2f63e0);
    public static Color AccentHover => Color.Hex(0x2756c9);
    public static Color SelectedSoft => Color.Hex(0xe3e3ea);
    public static Color SelectedMuted => Color.Hex(0xeef2ff);
    public static Color TabBar => Color.Hex(0xececf0);
    public static Color Pill => Color.Hex(0xededf1);
    public static Color YellowBadge => Color.Hex(0xfff4c2);
    public static Color YellowBorder => Color.Hex(0xe8cf6a);
    public static Color TagBadge => Color.Hex(0xe6f0ff);
    public static Color TagBorder => Color.Hex(0xb3cdf7);
    public static Color Added => Color.Hex(0xe9f9ee);
    public static Color AddedWord => Color.Hex(0xb4ecc4);
    public static Color Removed => Color.Hex(0xffeceb);
    public static Color RemovedWord => Color.Hex(0xffc8c4);
    public static Color Green => Color.Hex(0x1f7a35);
    public static Color Red => Color.Hex(0xd70015);
    public static Color Danger => Color.Hex(0xd70015);
    public static Color Overlay => Color.Black.Alpha(.2f);
    /// <summary>Ten Fork-like lane colours, as in the original.</summary>
    public static Color Lane(int color) => Color.Hex((color % 10) switch
    {
        0 => 0xf5a623u,
        1 => 0xe5484du,
        2 => 0x30a46cu,
        3 => 0x3e63ddu,
        4 => 0x8e4ec6u,
        5 => 0x12a594u,
        6 => 0xd6409fu,
        7 => 0x978365u,
        8 => 0x0090ffu,
        _ => 0xf76b15u
    });
    /// <summary>Status square colours, darkened from the original so their white glyphs stay legible.</summary>
    public static Color Status(char status) => Color.Hex(status switch
    {
        'A' or '?' => 0x1f7a35u,
        'M' or 'T' => 0xb25000u,
        'D' => 0xd70015u,
        'R' or 'C' => 0x3e63ddu,
        'U' => 0xc2307fu,
        _ => 0x6e6e73u
    });
    public static string StatusGlyph(char status) => status switch
    {
        '?' or 'A' => "+",
        'M' => "•",
        'D' => "−",
        'R' => "→",
        'U' => "!",
        _ => status.ToString()
    };
    public const float SidebarWidth = 290, TitleBar = 40, RowHeight = 22, LaneWidth = 12, MaxLanes = 14;
    public const float AuthorWidth = 170, ShaWidth = 74, DateWidth = 158;
    public static Theme Theme => Theme.Light with
    {
        Background = Canvas,
        Surface = Canvas,
        SurfaceHover = Hover,
        Text = Text,
        Muted = Muted,
        Accent = Accent,
        Border = Border,
        InputBorder = BorderStrong,
        Selection = Accent,
        SelectionText = Color.White,
        Typography = Theme.Light.Typography with
        {
            Body = new(13, Color: Text),
            Caption = new(11, Color: Muted)
        }
    };
}

/// <summary>Hugeicons stand-ins for the original's Lucide-style icon set (src/ui/icons.tsx).</summary>
public static class Icons
{
    public static readonly string Sidebar = Hugeicons.SidebarLeft;
    public static readonly string Folder = Hugeicons.Folder01;
    public static readonly string FolderOpen = Hugeicons.FolderOpen;
    public static readonly string Fetch = Hugeicons.Download04;
    public static readonly string Pull = Hugeicons.ArrowDown02;
    public static readonly string Push = Hugeicons.ArrowUp02;
    public static readonly string Stash = Hugeicons.Archive02;
    public static readonly string Branch = Hugeicons.GitBranch;
    public static readonly string Launch = Hugeicons.Search01;
    public static readonly string Search = Hugeicons.Search01;
    public static readonly string Focus = Hugeicons.FocusPoint;
    public static readonly string Collapse = Hugeicons.UnfoldLess;
    public static readonly string ChevronRight = Hugeicons.ArrowRight01;
    public static readonly string ChevronDown = Hugeicons.ArrowDown01;
    public static readonly string Check = Hugeicons.Tick02;
    public static readonly string Close = Hugeicons.Cancel01;
    public static readonly string Plus = Hugeicons.Add01;
    public static readonly string Changes = Hugeicons.FileEdit;
    public static readonly string Commits = Hugeicons.GitCommit;
    public static readonly string Cloud = Hugeicons.Cloud;
    public static readonly string Tag = Hugeicons.Tag01;
    public static readonly string Worktree = Hugeicons.GitFork;
    public static readonly string File = Hugeicons.File01;
    public static readonly string Layers = Hugeicons.DashboardSquare01;
    public static readonly string Pencil = Hugeicons.PencilEdit01;
    public static readonly string Trash = Hugeicons.Delete02;
    public static readonly string User = Hugeicons.User;
    public static readonly string Refresh = Hugeicons.Refresh;
    public static readonly string Terminal = Hugeicons.ComputerTerminal01;
    public static readonly string Finder = Hugeicons.FolderSearch;
    public static readonly string Copy = Hugeicons.Copy01;
}
