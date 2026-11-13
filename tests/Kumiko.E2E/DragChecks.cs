using Kumiko.Rendering;
using Kumiko.UI;

/// <summary>Lifted drags: tilt, sway, shadow, placeholder, settling into place; and rotated paint.</summary>
static class DragChecks
{
    static readonly Color[] Colors = [Color.Hex(0xff0000), Color.Hex(0x00ff00), Color.Hex(0x0000ff)];
    const float Degrees = MathF.PI / 180;

    sealed class Board
    {
        public List<string> Left = ["a", "b"], Right = ["c"];
        public DragStyle Style = DragStyle.Tilted;
        public int Clicks;
    }

    static Tester Open(Board board)
    {
        void Column(Context c, string key, List<string> items, List<string> other)
        {
            using var column = c.Div(key: key).Width(220).FillHeight().Padding(10).Gap(10).Background(Color.Hex(0xeeeeee));
            if (column.DropTarget("card").Dropped is { Item: { } item })
            {
                other.Remove(item.Value);
                items.Remove(item.Value);
                items.Add(item.Value);
            }
            foreach (var id in items)
            {
                using var card = c.Div(key: id).FillWidth().Height(80).Rounded(10).Background(Colors[id[0] - 'a'])
                    .Semantics(Role.Button, "Card " + id).DragSource("card", id, board.Style);
                if (card.Control().Clicked)
                    board.Clicks++;
            }
        }
        var t = new Tester(c =>
        {
            using var root = c.Div(key: "root").Row().SizeFull().Padding(20).Gap(40);
            Column(c, "left", board.Left, board.Right);
            Column(c, "right", board.Right, board.Left);
        }, new(Width: 600, Height: 400));
        t.Settle();
        return t;
    }

    static Rect Card(Tester t, string id) => t.Context.Accessibility.Single(n => n.Name == "Card " + id).Rect;

    /// <summary>Instances painted in the card's color, with their turn.</summary>
    static Instance[] Painted(Tester t, string id)
    {
        var color = Colors[id[0] - 'a'];
        return [.. t.Context.Instances.Where(i => i.Kind == 0 && i.Color.W > .01f && MathF.Abs(i.Color.X - color.R) < .01f &&
            MathF.Abs(i.Color.Y - color.G) < .01f && MathF.Abs(i.Color.Z - color.B) < .01f)];
    }

    public static void Run(string output, Action<bool, string> check)
    {
        LiftAndDrop(output, check);
        Cancel(check);
        Styles(check);
        Rotation(check);
    }

    static void LiftAndDrop(string output, Action<bool, string> check)
    {
        var board = new Board();
        using var t = Open(board);
        var a = Card(t, "a");
        var (x, y) = (a.X + 40, a.Y + 30);
        t.Press(x, y);
        t.MoveTo(x + 3, y + 1);
        check(t.Context.Dragging is null && t.Context.Instances.All(i => i.Turn.X == 0), "a press that barely moves lifts nothing");
        t.MoveTo(x + 30, y + 20);
        t.MoveTo(x + 32, y + 22);
        check(t.Context.Dragging is { Value: "a" } && t.Context.DraggedRect is { } held && MathF.Abs(held.X - (a.X + 32)) < .5f &&
            MathF.Abs(held.Width - a.Width) < .5f, "the dragged rect follows the pointer from where the card was grabbed");
        t.Advance(.4f);
        t.Frame();
        t.Frame();
        var lifted = Painted(t, "a").Where(i => i.Turn.X != 0).ToArray();
        check(lifted.Length == 1 && MathF.Abs(lifted[0].Turn.X - DragStyle.Tilted.Angle * Degrees) < .01f,
            "the lifted card tilts by the style's angle once it settles under the pointer");
        check(lifted.Length == 1 && lifted[0].Rect.Z > a.Width + .5f, "the lifted card grows by the style's scale");
        check(t.Context.Instances.Any(i => i.Kind == 2 && i.Turn.X != 0), "the lifted card casts a tilted shadow");
        check(t.Context.Instances.Any(i => i.Kind == 0 && MathF.Abs(i.Rect.X - a.X) < .5f && MathF.Abs(i.Rect.Y - a.Y) < .5f &&
            i.Color.W is > 0 and < .2f), "a ghost placeholder keeps the card's place");
        check(!Painted(t, "a").Any(i => i.Turn.X == 0 && MathF.Abs(i.Rect.X - a.X) < .5f), "the card itself leaves its place while held");
        t.Screenshot(Path.Combine(output, "drag-lifted.png"));

        var rest = Painted(t, "a").Single(i => i.Turn.X != 0).Turn.X;
        t.Frame(1 / 60f);
        t.Context.Input.Pointer = new(x + 80, y + 22);
        t.Frame(1 / 60f);
        var leaning = Painted(t, "a").Single(i => i.Turn.X != 0).Turn.X;
        check(leaning > rest + .01f && leaning <= DragStyle.Tilted.MaxAngle * Degrees + .001f,
            "moving sideways sways the card into the motion, within the style's limit");

        var c = Card(t, "c");
        var (dx, dy) = (c.X + 100, c.Bottom + 120);
        t.MoveTo(dx, dy);
        t.Release(dx, dy);
        check(board.Right is ["c", "a"] && t.Context.Dragging is null, "dropping on a target delivers the payload");
        t.Frame(1 / 60f);
        var landing = Card(t, "a");
        var gliding = Painted(t, "a").FirstOrDefault(i => i.Turn.X != 0);
        check(gliding.Turn.X != 0 && MathF.Abs(gliding.Rect.X - landing.X) > 1, "the dropped card glides from the pointer into its new place");
        t.Advance(.6f);
        t.Settle();
        check(Painted(t, "a") is [{ Turn.X: 0 } placed] && MathF.Abs(placed.Rect.X - landing.X) < .5f && MathF.Abs(placed.Rect.Y - landing.Y) < .5f,
            "the glide ends upright in the card's new place");
    }

