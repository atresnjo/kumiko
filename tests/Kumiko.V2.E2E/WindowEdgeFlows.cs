using Kumiko.Runtime;

static class WindowEdgeFlows
{
    static Program<WindowModel, WindowMsg> Bare() => new(WindowsApp.Init, WindowsApp.Update, WindowsApp.Root,
        WindowsAppKumikoJson.Default.Model, WindowsAppKumikoJson.Default.Message);

    public static void Run(string output, Action<bool, string> check)
    {
        using (var duplicate = Bare())
        {
            try
            {
                duplicate.WithWindows(_ => [Spec(), Spec()], WindowsApp.WindowRoot, WindowsAppKumikoJson.Default.Window);
                throw new Exception("Duplicate windows were accepted");
            }
            catch (InvalidOperationException error)
            {
                check(error.Message.Contains("Duplicate window identity", StringComparison.Ordinal) && error.Message.Contains("Win.Main", StringComparison.Ordinal),
                    "duplicate identities fail before native creation and name the conflicting key");
            }
        }
        using (var unstable = Bare())
        {
            unstable.DevMode = true;
            var counter = 0;
            try
            {
                unstable.WithWindows(_ => [Spec() with { Title = (++counter).ToString() }], WindowsApp.WindowRoot, WindowsAppKumikoJson.Default.Window);
                throw new Exception("Nondeterministic windows were accepted");
            }
            catch (InvalidOperationException error)
            {
                check(error.Message.Contains("deterministic", StringComparison.Ordinal), "DevMode rejects nondeterministic window specifications");
            }
        }
        using var rejectReplay = Bare().WithWindows(model => model.Count == 0 ? [Spec()] : throw new InvalidOperationException("incompatible windows"),
            WindowsApp.WindowRoot, WindowsAppKumikoJson.Default.Window);
        try
        {
            rejectReplay.Replay(Path.Combine(output, "windows-session.jsonl"));
            throw new Exception("Incompatible window replay was accepted");
        }
        catch (InvalidDataException)
        {
            check(rejectReplay.Position == 0 && rejectReplay.Model.Count == 0 && rejectReplay.Windows.Count == 1,
                "failed window projection during replay leaves the original history and windows intact");
        }
        check(!rejectReplay.RestoreModel(Path.Combine(output, "windows-model.json")) && rejectReplay.Position == 0,
            "incompatible window projection during model restore leaves the program unchanged");
    }

    static WindowSpec<Win, WindowMsg> Spec() => new(new Win.Main(), "Main", new WindowMsg.Close(new Win.Main()));
}
