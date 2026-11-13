using System.Text;
using System.Text.Json;

namespace Kumiko.Runtime;

public sealed partial class Program<TModel, TMessage>
{
    string? recordingPath;

    public void Record(string path)
    {
        CheckThread();
        recorder?.Dispose();
        recorder = null;
        recordingPath = Path.GetFullPath(path);
        recorder = Open(recordingPath);
        retainHistory = true;
        WriteSession(recorder);
    }

    public void Save(string path)
    {
        CheckThread();
        var fullPath = Path.GetFullPath(path);
        if (recorder is not null && fullPath == recordingPath)
        {
            WriteCursor(recorder);
            return;
        }
        using var writer = Open(fullPath);
        WriteSession(writer);
    }

    static StreamWriter Open(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        return new(path, false, new UTF8Encoding(false))
        {
            AutoFlush = true
        };
    }

    void WriteSession(StreamWriter writer)
    {
        WriteLine(writer, json =>
        {
            json.WriteString("kind", "init");
            json.WriteNumber("version", windowFactory is null ? 1 : 2);
            json.WritePropertyName("model");
            JsonSerializer.Serialize(json, history[0].Model, modelType);
        });
        foreach (var entry in history.Skip(1))
            WriteEntry(writer, entry);
        WriteCursor(writer);
    }

    static void WriteLine(StreamWriter? writer, Action<Utf8JsonWriter> write)
    {
        if (writer is null)
            return;
        using var bytes = new MemoryStream();
        using (var json = new Utf8JsonWriter(bytes))
        {
            json.WriteStartObject();
            write(json);
            json.WriteEndObject();
        }
        writer.WriteLine(Encoding.UTF8.GetString(bytes.GetBuffer(), 0, (int)bytes.Length));
    }

    void WriteCursor(StreamWriter? writer) => WriteLine(writer, json =>
    {
        json.WriteString("kind", "cursor");
        json.WriteNumber("step", Position);
        json.WriteNumber("time", renderTime);
    });

    void WriteEntry(StreamWriter? writer, ProgramStep<TModel, TMessage> entry) => WriteLine(writer, json =>
    {
        json.WriteString("kind", "message");
        json.WriteNumber("step", entry.Id);
        json.WriteNumber("parent", entry.Parent);
        json.WriteNumber("frame", entry.Frame);
        json.WriteNumber("time", entry.Time);
        json.WritePropertyName("source");
        if (entry.Source is not { } source)
            json.WriteNullValue();
        else
        {
            json.WriteStartObject();
            json.WriteString("role", source.Role.ToString());
            json.WriteString("name", source.Name);
            json.WriteString("key", source.Key);
            json.WriteString("path", source.Path);
            if (source.Window is not null)
                json.WriteString("window", source.Window);
            json.WriteEndObject();
        }
        if (windowFactory is not null)
            json.WriteString("window", entry.Window);
        json.WritePropertyName("message");
        JsonSerializer.Serialize(json, entry.Message!, messageType);
        json.WritePropertyName("model");
        var model = JsonSerializer.SerializeToElement(entry.Model, modelType);
        model.WriteTo(json);
        json.WriteStartArray("diff");
        Diff(json, "", JsonSerializer.SerializeToElement(history[entry.Parent].Model, modelType), model);
        json.WriteEndArray();
        json.WriteStartArray("commands");
        foreach (var command in entry.Commands)
            json.WriteStringValue(command);
        json.WriteEndArray();
    });

