using Kumiko.Rendering;
using Kumiko.UI;

/// <summary>
/// Apple GPU drivers charge a process roughly 230 MB of graphics memory for about a second after each render pass.
/// Frames that would show exactly the image already on screen must not reach the GPU, so pointer motion over
/// static content lets that memory become reclaimable instead of keeping it resident.
/// </summary>
internal static class IdleFrameChecks
{
    internal static void Run(Action<bool, string> check)
    {
        using var t = new Tester(c =>
        {
            using var page = c.Div(key: "page").SizeFull().Padding(16).Gap(12).Background(Color.White);
            c.Text("Static heading", key: "heading");
            c.Button("Hover me", key: "button");
        }, new(Width: 320, Height: 160, Scale: 2));
        using var metal = new MetalRenderer();
        void Present() => metal.Present(t.Context.Instances, t.Context.TextSystem.Atlas, t.Context.Size, t.Context.Scale, Color.White);

        Present();
        check(metal.PresentedFrames == 1, "the first frame reaches the GPU");
        foreach (var x in new[] { 200f, 220f, 240f, 260f })
        {
            t.MoveTo(x, 20);
            Present();
        }
        check(metal.PresentedFrames == 1, $"pointer motion over static content renders no new GPU frames ({metal.PresentedFrames - 1} extra)");

        var button = t.FindText("Hover me") ?? throw new KeyNotFoundException("Hover me");
        t.MoveTo(button.X + button.Width / 2, button.Y + button.Height / 2);
        Present();
        check(metal.PresentedFrames == 2, "a hover that changes the image renders a new frame");
        for (var i = 0; i < 120 && t.Context.Animating; i++)
        {
            t.Advance(1 / 60f);
            Present();
        }
        var settled = metal.PresentedFrames;
        t.MoveTo(button.X + button.Width / 2 + 2, button.Y + button.Height / 2);
        Present();
        check(metal.PresentedFrames == settled, "moving within a settled hover renders no new frame");

        metal.Present(t.Context.Instances, t.Context.TextSystem.Atlas, new(t.Context.Size.Width + 10, t.Context.Size.Height), t.Context.Scale, Color.White);
        check(metal.PresentedFrames == settled + 1, "a new drawable size renders even when the paint list is unchanged");
        metal.Present(t.Context.Instances, t.Context.TextSystem.Atlas, new(t.Context.Size.Width + 10, t.Context.Size.Height), t.Context.Scale, Color.Black);
        check(metal.PresentedFrames == settled + 2, "a new clear color renders even when the paint list is unchanged");
    }
}
