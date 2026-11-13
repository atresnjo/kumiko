internal static class EntryPoint
{
    static int Main(string[] args)
    {
        try
        {
            // The bootstrap initializes bindings before Main. Every window stays hidden.
            var output = Environment.GetEnvironmentVariable("KUMIKO_IOS_ARTIFACTS") ?? args.Single();
            Directory.CreateDirectory(output);
            var checks = new List<string>();
            HostFlow.Run(output, checks);
            LifecycleFlow.Run(checks);
            RestorationFlow.Run(output, checks);
            Probe.Report(Path.Combine(output, "host-report.json"), checks);
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }
}
