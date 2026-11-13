using Kumiko.UI;
using static CRMLite.Palette;

namespace CRMLite;

// Visual styles configure an existing scope. Keys, input and motion stay in the view.
static class CrmStyles
{
    extension(Div div)
    {
        public Div ControlRow() => div.Row()
            .Height(Sizes.Control)
            .ItemsCenter()
            .Gap(6)
            .Px(8)
            .Rounded(Corners.Control);

        public Div PrimaryAction(Palette palette) => div.ControlRow()
            .Px(10)
            .Rounded(Corners.Button)
            .Background(palette.Accent);

        public Div IconAction(float size = Sizes.Control) => div.Width(size)
            .Height(size)
            .Center()
            .Rounded(Corners.Control);

        public Div FieldSurface(Palette palette, bool invalid = false) => div.Row()
            .Height(Sizes.Field)
            .ItemsCenter()
            .Gap(8)
            .Px(12)
            .Rounded(Corners.Control)
            .Background(palette.Page)
            .Border(invalid ? palette.Danger : palette.Kit.Input);

        public Div RaisedSurface(Palette palette) => div.Column()
            .Background(palette.Raised)
            .Border(palette.Border)
            .Rounded(Corners.Dialog);

        public Div OutlinedCard(Palette palette) => div.Rounded(Corners.Card)
            .Border(palette.Border);

        public Div TableHeaderRow() => div.Row()
            .FillWidth()
            .Height(Sizes.TableHead)
            .ItemsCenter()
            .Px(Spacing.Gutter);

        public Div TableBodyRow() => div.Row()
            .FillWidth()
            .Grow()
            .ItemsCenter()
            .Px(Spacing.Gutter);

        public Div AccentOutline(Palette palette) => div.Border(palette.Accent.Alpha(.55f), 2);
    }
}
