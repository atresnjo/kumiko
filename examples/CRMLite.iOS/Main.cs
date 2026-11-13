using CRMLite;
using Kumiko.iOS;

internal static class EntryPoint
{
    // CRMLite pads its own shell with Context.SafeAreaInsets, so its content fills the window.
    static void Main(string[] args) => NativeApplication.Run(args, Create, new NativeHostOptions { EdgeToEdge = true });

    static Kumiko.Runtime.IProgram Create()
    {
        var initial = Data.Initial();
        return CrmApp.CreateProgram(initial, new FakeCrmBackend(seed: initial).Services());
    }
}
