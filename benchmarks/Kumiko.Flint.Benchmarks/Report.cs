using System.Text.Json.Serialization;

sealed record Measurement(string Name, string Scope, int Samples, double MedianMs, double P95Ms, double MaxMs,
    double AllocatedBytesPerOperation, int Nodes, int Instances, double[] RawMilliseconds);
sealed record IdleSample(int Pid, double Seconds, double CpuMilliseconds, double PercentOfOneCore, long WorkingSetBytes);
sealed record Report(string Runtime, string OS, string Architecture, string Compilation, int HistoryCommits,
    int ChangedFiles, int DiffRows, string Limitations, Measurement[] Measurements, IdleSample? NativeIdle);
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(Report))]
partial class PerfJson : JsonSerializerContext;
