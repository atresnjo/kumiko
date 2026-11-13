using System.Text.Json.Serialization;

namespace DiskMap.E2E;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(Dictionary<string, object>))]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(SortedDictionary<string, double>))]
[JsonSerializable(typeof(SortedDictionary<string, string>))]
internal sealed partial class ReportJson : JsonSerializerContext;
