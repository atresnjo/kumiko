using System.Text.Json.Serialization;

namespace Kumiko.Runtime;

/// <summary>A serializable result from application-owned asynchronous admission work.</summary>
public enum RouteAdmission
{
    Allow,
    Redirect,
    Defer,
    Reject
}

/// <summary>Identifies one entry check. Workers must echo Stack, Request and Step in their reply.</summary>
public sealed record RouteGuardRequest<TRoute>(string Stack, long Request, int Step, TRoute Route) where TRoute : notnull;

/// <summary>A request-bound admission result. Redirect uses the supplied destination.</summary>
public sealed record RouteGuardReply<TRoute> where TRoute : notnull
{
    public string Stack
    {
        get;
    }
    public long Request
    {
        get;
    }
    public int Step
    {
        get;
    }
    public RouteAdmission Admission
    {
        get;
    }
    public TRoute? RedirectTo
    {
        get;
    }

    [JsonConstructor]
    public RouteGuardReply(string stack, long request, int step, RouteAdmission admission, TRoute? redirectTo = default)
    {
        if (string.IsNullOrWhiteSpace(stack) || request < 1 || step < 0 || !Enum.IsDefined(admission) || admission == RouteAdmission.Redirect && redirectTo is null)
            throw new InvalidOperationException("Invalid route guard reply.");
        Stack = stack;
        Request = request;
        Step = step;
        Admission = admission;
        RedirectTo = redirectTo;
    }
}

/// <summary>Serialized admission work bound to an unchanged source visit and revision.</summary>
public sealed record PendingRouteGuard<TRoute, TEntry> where TRoute : notnull where TEntry : class, INavigationEntry<TRoute, TEntry>
{
    public long Request
    {
        get;
    }
    public BackRequest Source
    {
        get;
    }
    public NavigationPath<TRoute, TEntry> Desired
    {
        get;
    }
    public int Step
    {
        get;
    }
    [JsonIgnore] public RouteGuardRequest<TRoute> Check => new(Source.Stack, Request, Step, Desired.Entries[Step].Route);

    [JsonConstructor]
    public PendingRouteGuard(long request, BackRequest source, NavigationPath<TRoute, TEntry> desired, int step)
    {
        if (request < 1 || source is null || string.IsNullOrWhiteSpace(source.Stack) || source.Revision < 0 || source.From < 0 ||
            desired is null || source.To != desired.Top.Id || desired.Revision < source.Revision || step < 0 || step >= desired.Entries.Length)
            throw new InvalidOperationException("Invalid pending route guard.");
        Request = request;
        Source = source;
        Desired = desired;
        Step = step;
    }
}

/// <summary>An ephemeral reduction result. A null Path never authorizes a commit.</summary>
public sealed record NavigationGuardResolution<TRoute, TEntry>(NavigationGuard<TRoute, TEntry> State,
    NavigationPath<TRoute, TEntry>? Path, RouteDecision<TRoute>? Decision)
    where TRoute : notnull where TEntry : class, INavigationEntry<TRoute, TEntry>;

/// <summary>Immutable async admission state. It describes work; it never runs a command or installs a path.</summary>
public sealed record NavigationGuard<TRoute, TEntry> where TRoute : notnull where TEntry : class, INavigationEntry<TRoute, TEntry>
{
    public long Sequence
    {
        get;
    }
    public PendingRouteGuard<TRoute, TEntry>? Pending
    {
        get;
    }

    [JsonConstructor]
    public NavigationGuard(long sequence = 0, PendingRouteGuard<TRoute, TEntry>? pending = null)
    {
        if (sequence < 0 || pending is not null && pending.Request != sequence)
            throw new InvalidOperationException("Invalid navigation guard sequence.");
        Sequence = sequence;
        Pending = pending;
    }

    /// <summary>Withdraws work without reusing its identity. Preserve this state across sign-out.</summary>
    public NavigationGuard<TRoute, TEntry> Cancel() => Pending is null ? this : new(Sequence);

    /// <summary>Starts checking an already synchronously admitted and discard-confirmed path.</summary>
    public NavigationGuard<TRoute, TEntry> Start(NavigationPath<TRoute, TEntry> current, NavigationPath<TRoute, TEntry> desired, string stack)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(desired);
        ArgumentException.ThrowIfNullOrWhiteSpace(stack);
        if (Follow(current, desired) is null)
            throw new InvalidOperationException("Cannot guard an unrelated or stale navigation path.");
        var request = checked(Sequence + 1);
        return new(request, new(request, current.RequestTo(desired.Top.Id, stack), desired, 0));
    }

    /// <summary>
    /// Advances only a matching result. Final approval follows current entry state and reruns synchronous
    /// admission against the current model. Applications own errors, redirects, deferral and committing Path.
    /// </summary>
    public NavigationGuardResolution<TRoute, TEntry> Resolve<TModel>(TModel model, NavigationPath<TRoute, TEntry> current,
        RouteGuardReply<TRoute> reply, string stack, Func<TModel, TRoute, RouteDecision<TRoute>> beforeEnter)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(reply);
        ArgumentNullException.ThrowIfNull(beforeEnter);
        if (Pending is not { } pending || reply.Request != pending.Request || reply.Step != pending.Step ||
            reply.Stack != pending.Source.Stack || stack != pending.Source.Stack)
            return new(this, null, null);
        if (!current.Matches(pending.Source, stack))
            return new(Cancel(), null, null);
        switch (reply.Admission)
        {
            case RouteAdmission.Allow:
                if (pending.Step + 1 < pending.Desired.Entries.Length)
                    return new(new(Sequence, new(pending.Request, pending.Source, pending.Desired, pending.Step + 1)), null, null);
                var path = Follow(current, pending.Desired);
                if (path is null)
                    return new(Cancel(), null, null);
                var decision = RouteGuard.BeforeLoad(model, path.Entries.Select(static entry => entry.Route), beforeEnter);
                return new(Cancel(), decision is RouteDecision<TRoute>.Allow ? path : null, decision);
            case RouteAdmission.Redirect:
                return new(Cancel(), null, new RouteDecision<TRoute>.Redirect(reply.RedirectTo!));
            case RouteAdmission.Defer:
                return new(Cancel(), null, new RouteDecision<TRoute>.Defer());
            case RouteAdmission.Reject:
                return new(Cancel(), null, null);
            default:
                throw new InvalidOperationException("Unknown route admission.");
        }
    }

    static NavigationPath<TRoute, TEntry>? Follow(NavigationPath<TRoute, TEntry> current, NavigationPath<TRoute, TEntry> desired) =>
        current.Revision == desired.Revision && current.NextId == desired.NextId && current.Entries.Length == desired.Entries.Length &&
        current.Entries.Zip(desired.Entries).All(static pair => pair.First.Id == pair.Second.Id && EqualityComparer<TRoute>.Default.Equals(pair.First.Route, pair.Second.Route))
            ? current : current.Follow(desired);
}
