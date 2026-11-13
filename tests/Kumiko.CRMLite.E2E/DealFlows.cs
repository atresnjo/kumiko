using CRMLite;
using Kumiko.UI;

/// <summary>The Deals board: forecast metrics, owner filter and drag between stages.</summary>
static class DealFlows
{
    public static void Run(CrmSession s, string output, Action<bool, string> check)
    {
        s.Click(Role.Button, "Deals");
        check(s.Tester.HasText("$3.39M") && s.Tester.HasText("$134k"), "the board sums the open pipeline and won deals");
        check(s.Tester.HasText("In 3 days"), "deals closing this week are called out");
        check(Cards(s).Length == 23, "every deal has a card, including lanes scrolled out of view");

        var card = s.Node(Role.Button, "Juniper Retail, Partner billing");
        var lane = s.Node(Role.Group, "Qualified");
        var before = Crm.Weighted(s.Model.Deals);
        s.Drag(card, lane.Rect);
        check(s.Model.Deals.Single(d => d.Id == "d-juniper").Stage == Stage.Qualified, "dragging a card to a lane moves its deal");
        check(Crm.Weighted(s.Model.Deals) > before && s.Tester.HasText(Format.Short(Crm.Weighted(s.Model.Deals), s.Tester.Context.Culture)),
            "the weighted forecast follows the move");
        s.Click(Role.Button, "Juniper Retail, Partner billing");
        check(s.Model.Overlay is Overlay.DealDrawer { DealId: "d-juniper" }, "clicking a card opens its deal drawer");
        s.Click(Role.Button, "Open Juniper Retail");
        check(ScreenRoutes.Kind(s.Model.Screen) == ScreenKind.Company && s.Model.CompanyId == "juniper", "the deal drawer opens its company");
        check(Crm.StageOf(s.Model, "juniper") == Stage.Qualified, "the company's stage follows the deal");
        s.Click(Role.Button, "Deals");

        s.Click(Role.Button, "Owner filter");
        check(s.Exists(Role.MenuItem, "All owners") && s.Tester.HasText("You"), "the owner menu marks the signed-in owner");
        s.Click(Role.MenuItem, "Daniel Okafor");
        var cards = Cards(s);
        check(cards.Length == s.Model.Deals.Count(d => d.OwnerId == "daniel") && s.Model.BoardOwner == "daniel", "the owner filter narrows the board");
        s.Click(Role.Button, "Owner filter");
        s.Click(Role.MenuItem, "All owners");
        check(s.Model.BoardOwner is null, "All owners clears the filter");
        Animation(s, output, check);
    }

