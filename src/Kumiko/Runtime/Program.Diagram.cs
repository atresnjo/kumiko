using System.Text;
using System.Text.Json;

namespace Kumiko.Runtime;

public sealed partial class Program<TModel, TMessage>
{
    /// <summary>Exports observed transitions of a nested state discriminator, addressed by JSON Pointer.</summary>
    public void WriteDiagram(string path, string statePointer)
    {
        CheckThread();
        string State(TModel model)
        {
            var value = JsonSerializer.SerializeToElement(model, modelType);
            if (!statePointer.StartsWith('/'))
                throw new ArgumentException("Use a JSON Pointer such as /Filters/Flow/state.", nameof(statePointer));
            foreach (var part in statePointer[1..].Split('/'))
                value = value.GetProperty(part.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal));
            return value.ValueKind == JsonValueKind.String ? value.GetString()! : value.GetRawText();
        }
        var states = history.Select(e => State(e.Model)).Distinct(StringComparer.Ordinal).ToArray();
        var text = new StringBuilder("stateDiagram-v2\n");
        for (var i = 0; i < states.Length; i++)
            text.Append("    state \"").Append(JsonEncodedText.Encode(states[i]).ToString()).Append("\" as s").Append(i).Append('\n');
        text.Append("    [*] --> s0\n");
        var transitions = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in history.Skip(1))
        {
            var from = Array.IndexOf(states, State(history[entry.Parent].Model));
            var to = Array.IndexOf(states, State(entry.Model));
            if (from == to)
                continue;
            var message = JsonSerializer.SerializeToElement(entry.Message!, messageType);
            var label = message.TryGetProperty("type", out var discriminator) ? discriminator.GetString() ?? "message" : "message";
            label = new string(label.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.').ToArray());
            var line = $"    s{from} --> s{to} : {label}\n";
            if (transitions.Add(line))
                text.Append(line);
        }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, text.ToString());
    }
}
