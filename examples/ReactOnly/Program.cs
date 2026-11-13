using Kumiko;
using ReactOnly;

if (Option(args, "--write-client") is { } directory)
{
    var (javaScript, typeScript) = NativeServices.Client();
    Directory.CreateDirectory(directory);
    File.WriteAllText(Path.Combine(directory, "client.js"), javaScript);
    File.WriteAllText(Path.Combine(directory, "client.d.ts"), typeScript);
    return 0;
}
if (Option(args, "--verify") is { } output)
    return Verify.Run(output, Option(args, "--dev-server"));
using var app = new App(new("React Only"));
using var window = NativeHost.Open(app, Option(args, "--dev-server"), visible: true);
app.Run();
return 0;

static string? Option(string[] args, string name) =>
    args.FirstOrDefault(arg => arg.StartsWith(name + "=", StringComparison.Ordinal))?[(name.Length + 1)..];
