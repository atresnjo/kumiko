using Kumiko.UI;

// Named styles are ordinary methods: compose them and pass the active theme per use.
static class GalleryStyles
{
    extension(Div div)
    {
        public Div CardSurface(Theme theme) => div.Column()
            .Padding(theme.Space.S4)
            .Gap(theme.Space.S3)
            .Rounded(theme.Radius.Lg)
            .Background(theme.Surface)
            .Border(theme.Border)
            .Shadow(12, 4);

        public Div Selected(Theme theme, bool selected) =>
            div.Border(selected ? theme.Accent : theme.Border, selected ? 2 : 1);

        public Div Density(Theme theme, bool compact) =>
            div.Padding(compact ? theme.Space.S2 : theme.Space.S4);

        public Div Flat() => div.Shadow(null).Border(null);
    }

    public static TextStyle PlanTitle(Theme theme) => theme.Typography.Title with
    {
        Weight = Weight.Semibold
    };
}