    static void Cancel(Action<bool, string> check)
    {
        var board = new Board();
        using var t = Open(board);
        var b = Card(t, "b");
        t.Press(b.X + 20, b.Y + 20);
        t.MoveTo(b.X + 60, b.Y + 40);
        t.MoveTo(b.X + 260, b.Y + 40);
        t.Key(Key.Escape);
        check(t.Context.Dragging is null && board.Left is ["a", "b"], "Escape cancels a drag");
        t.Frame(1 / 60f);
        check(Painted(t, "b").Any(i => i.Turn.X != 0 && i.Rect.X > b.X + 5), "a cancelled card glides back home");
        t.Release(b.X + 260, b.Y + 40);
        check(board.Clicks == 0, "releasing after a cancelled drag does not click");
        t.Advance(.6f);
        t.Settle();
        check(Painted(t, "b") is [{ Turn.X: 0 } home] && MathF.Abs(home.Rect.X - b.X) < .5f, "the cancelled card rests where it started");

        t.RightClick(b.X + 20, b.Y + 20);
        t.Context.Input.Button = PointerButton.Secondary;
        t.Press(b.X + 20, b.Y + 20);
        t.MoveTo(b.X + 80, b.Y + 60);
        check(t.Context.Dragging is null, "a secondary press never starts a drag");
        t.Release(b.X + 80, b.Y + 60);
        t.Context.Input.Button = PointerButton.Primary;
    }

    static void Styles(Action<bool, string> check)
    {
        var board = new Board
        {
            Style = DragStyle.Flat
        };
        using var t = Open(board);
        var a = Card(t, "a");
        t.Press(a.X + 20, a.Y + 20);
        t.MoveTo(a.X + 60, a.Y + 40);
        t.Advance(.4f);
        t.Frame();
        t.Frame();
        check(!t.Context.Instances.Any(i => i.Turn.X != 0) && t.Context.DraggedRect is not null, "the flat style lifts without tilting");
        t.Release(a.X + 60, a.Y + 40);
        board.Style = DragStyle.Tilted with
        {
            Angle = -6,
            Sway = 0
        };
        t.Advance(1);
        t.Settle();
        a = Card(t, "a");
        t.Press(a.X + 20, a.Y + 20);
        t.MoveTo(a.X + 60, a.Y + 40);
        t.Advance(.4f);
        t.Frame();
        check(Painted(t, "a").Any(i => MathF.Abs(i.Turn.X + 6 * Degrees) < .01f), "a negative angle tilts the card counterclockwise");
        t.Release(a.X + 60, a.Y + 40);
        t.Context.ReduceMotion = true;
        t.Settle();
        a = Card(t, "a");
        t.Press(a.X + 20, a.Y + 20);
        t.MoveTo(a.X + 60, a.Y + 40);
        t.Frame();
        check(Painted(t, "a").Any(i => MathF.Abs(i.Turn.X + 6 * Degrees) < .01f), "Reduce Motion lifts at once without sway");
        t.Release(a.X + 60, a.Y + 40);
    }

    static void Rotation(Action<bool, string> check)
    {
        using var t = new Tester(c =>
        {
            using var root = c.Div(key: "root").SizeFull().Padding(100);
            using var outer = c.Div(key: "outer").Width(120).Height(120).Rotate(10);
            using var bar = c.Div(key: "bar").Width(100).Height(20).Background(Color.Hex(0xff0000)).Rotate(80);
        }, new(Width: 400, Height: 400));
        t.Settle();
        var bar = t.Context.Instances.Single(i => i.Color.X > .9f && i.Color.Y < .1f);
        check(MathF.Abs(bar.Turn.X - 90 * Degrees) < .001f, "nested rotations add up");
        var pixels = t.Render();
        // Green is low inside the red bar and high on the light background.
        int Green(int px, int py) => pixels.Bytes[(py * pixels.Width + px) * 4 + 1];
        var (cx, cy) = (bar.Rect.X + bar.Rect.Z / 2, bar.Rect.Y + bar.Rect.W / 2);
        var (px0, py0) = (bar.Turn.Y, bar.Turn.Z);
        // The bar's center after the combined turn: a quarter turn makes the wide bar tall.
        var (tx, ty) = (px0 - (cy - py0), py0 + (cx - px0));
        check(Green((int)tx, (int)(ty - 40)) < 50 && Green((int)(tx - 40), (int)ty) > 200, "the CPU renderer draws a turned box turned");
    }
}
