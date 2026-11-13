using System.Security.Cryptography;
using System.Text.Json;
using Kumiko.Examples.Workbench;
using Kumiko.UI;

if (args.FirstOrDefault() == "--linux-services-review")
{
    LinuxServiceReview.Run(Path.GetFullPath(args.ElementAtOrDefault(1) ?? "artifacts/linux-services-review"));
    return;
}

if (args.FirstOrDefault() == "--native-services-only")
{
    CredentialFlows.Run(Path.GetFullPath(args.ElementAtOrDefault(1) ?? "artifacts/native-services"), (ok, name) =>
    {
        if (!ok)
            throw new InvalidOperationException(name);
    });
    SystemServiceFlows.Run(Path.GetFullPath(args.ElementAtOrDefault(1) ?? "artifacts/native-services"), (ok, name) =>
    {
        if (!ok)
            throw new InvalidOperationException(name);
    });
    Console.WriteLine("Native service behavior passed.");
    return;
}

if (ReviewWindows.Child(args))
    return;
if (ReviewLifecycle.Child(args))
    return;
if (Tier1Checks.Child(args))
    return;
if (args.FirstOrDefault() == "--render-performance")
{
    RenderPerformance.Run(Path.GetFullPath(args.ElementAtOrDefault(1) ?? "artifacts/render-performance"));
    return;
}

var output = Path.GetFullPath(args.FirstOrDefault() ?? "artifacts/desktop");
Directory.CreateDirectory(output);
File.Delete(Path.Combine(output, "report.json"));
var checks = new List<string>();
void Check(bool condition, string name)
{
    if (!condition)
        throw new InvalidOperationException(name);
    checks.Add(name);
}

using var model = new InventoryWorkbench();
model.LoadFixture(100_000);
using var ui = new Tester(model.Program, new(Width: 1180, Height: 760, DevMode: true));
Check(ui.Context.Stats.Nodes < 1800, "100,000 inventory records build only the viewport");
Check(ui.HasText("Precision bearing"), "inventory fixture is displayed");
ui.Get(Role.ColumnHeader, "Name").Click();
Check(model.Grid.SortColumn == 0, "header click sorts records");
ui.Get(Role.Cell, "Name: Precision bearing").Click();
var selected = model.Grid.SelectedId;
ui.Key(Key.F2);
ui.Key(Key.A, Modifiers.Primary);
ui.TypeText("Updated bearing");
ui.Key(Key.Enter);
Check(model.Grid.SelectedId == selected && ui.HasText("Updated bearing"), "editing follows record identity after sort");
ui.Get(Role.Cell, "Stock: 24").Click();
ui.Key(Key.F2);
ui.Key(Key.A, Modifiers.Primary);
ui.TypeText("-5");
ui.Key(Key.Enter);
Check(ui.ContainsText("Stock must be"), "invalid stock stays in the editor with an explanation");
ui.Key(Key.Escape);
Check(model.Grid.SelectedRow?.Stock == 24, "Escape discards an invalid draft");
ui.Get(Role.ComboBox, "Supplier").Click();
ui.TypeText("north");
Check(ui.HasText("Northstar Components"), "dropdown search filters suppliers");
ui.Key(Key.Down);
ui.Key(Key.Enter);
Check(model.Supplier == "northstar", "keyboard chooses the filtered supplier");
ui.Get(Role.ComboBox, "Supplier").Click();
ui.TypeText("no such supplier");
Check(ui.HasText("No matching suppliers"), "empty dropdown search gives feedback");
ui.Key(Key.Escape);
Check(!model.SupplierPicker.IsOpen, "Escape dismisses the dropdown");
model.Supplier = null;
ui.Settle();
ui.Get(Role.Cell, "Name: Updated bearing").Click();
ui.Key(Key.F10, Modifiers.Shift);
Check(ui.Context.Accessibility.Any(n => n.Role == Role.Menu), "Shift+F10 opens a row context menu");
ui.Key(Key.Escape);
Check(ui.Context.Focus != 0, "context menu dismissal restores keyboard focus");
ui.Key(Key.End, Modifiers.Primary);
ui.Settle();
Check(model.Grid.SelectedIndex == 99_999, "keyboard can reveal the final virtual row");
ui.Screenshot(Path.Combine(output, "inventory.png"));
File.WriteAllText(Path.Combine(output, "inventory.txt"), ui.Dump());
model.Theme = Theme.ShadcnDark;
ui.Resize(800, 620);
ui.Screenshot(Path.Combine(output, "inventory-dark.png"));
FoundationChecks.Run(output, Check);
GpuChecks.Run(output, Check);
RenderingFlows.Run(output, Check);
Direct3DChecks.Run(output, Check);
NativeDesktopChecks.Run(output, Check);
WorkbenchFlows.Run(output, Check);
TextFlows.Run(output, Check);
DisabledFlows.Run(Check);
Tier1Checks.Run(output, Check);
ReviewLifecycle.Run(output, Check);
ReviewShortcuts.Run(output, Check);
ReviewWindows.Run(output, Check);
ReviewServices.Run(output, Check);
ReviewTransfers.Run(output, Check);
CredentialFlows.Run(output, Check);
SystemServiceFlows.Run(output, Check);
WindowsTextChecks.Run(output, Check);
WindowsWindowChecks.Run(output, Check);
WindowsNotificationChecks.Run(output, Check);
WindowsShellChecks.Run(output, Check);
WindowsUpdateChecks.Run(output, Check);
using var report = File.Create(Path.Combine(output, "report.json"));
using var json = new Utf8JsonWriter(report, new() { Indented = true });
json.WriteStartObject();
json.WriteNumber("version", 1);
json.WriteString("status", "passed");
json.WriteString("platform", System.Runtime.InteropServices.RuntimeInformation.OSDescription);
json.WriteString("textBackend", ui.Context.TextSystem.Backend);
json.WriteStartArray("checks");
foreach (var check in checks)
    json.WriteStringValue(check);
json.WriteEndArray();
json.WriteStartArray("artifacts");
foreach (var path in Directory.GetFiles(output).Where(path => Path.GetFileName(path) != "report.json" && Path.GetExtension(path) is ".png" or ".txt" or ".json").Order())
{
    json.WriteStartObject();
    json.WriteString("file", Path.GetFileName(path));
    json.WriteString("sha256", Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
    json.WriteEndObject();
}

json.WriteEndArray();
json.WriteEndObject();
json.Flush();
ArtifactGallery.Write(output, checks.Count);
Console.WriteLine($"Passed {checks.Count} desktop behavior checks. Artifacts: {output}");
