using System.Text.Json;

var output = Path.GetFullPath(args.FirstOrDefault() ?? "artifacts/v2/e2e");
Directory.CreateDirectory(output);
var checks = new List<string>();
void Check(bool condition, string name)
{
    if (!condition)
        throw new InvalidOperationException(name);
    checks.Add(name);
}
ServiceFlows.Run(output, Check);
DiFlows.Run(output, Check);
DiHostFlows.Run(output, Check);
WindowFlows.Run(output, Check);
WindowEdgeFlows.Run(output, Check);
File.WriteAllText(Path.Combine(output, "report.json"), JsonSerializer.Serialize(checks, ReportJson.Default.ListString));
Console.WriteLine($"PASS: {checks.Count} V2 E2E checks; artifacts: {output}");

[System.Text.Json.Serialization.JsonSerializable(typeof(List<string>))]
internal partial class ReportJson : System.Text.Json.Serialization.JsonSerializerContext;