    /// <summary>
    /// The framework lift with the chosen tilt, the opening slot, the glide into place, cancelling with Escape and
    /// reordering a lane.
    /// </summary>
    static void Animation(CrmSession s, string output, Action<bool, string> check)
    {
        static (float X, float Y) Center(Rect r) => (r.X + r.Width / 2, r.Y + r.Height / 2);
        bool Shows(string key) => s.Tester.Dump().Contains($"key=\"{key}\"", StringComparison.Ordinal);
        bool Turned() => s.Tester.Context.Instances.Any(i => i.Turn.X != 0);
        s.Tester.Context.ReduceMotion = false;

        var (x, y) = Center(s.Node(Role.Button, "Vantage Energy, Metering pilot").Rect);
        var demo = s.Node(Role.Group, "Demo").Rect;
        s.Tester.Press(x, y);
        s.Tester.MoveTo(x + 2, y + 1);
        check(s.Tester.Context.Dragging is null, "a press alone does not lift the card");
        s.Tester.MoveTo(x + 24, y + 12);
        s.Tester.MoveTo(demo.X + demo.Width / 2, demo.Y + 90);
        s.Tester.Advance(.3f);
        s.Tester.Settle();
        check(s.Tester.Context.Dragging is { Kind: "deal", Value: "d-vantage" } && Turned(), "the card lifts tilted and follows the pointer");
        check(Shows("drop-slot") && !s.Exists(Role.Button, "Vantage Energy, Metering pilot"),
            "the lane under the pointer opens a slot while the card leaves its own lane");
        s.Tester.Screenshot(Path.Combine(output, "deals-dragging.png"));
        s.Tester.Release(demo.X + demo.Width / 2, demo.Y + 90);
        s.Tester.Frame(1 / 60f);
        check(s.Model.Deals.Single(d => d.Id == "d-vantage").Stage == Stage.Demo && s.Tester.Context.Dragging is null,
            "releasing drops the deal into the lane");
        check(Turned(), "the dropped card glides into its slot instead of jumping");
        s.Tester.Advance(1);
        s.Tester.Settle();
        check(!Turned() && s.Exists(Role.Button, "Vantage Energy, Metering pilot"), "the glide ends with the card upright in place");

        (x, y) = Center(s.Node(Role.Button, "Summit Outdoor, Loyalty credits").Rect);
        s.Tester.Press(x, y);
        s.Tester.MoveTo(x + 20, y + 10);
        s.Tester.MoveTo(demo.X + demo.Width / 2, demo.Y + 90);
        s.Key(Key.Escape);
        s.Tester.Release(demo.X + demo.Width / 2, demo.Y + 90);
        s.Tester.Advance(1);
        s.Tester.Settle();
        check(s.Model.Deals.Single(d => d.Id == "d-summit").Stage == Stage.Lead && s.Tester.Context.Dragging is null, "Escape returns a lifted card");

        string[] Lead() => [.. s.Model.Deals.Where(d => d.Stage == Stage.Lead).Select(d => d.Id)];
        var before = Lead();
        var first = s.Node(Role.Button, "Summit Outdoor, Loyalty credits").Rect;
        (x, y) = Center(first);
        var pitch = s.Node(Role.Button, "Mosaic Design, Agency seats").Rect.Y - first.Y;
        s.Tester.Press(x, y);
        s.Tester.MoveTo(x, y + 10);
        s.Tester.MoveTo(x, y + pitch * 2);
        s.Tester.Release(x, y + pitch * 2);
        s.Tester.Advance(1);
        s.Tester.Settle();
        check(before[0] == "d-summit" && Lead() is ["d-mosaic", "d-tidewater", "d-summit"], $"dragging within a lane reorders it ({string.Join(", ", Lead())})");

        s.Tester.Context.ReduceMotion = true;
        Tilt(s, check);
    }

    /// <summary>The Card tilt setting picks the framework drag style.</summary>
    static void Tilt(CrmSession s, Action<bool, string> check)
    {
        float Angle()
        {
            var (x, y) = (s.Node(Role.Button, "Mosaic Design, Agency seats").Rect.X + 40, s.Node(Role.Button, "Mosaic Design, Agency seats").Rect.Y + 30);
            s.Tester.Press(x, y);
            s.Tester.MoveTo(x + 30, y + 20);
            s.Tester.Frame();
            var turn = s.Tester.Context.Instances.Select(i => i.Turn.X).FirstOrDefault(t => t != 0);
            s.Key(Key.Escape);
            s.Tester.Release(x + 30, y + 20);
            s.Tester.Settle();
            return turn * 180 / MathF.PI;
        }
        check(MathF.Abs(Angle() - DragStyle.Tilted.Angle) < .1f, "cards tilt subtly by default");
        s.Tester.Context.ReduceMotion = true;
        s.Click(Role.Button, "Account");
        s.Click(Role.Button, "Appearance");
        s.Click(Role.Option, "Playful");
        check(s.Model.Preferences.Tilt == CardTilt.Playful, "the Card tilt setting is stored in preferences");
        s.Key(Key.Escape);
        check(MathF.Abs(Angle() - DragStyle.Playful.Angle) < .1f, "the playful tilt leans the other way and further");
        s.Click(Role.Button, "Account");
        s.Click(Role.Button, "Appearance");
        s.Click(Role.Option, "Flat");
        s.Key(Key.Escape);
        check(Angle() == 0, "the flat tilt lifts cards without turning them");
    }

    /// <summary>Deal cards are buttons named "Company, Deal".</summary>
    static AccessibleNode[] Cards(CrmSession s) =>
        [.. s.Tester.Context.Accessibility.Where(n => n.Role == Role.Button && n.Name?.Contains(", ", StringComparison.Ordinal) == true)];
}
