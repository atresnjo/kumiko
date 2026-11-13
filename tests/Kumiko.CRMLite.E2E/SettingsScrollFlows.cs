using System.Text.Json;
using System.Text.Json.Serialization;
using Kumiko.UI;

/// <summary>Repeated section clicks must keep the in-flight scroll moving without restarting its ease.</summary>
static class SettingsScrollFlows
{
    public static void Run(string output, Action<bool, string> check)
    {
        using var baseline = new CrmSession();
        using var repeated = new CrmSession();
        foreach (var session in new[] { baseline, repeated })
        {
            session.Click(Role.Button, "Account");
            session.Tester.Context.ReduceMotion = false;
            session.Click(Role.Button, "Billing");
        }

        var samples = new List<SettingsScrollSample>();
        var started = repeated.Tester.Context.Time;
        for (int frame = 0; frame < 40; frame++)
        {
            bool click = frame % 3 == 0;
            if (click)
                repeated.Click(Role.Button, "Billing");
            else
                repeated.Tester.Frame();
            while (baseline.Tester.Context.Time < repeated.Tester.Context.Time - .000001)
                baseline.Tester.Frame();
            samples.Add(new(repeated.Tester.Context.Time - started, click, Top(baseline, "Billing"), Top(repeated, "Billing")));
            if (frame == 6)
            {
                baseline.Tester.Screenshot(Path.Combine(output, "settings-scroll-single.png"));
                repeated.Tester.Screenshot(Path.Combine(output, "settings-scroll-repeated.png"));
            }
        }
        File.WriteAllText(Path.Combine(output, "settings-scroll.json"),
            JsonSerializer.Serialize(samples, SettingsScrollJson.Default.ListSettingsScrollSample));
        float difference = samples.Max(s => MathF.Abs(s.SingleTop - s.RepeatedTop));
        check(difference < 1, $"repeated section clicks preserve the original scroll trajectory (maximum difference {difference:0.00} px)");
        var viewport = repeated.Node(Role.ScrollArea, "Settings content").Rect;
        check(MathF.Abs(Top(repeated, "Billing") - viewport.Y - 28) < 1,
            "repeated section clicks arrive at the requested alignment on time");

        var landed = Top(repeated, "Billing");
        for (int click = 0; click < 4; click++)
            repeated.Click(Role.Button, "Billing");
        check(MathF.Abs(Top(repeated, "Billing") - landed) < 1, "clicking an aligned section keeps the content still");

        repeated.Click(Role.Button, "General");
        repeated.Tester.Advance(.12f);
        repeated.Click(Role.Button, "Security");
        repeated.Tester.Advance(1);
        repeated.Tester.Settle();
        check(MathF.Abs(Top(repeated, "Security") - viewport.Y - 28) < 1,
            "clicking a different section during a glide still changes its destination");

        repeated.Click(Role.Button, "Billing");
        repeated.Tester.Advance(1);
        repeated.Tester.Settle();
        repeated.Tester.MoveTo(viewport.X + viewport.Width / 2, viewport.Y + viewport.Height / 2);
        repeated.Tester.Scroll(-80);
        repeated.Tester.Settle();
        check(MathF.Abs(Top(repeated, "Billing") - landed) > 40, "manual scrolling can move away from the last jump target");
        repeated.Click(Role.Button, "Billing");
        repeated.Tester.Advance(1);
        repeated.Tester.Settle();
        repeated.Tester.Screenshot(Path.Combine(output, "settings-scroll-landed.png"));
        File.WriteAllText(Path.Combine(output, "settings-scroll-landed.txt"), repeated.Tester.Dump());
        check(MathF.Abs(Top(repeated, "Billing") - landed) < 1,
            $"clicking the same section after manual scrolling realigns it ({Top(repeated, "Billing"):0.00} vs {landed:0.00} px)");
    }

    static float Top(CrmSession session, string section) =>
        session.Tester.Context.Accessibility.Single(n => n.Role == Role.Heading && n.Name == section).Rect.Y;
}

sealed record SettingsScrollSample(double Seconds, bool Click, float SingleTop, float RepeatedTop);

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(List<SettingsScrollSample>))]
partial class SettingsScrollJson : JsonSerializerContext;
