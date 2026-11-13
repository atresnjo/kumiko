using System.Text.Json;
using Kumiko.UI;

namespace Kumiko.Runtime;

public sealed partial class Program<TModel, TMessage>
{
    (TModel Model, Cmd<TMessage> Command) Transition(TModel input, TMessage message, bool validate)
    {
        using var scope = KumikoExecution.Enter("update");
        var before = validate ? JsonSerializer.SerializeToUtf8Bytes(input, modelType) : null;
        var first = update(input, message);
        if (!validate)
            return first;
        var firstJson = JsonSerializer.SerializeToUtf8Bytes(first.Model, modelType);
        if (!before!.AsSpan().SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(input, modelType)))
            throw new InvalidOperationException($"Kumiko update mutated its input for {JsonSerializer.Serialize(message, messageType)}.");
        var second = update(input, message);
        if (!before.AsSpan().SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(input, modelType)) ||
            !firstJson.AsSpan().SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(first.Model, modelType)) ||
            !firstJson.AsSpan().SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(second.Model, modelType)) ||
            !first.Command.Effects.Select(e => e.Name).SequenceEqual(second.Command.Effects.Select(e => e.Name)))
            throw new InvalidOperationException($"Kumiko determinism violation for {JsonSerializer.Serialize(message, messageType)}: repeated updates produced different models or commands.");
        return first;
    }

    void RenderView(Context context)
    {
        var desktopStep = history[Position];
        var desktopGeneration = Volatile.Read(ref generation);
        context.DesktopModelCurrent = () => !disposed && messages.IsEmpty &&
            desktopGeneration == Volatile.Read(ref generation) && ReferenceEquals(history[Position], desktopStep);
        var before = context.DevMode || devMode ? JsonSerializer.SerializeToUtf8Bytes(Model, modelType) : null;
        if (windowFactory is null)
            view(context, Model, viewDispatch);
        else if (desiredWindows.FirstOrDefault(w => w.Info.Key == renderingWindow) is { } entry)
        {
            var key = entry.Info.Key;
            entry.Render(context, Model, message => DispatchFromView(message, key, context));
        }
        if (before is not null && !before.AsSpan().SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(Model, modelType)))
            throw new InvalidOperationException("Kumiko view mutated the model. Dispatch a message instead.");
    }
}
