using Microsoft.Extensions.DependencyInjection;

namespace Kumiko.Runtime;

/// <summary>Effect dependencies backed by DI scopes or explicit borrowed instances. The host owns the root provider.</summary>
public sealed class KumikoServices
{
    readonly Dictionary<Type, object> values = [];
    IServiceProvider? provider;
    IServiceScopeFactory? scopes;
    IServiceProviderIsService? registrations;

    public KumikoServices()
    {
    }

    public KumikoServices Add<TService>(TService service) where TService : class
    {
        ArgumentNullException.ThrowIfNull(service);
        if (provider is not null)
            throw new InvalidOperationException("Register DI services on IServiceCollection before building the provider.");
        if (!values.TryAdd(typeof(TService), service))
            throw new InvalidOperationException($"Kumiko service already registered: {typeof(TService).FullName}");
        return this;
    }

    /// <summary>Uses the standard DI scope factory, with no copied service list or root service resolution.</summary>
    public static KumikoServices From(IServiceProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        return new()
        {
            provider = provider,
            scopes = provider.GetRequiredService<IServiceScopeFactory>(),
            registrations = provider.GetRequiredService<IServiceProviderIsService>()
        };
    }

    /// <summary>Copies explicitly named registrations from a host provider without taking ownership.</summary>
    public static KumikoServices From(IServiceProvider provider, params Type[] services)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(services);
        var result = new KumikoServices();
        foreach (var type in services.Distinct())
        {
            ArgumentNullException.ThrowIfNull(type);
            result.values.Add(type, provider.GetService(type) ?? throw Missing([type]));
        }
        return result;
    }

    /// <summary>Used by generated factories to reject statically discovered missing dependencies before startup.</summary>
    public KumikoServices Validate(params Type[] services)
    {
        ArgumentNullException.ThrowIfNull(services);
        var missing = services.Where(type => !(registrations?.IsService(type) ?? values.ContainsKey(type))).ToArray();
        if (missing.Length > 0)
            throw Missing(missing);
        return this;
    }

    static InvalidOperationException Missing(IEnumerable<Type> types) =>
        new("Missing Kumiko effect services: " + string.Join(", ", types.Select(t => t.FullName)));

    internal TService Resolve<TService>() where TService : class =>
        provider is not null ? provider.GetRequiredService<TService>() :
        values.TryGetValue(typeof(TService), out var service) ? (TService)service : throw Missing([typeof(TService)]);

    internal ExecutionScope OpenScope()
    {
        if (scopes is null)
            return new(this, null);
        var scope = scopes.CreateAsyncScope();
        return new(new KumikoServices { provider = scope.ServiceProvider }, scope);
    }

    internal sealed class ExecutionScope(KumikoServices services, AsyncServiceScope? scope) : IAsyncDisposable
    {
        internal KumikoServices Services => services;
        public ValueTask DisposeAsync() => scope?.DisposeAsync() ?? ValueTask.CompletedTask;
    }

    internal KumikoServices Snapshot()
    {
        var copy = new KumikoServices { provider = provider, scopes = scopes, registrations = registrations };
        foreach (var pair in values)
            copy.values.Add(pair.Key, pair.Value);
        return copy;
    }
}