    static void Diff(Utf8JsonWriter json, string path, JsonElement before, JsonElement after)
    {
        if (before.ValueKind == after.ValueKind && (before.ValueKind == JsonValueKind.Undefined || JsonElement.DeepEquals(before, after)))
            return;
        if (before.ValueKind == JsonValueKind.Object && after.ValueKind == JsonValueKind.Object)
        {
            var names = before.EnumerateObject().Select(p => p.Name).Union(after.EnumerateObject().Select(p => p.Name));
            foreach (var name in names)
            {
                before.TryGetProperty(name, out var left);
                after.TryGetProperty(name, out var right);
                Diff(json, path + "/" + name.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal), left, right);
            }
            return;
        }
        json.WriteStartObject();
        json.WriteString("path", path);
        json.WritePropertyName("before");
        WriteValue(json, before);
        json.WritePropertyName("after");
        WriteValue(json, after);
        json.WriteEndObject();
    }

    static void WriteValue(Utf8JsonWriter json, JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Undefined)
            json.WriteNullValue();
        else
            value.WriteTo(json);
    }

    /// <summary>Validates the entire session before replacing history. Effects never run during replay.</summary>
    public void Replay(string path)
    {
        CheckThread();
        var loaded = new List<ProgramStep<TModel, TMessage>>();
        int cursor = 0;
        double time = 0;
        try
        {
            foreach (var line in File.ReadLines(path))
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                switch (root.GetProperty("kind").GetString())
                {
                    case "init" when loaded.Count == 0 && root.GetProperty("version").GetInt32() is 1 or 2:
                        var initial = root.GetProperty("model").Deserialize(modelType) ?? throw new InvalidDataException("Null initial model.");
                        _ = ReadWindows(initial);
                        loaded.Add(new(0, -1, 0, 0, null, default, initial, []));
                        break;
                    case "message" when loaded.Count > 0:
                        var id = root.GetProperty("step").GetInt32();
                        var parent = root.GetProperty("parent").GetInt32();
                        if (id != loaded.Count || parent < 0 || parent >= id)
                            throw new InvalidDataException("Invalid session branch/step order.");
                        var message = root.GetProperty("message").Deserialize(messageType) ?? throw new InvalidDataException("Null message.");
                        var (model, command) = Transition(loaded[parent].Model, message, true);
                        _ = ReadWindows(model);
                        var names = root.GetProperty("commands").EnumerateArray().Select(e => e.GetString()!).ToArray();
                        if (!JsonElement.DeepEquals(JsonSerializer.SerializeToElement(model, modelType), root.GetProperty("model")) ||
                            !names.SequenceEqual(command.Effects.Select(e => e.Name)))
                            throw new InvalidDataException($"Replay diverged at step {id}. The update function or session changed.");
                        var origin = root.GetProperty("source");
                        MessageSource? source = origin.ValueKind == JsonValueKind.Null ? null : new(
                            Enum.Parse<UI.Role>(origin.GetProperty("role").GetString()!), origin.GetProperty("name").GetString()!,
                            origin.GetProperty("key").GetString(), origin.GetProperty("path").GetString()!)
                        {
                            Window = origin.TryGetProperty("window", out var sourceWindow) ? sourceWindow.GetString() : null
                        };
                        time = root.GetProperty("time").GetDouble();
                        loaded.Add(new(id, parent, root.GetProperty("frame").GetInt64(), time, source, message, model, names)
                        {
                            Window = root.TryGetProperty("window", out var messageWindow) ? messageWindow.GetString() : null
                        });
                        cursor = id;
                        break;
                    case "cursor" when loaded.Count > 0:
                        cursor = root.GetProperty("step").GetInt32();
                        time = root.GetProperty("time").GetDouble();
                        if (cursor < 0 || cursor >= loaded.Count)
                            throw new InvalidDataException("Invalid session cursor.");
                        break;
                    default:
                        throw new InvalidDataException("Unsupported session format/version.");
                }
                if (!double.IsFinite(time) || time < 0)
                    throw new InvalidDataException("Invalid session time.");
            }
            if (loaded.Count == 0)
                throw new InvalidDataException("Empty session.");
        }
        catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException or ArgumentException or FormatException or OverflowException)
        {
            throw new InvalidDataException("Malformed session or incompatible model/message schema.", error);
        }
        var windows = ReadWindows(loaded[cursor].Model);
        Suspend();
        recorder?.Dispose();
        recorder = null;
        history.Clear();
        history.AddRange(loaded);
        retainHistory = true;
        Position = cursor;
        SelectWindows(windows);
        ResetDebugHistory();
        renderTime = time;
        ResetContexts();
        InvalidateWindows();
        RaiseModelChanged();
    }
}
