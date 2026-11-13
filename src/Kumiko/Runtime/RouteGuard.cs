namespace Kumiko.Runtime;

/// <summary>A synchronous route admission decision. The application owns navigation and deferred state.</summary>
public abstract class RouteDecision<TRoute> where TRoute : notnull
{
    private RouteDecision()
    {
    }

    /// <summary>Admit this route and continue evaluating the path.</summary>
    public sealed class Allow : RouteDecision<TRoute>;

    /// <summary>Reject the proposed path in favor of an application-selected destination.</summary>
    public sealed class Redirect : RouteDecision<TRoute>
    {
        /// <summary>The destination the application should guard before committing.</summary>
        public TRoute To
        {
            get;
        }

        public Redirect(TRoute to)
        {
            ArgumentNullException.ThrowIfNull(to);
            To = to;
        }
    }

    /// <summary>Wait for application-owned prerequisites, such as sign-in or workspace loading.</summary>
    public sealed class Defer : RouteDecision<TRoute>;
}

/// <summary>Pure admission checks for model-owned navigation, independent of rendering and history installation.</summary>
public static class RouteGuard
{
    /// <summary>
    /// Evaluates routes in path order against the same model, stopping at the first redirect or deferral.
    /// An empty path is allowed. The callback must be pure, deterministic and synchronous.
    /// This method does not commit a path, follow a redirect, run effects or remember a deferred target.
    /// </summary>
    public static RouteDecision<TRoute> BeforeLoad<TModel, TRoute>(TModel model, IEnumerable<TRoute> path,
        Func<TModel, TRoute, RouteDecision<TRoute>> beforeLoad) where TRoute : notnull
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(beforeLoad);
        foreach (var route in path)
        {
            if (route is null)
                throw new ArgumentException("A guarded path cannot contain a null route.", nameof(path));
            var decision = beforeLoad(model, route) ?? throw new InvalidOperationException("A route guard must return a decision.");
            if (decision is not RouteDecision<TRoute>.Allow)
                return decision;
        }
        return new RouteDecision<TRoute>.Allow();
    }
}
