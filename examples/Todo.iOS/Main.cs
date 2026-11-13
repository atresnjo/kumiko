using Kumiko.iOS;
using Todo;

internal static class EntryPoint
{
    static void Main(string[] args) => NativeApplication.Run(args, TodoApp.Create, new NativeHostOptions { RestoreState = true });
}
