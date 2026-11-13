using Kumiko;
using ReactVite;

if (args.Any(arg => arg.StartsWith("--kumiko-", StringComparison.Ordinal)))
{
    // Offscreen snapshots, trees and agent sessions render the native shell through
    // Tester. The webview slot draws its placeholder; no browser is created.
    Dev.Run<CounterApp>("React + Vite in Kumiko", Host.Width, Host.Height, args);
    return 0;
}
if (Option(args, "--write-client") is { } directory)
{
    var (javaScript, typeScript) = HostServices.Client();
    Directory.CreateDirectory(directory);
    File.WriteAllText(Path.Combine(directory, "client.js"), javaScript);
    File.WriteAllText(Path.Combine(directory, "client.d.ts"), typeScript);
    return 0;
}
if (Option(args, "--verify") is { } output)
    return Verify.Run(output, Option(args, "--dev-server"));
Host.Run(Option(args, "--dev-server"));
return 0;

static string? Option(string[] args, string name) =>
    args.FirstOrDefault(arg => arg.StartsWith(name + "=", StringComparison.Ordinal))?[(name.Length + 1)..];
