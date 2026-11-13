internal static class PresentationEntryPoint
{
    static int Main(string[] args)
    {
        try
        {
            var output = Environment.GetEnvironmentVariable("KUMIKO_IOS_ARTIFACTS") ?? args.Single();
            Directory.CreateDirectory(output);
            PresentationFlow.Run(output);
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }
}
