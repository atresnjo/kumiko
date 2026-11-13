internal static class EntryPoint
{
    static int Main(string[] args)
    {
        try
        {
            // The bootstrap initializes bindings before Main. Native integration stays hidden.
            var output = Environment.GetEnvironmentVariable("KUMIKO_IOS_ARTIFACTS") ?? args.Single();
            if (Environment.GetEnvironmentVariable("KUMIKO_IOS_HOT_RELOAD") == "1")
                HotReloadFlow.Run(output);
            else if (Environment.GetEnvironmentVariable("KUMIKO_IOS_FOCUS_ONLY") == "1")
                FocusFlow.Run(output);
            else if (Environment.GetEnvironmentVariable("KUMIKO_IOS_FLEXBOX_ONLY") == "1")
                FlexboxFlow.Run(output);
            else if (Environment.GetEnvironmentVariable("KUMIKO_IOS_TABS_ONLY") == "1")
                TabBarFlow.Run(output);
            else if (Environment.GetEnvironmentVariable("KUMIKO_IOS_SHEET_ONLY") == "1")
                SheetFlow.Run(output);
            else if (Environment.GetEnvironmentVariable("KUMIKO_IOS_CATALOG_ONLY") == "1")
                CatalogFlow.Run(output);
            else if (Environment.GetEnvironmentVariable("KUMIKO_IOS_TODO_ONLY") == "1")
                TodoFlow.Run(output);
            else
            {
                ResponsiveFlow.Run(output);
                ButtonFlow.Run(output);
                ComponentsFlow.Run(output);
                FlexboxFlow.Run(output);
                FocusFlow.Run(output);
                TabBarFlow.Run(output);
                SheetFlow.Run(output);
                CatalogFlow.Run(output);
                TodoFlow.Run(output);
                PresentationFlow.Run(output);
            }
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }
}
