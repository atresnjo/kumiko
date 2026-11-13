using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

var output = Path.GetFullPath(args.FirstOrDefault() ?? "artifacts/text-overflow");
Directory.CreateDirectory(output);
var checks = new List<string>();
TextOverflowChecks.Run(output, (passed, name) =>
{
    if (!passed)
        throw new InvalidOperationException(name);
    checks.Add(name);
});
var artifacts = Directory.GetFiles(output).Where(p => Path.GetFileName(p) != "report.json").Order()
    .Select(p => new Artifact(Path.GetFileName(p), Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))))).ToArray();
var report = new Report(System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported ? "JIT" : "NativeAOT", checks.ToArray(), artifacts);
File.WriteAllText(Path.Combine(output, "report.json"), JsonSerializer.Serialize(report, TextJson.Default.Report));
Console.WriteLine($"PASS: {checks.Count} text E2E checks; artifacts: {output}");

internal sealed record Artifact(string File, string Sha256);
internal sealed record Report(string Compilation, string[] Checks, Artifact[] Artifacts);
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(Report))]
internal partial class TextJson : JsonSerializerContext;
