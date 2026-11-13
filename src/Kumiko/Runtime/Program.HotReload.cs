using System.Text.Json;

namespace Kumiko.Runtime;

public sealed partial class Program<TModel, TMessage>
{
    /// <summary>Raised on the UI thread after the selected model changes.</summary>
    public event Action? ModelChanged;

    void RaiseModelChanged() => ModelChanged?.Invoke();

    /// <summary>Writes the selected model as JSON. History, view state and effects are not included.</summary>
    public void SaveModel(string path)
    {
        CheckThread();
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var temporary = fullPath + ".tmp";
        File.WriteAllBytes(temporary, JsonSerializer.SerializeToUtf8Bytes(Model, modelType));
        File.Move(temporary, fullPath, true);
    }

    /// <summary>
    /// Starts a new history from a saved model. Returns false, leaving the program unchanged, when the file is
    /// missing, malformed, or does not round-trip exactly through the current model schema.
    /// </summary>
    public bool RestoreModel(string path)
    {
        CheckThread();
        TModel model;
        WindowEntry[] windows;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(path));
            if (document.RootElement.Deserialize(modelType) is not { } loaded ||
                !JsonElement.DeepEquals(document.RootElement, JsonSerializer.SerializeToElement(loaded, modelType)))
                return false;
            model = loaded;
            windows = ReadWindows(model);
        }
        // Serializing a loaded model can also fail on incomplete data, such as a missing immutable array.
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or NotSupportedException or InvalidOperationException
            or NullReferenceException or ArgumentException)
        {
            return false;
        }
        Suspend();
        history.Clear();
        history.Add(new(0, -1, 0, 0, null, default, model, []));
        Position = 0;
        SelectWindows(windows);
        frozen = false;
        renderTime = 0;
        ResetDebugHistory();
        if (recorder is not null)
        {
            recorder.Dispose();
            recorder = Open(recordingPath!);
            WriteSession(recorder);
        }
        ResetContexts();
        InvalidateWindows();
        RaiseModelChanged();
        return true;
    }
}
