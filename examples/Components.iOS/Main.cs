using Kumiko.iOS;

internal static class EntryPoint
{
    static void Main(string[] args) => NativeApplication.Run(args, CatalogApp.Create, new NativeHostOptions { RestoreState = true });
}
