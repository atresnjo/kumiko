using Kumiko.iOS;

internal static class EntryPoint
{
    // The shared desktop app, hosted by Kumiko's UIKit scene delegate (see Info.plist).
    static void Main(string[] args) => NativeApplication.Run(args, NativeButtonApp.Create);
}
