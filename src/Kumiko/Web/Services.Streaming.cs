using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Kumiko.Web;

public sealed partial class ServiceRegistry
{
    sealed record StreamMethod(string Service, string Name, string ArgumentType, string ItemType,
        Func<CallContext, JsonElement, IAsyncEnumerable<string>> Invoke);
    readonly Dictionary<string, StreamMethod> streams = new(StringComparer.Ordinal);
    /// <summary>Registers a typed pull stream. The transport requests one item at a time; no unbounded response queue.</summary>
    public void BindStream<TArgument, TItem>(string service, string method, Func<CallContext, TArgument, IAsyncEnumerable<TItem>> handler,
        JsonTypeInfo<TArgument> arguments, JsonTypeInfo<TItem> item, string argumentType, string itemType)
    {
        Validate(service);
        Validate(method);
        string key = service + "." + method;
        if (methods.ContainsKey(key))
            throw new ArgumentException("IPC method already registered.", nameof(method));
        async IAsyncEnumerable<string> Invoke(CallContext context, JsonElement argument, [EnumeratorCancellation] CancellationToken token = default)
        {
            using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken, token);
            var input = argument.Deserialize(arguments) ?? throw new JsonException("Missing stream argument.");
            await foreach (var value in handler(context with
            {
                CancellationToken = lifetime.Token
            }, input).WithCancellation(lifetime.Token).ConfigureAwait(false))
            {
                lifetime.Token.ThrowIfCancellationRequested();
                yield return JsonSerializer.Serialize(value, item);
            }
        }
        streams.Add(key, new(service, method, argumentType, itemType, (context, argument) => Invoke(context, argument)));
    }
    public IAsyncEnumerable<string> StreamAsync(string method, JsonElement argument, CallContext context) =>
        streams.TryGetValue(method, out var stream) ? stream.Invoke(context, argument) : throw new KeyNotFoundException("Unknown IPC stream: " + method);
}
