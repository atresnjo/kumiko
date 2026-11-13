using Kumiko.iOS;
using UIKit;

internal static class EntryPoint
{
    static void Main(string[] args) => NativeApplication.Run(args, FocusApp.Create, new NativeHostOptions
    {
        Background = UIColor.FromRGB(16, 22, 21),
        InterfaceStyle = UIUserInterfaceStyle.Dark,
        RestoreState = true
    });
}
