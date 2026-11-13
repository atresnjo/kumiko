using System.Buffers.Binary;
using System.Text.Json;
using Kumiko.Desktop;
using Kumiko.Rendering;

var output = Path.GetFullPath(args.FirstOrDefault() ?? "artifacts/desktop-formats");
Directory.CreateDirectory(output);
File.Delete(Path.Combine(output, "report.json"));
var checks = new List<string>();
void Check(bool value, string name)
{
    if (!value)
        throw new InvalidOperationException(name);
    checks.Add(name);
}
var bmp = new byte[62];
"BM"u8.CopyTo(bmp);
BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(2), bmp.Length);
BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(10), 54);
BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(14), 40);
BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(18), 2);
BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(22), 1);
BinaryPrimitives.WriteInt16LittleEndian(bmp.AsSpan(26), 1);
BinaryPrimitives.WriteInt16LittleEndian(bmp.AsSpan(28), 24);
bmp[56] = 255;
bmp[57] = 255;
var image = Image.Decode(bmp);
Check(image.Pixels.Width == 2 && image.Pixels.Height == 1 && image.Pixels.Bytes[2] == 255 && image.Pixels.Bytes[4] == 255, "native BMP decoder preserves dimensions and RGB channels");
foreach (var bytes in new[] { new byte[8], bmp[..20] })
{
    try
    {
        Image.Decode(bytes);
        Check(false, "malformed raster rejected");
    }
    catch (InvalidDataException)
    {
        Check(true, "malformed raster rejected");
    }
}
Check(SemanticVersion.Parse("1.0.0-beta.10").CompareTo(SemanticVersion.Parse("1.0.0-beta.2")) > 0, "numeric prerelease identifiers sort numerically");
Check(SemanticVersion.Parse("1.0.0").CompareTo(SemanticVersion.Parse("1.0.0-rc.9")) > 0, "stable release follows prerelease");
Check(SemanticVersion.Parse("1.0.0+one").CompareTo(SemanticVersion.Parse("1.0.0+two")) == 0, "build metadata does not affect precedence");
var releases = new[]
{
    new UpdateRelease("1.1.0-beta.1", "osx-arm64", new Uri("https://example.com/beta.zip"), new string('A', 64)),
    new UpdateRelease("1.0.1", "osx-arm64", new Uri("https://example.com/stable.zip"), new string('A', 64)),
    new UpdateRelease("2.0.0", "linux-x64", new Uri("https://example.com/linux.zip"), new string('A', 64))
};
Check(UpdateFeed.Select(releases, "1.0.0", "osx-arm64", false)?.Version == "1.0.1", "stable channel selects matching runtime stable release");
Check(UpdateFeed.Select(releases, "1.0.0", "osx-arm64", true)?.Version == "1.1.0-beta.1", "explicit prerelease channel selects newer beta");
foreach (string invalid in new[] { "1.0", "1.0.0-01", "1.0.0-", "01.0.0" })
{
    try
    {
        SemanticVersion.Parse(invalid);
        Check(false, "malformed semantic version rejected");
    }
    catch (FormatException)
    {
        Check(true, "malformed semantic version rejected");
    }
}
UpdateFlows.Run(output, Check).GetAwaiter().GetResult();
StreamingFlows.Run(Check).GetAwaiter().GetResult();
SameDocumentFlows.Run(output, Check);
FailedNavigationFlows.Run(output, Check);
NavigationReviewFlows.Run(output, Check);
NativeStreamingFlow.Run(Check);
EmbeddedWebFlow.Run(output, Check);
PageWindowFlow.Run(Check);
using var report = new Utf8JsonWriter(File.Create(Path.Combine(output, "report.json")), new() { Indented = true });
report.WriteStartObject();
report.WriteString("boundary", "native raster on " + System.Runtime.InteropServices.RuntimeInformation.OSDescription + "; update feed fixture only");
report.WriteString("status", "passed");
report.WriteStartArray("checks");
foreach (var check in checks)
    report.WriteStringValue(check);
report.WriteEndArray();
report.WriteEndObject();
Console.WriteLine($"Passed {checks.Count} desktop format checks: {output}");
