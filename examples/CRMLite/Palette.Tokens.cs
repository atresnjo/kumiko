namespace CRMLite;

// Exact app scale values preserve the reference design; use shared tokens when they fit.
public sealed partial record Palette
{
    public static class Spacing
    {
        public const float Tag = 7;
        public const float Cell = 10;
        public const float Gutter = 24;
        public const float Panel = 18;
    }

    public static class Corners
    {
        public const float Tag = 5;
        public const float Control = 6;
        public const float Button = 7;
        public const float Card = 8;
        public const float Lane = 10;
        public const float Dialog = 12;
        public const float Plan = 18;
        public const float Step = 10;
        public const float Palette = 14;
        public const float Sheet = 14;
    }

    public static class Sizes
    {
        public const float Workspace = 1440;
        public const float Rail = 52;
        public const float Header = 48;
        public const float Toolbar = 44;
        public const float ToolbarInlineMin = 1040;
        public const float Row = 34;
        public const float TableHead = 32;
        public const float NavItem = 31;
        public const float Control = 28;
        public const float Field = 32;
        public const float Panel = 300;
        public const float Column = 580;
        public const float Lane = 262;
        public const float Dialog = 520;
        public const float Plan = 600;
        public const float Settings = 1040;
        public const float SettingsHeight = 780;
        public const float SettingsNav = 232;
        public const float SettingsColumn = 700;
        public const float Palette = 660;
        public const float TableMin = 1100;
    }

    public static class TextSize
    {
        public const float Tiny = 11;
        public const float Caption = 12;
        public const float Small = 12.5f;
        public const float Body = 13;
        public const float Card = 13.5f;
        public const float Title = 14;
        public const float Nav = 14;
        public const float Dialog = 16;
        public const float Search = 16;
        public const float Setting = 14.5f;
        public const float Step = 14.5f;
        public const float Sheet = 17;
        public const float Name = 20;
        public const float Metric = 22;
    }
}
