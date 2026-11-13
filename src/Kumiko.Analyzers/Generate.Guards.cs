using System.Text;
using Microsoft.CodeAnalysis;

namespace Kumiko.Analyzers;

static partial class Generate
{
    static string? Guards(Compilation compilation)
    {
        var output = new StringBuilder();
        foreach (var app in Types(compilation.Assembly.GlobalNamespace))
        {
            var contract = app.Interfaces.FirstOrDefault(i => i.Name == "IKumiko" && i.TypeArguments.Length == 2 && i.ContainingNamespace.ToDisplayString() == "Kumiko.Runtime");
            if (contract is null)
                continue;
            var sync = app.GetMembers("BeforeEnter");
            var async = app.GetMembers("BeforeEnterAsync");
            if (sync.Length == 0 && async.Length == 0)
                continue;
            var state = contract.TypeArguments[0];
            ITypeSymbol? route = null;
            foreach (var (members, isAsync) in new[] { (sync, false), (async, true) })
            {
                if (members.Length == 0)
                    continue;
                if (members.Length != 1 || members[0] is not IMethodSymbol method || !method.IsStatic || method.IsAsync || method.Arity != 0 ||
                    method.Parameters.Length != 2 || method.Parameters.Any(p => p.RefKind != RefKind.None || p.IsOptional) ||
                    method.ReturnNullableAnnotation == NullableAnnotation.Annotated || method.Parameters.Any(p => p.NullableAnnotation == NullableAnnotation.Annotated) ||
                    !SymbolEqualityComparer.Default.Equals(method.Parameters[0].Type, state))
                    return GuardError(app);
                var argument = method.Parameters[1].Type;
                var result = method.ReturnType;
                if (isAsync)
                {
                    if (argument is not INamedTypeSymbol request || !RuntimeGeneric(request, "RouteGuardRequest") ||
                        result is not INamedTypeSymbol command || !RuntimeGeneric(command, "Cmd") ||
                        command.TypeArguments[0] is not INamedTypeSymbol reply || !RuntimeGeneric(reply, "RouteGuardReply") ||
                        !SymbolEqualityComparer.Default.Equals(request.TypeArguments[0], reply.TypeArguments[0]))
                        return GuardError(app);
                    argument = request.TypeArguments[0];
                }
                else if (result is not INamedTypeSymbol decision || !RuntimeGeneric(decision, "RouteDecision") ||
                    !SymbolEqualityComparer.Default.Equals(argument, decision.TypeArguments[0]))
                    return GuardError(app);
                if (route is not null && !SymbolEqualityComparer.Default.Equals(route, argument) || argument.NullableAnnotation == NullableAnnotation.Annotated)
                    return GuardError(app);
                route = argument;
            }
            var model = state.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var message = contract.TypeArguments[1].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var name = route!.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            const string runtime = "global::Kumiko.Runtime.";
            var constraint = $"where TEntry : class, {runtime}INavigationEntry<{name}, TEntry>";
            var policy = sync.Length != 0 ? "BeforeEnter" : $"static (_, _) => new {runtime}RouteDecision<{name}>.Allow()";
            if (!app.ContainingNamespace.IsGlobalNamespace)
                output.AppendLine($"namespace {app.ContainingNamespace.ToDisplayString()} {{");
            output.AppendLine($$"""
                partial class {{app.Name}}
                {
                    public static {{runtime}}RouteDecision<{{name}}> GuardNavigation<TEntry>({{model}} model, {{runtime}}NavigationPath<{{name}}, TEntry> desired) {{constraint}} =>
                        {{runtime}}RouteGuard.BeforeLoad(model, global::System.Linq.Enumerable.Select(desired.Entries, static entry => entry.Route), {{policy}});
                    public static {{runtime}}NavigationGuardResolution<{{name}}, TEntry> CompleteNavigation<TEntry>({{model}} model, {{runtime}}NavigationPath<{{name}}, TEntry> current,
                        {{runtime}}NavigationGuard<{{name}}, TEntry> state, {{runtime}}RouteGuardReply<{{name}}> reply, string stack) {{constraint}} =>
                        state.Resolve(model, current, reply, stack, {{policy}});
                """);
            if (async.Length != 0)
                output.AppendLine($$"""
                    public static {{runtime}}Cmd<{{message}}> CheckNavigation<TEntry>({{model}} model, {{runtime}}NavigationGuard<{{name}}, TEntry> state,
                        global::System.Func<{{runtime}}RouteGuardReply<{{name}}>, {{message}}> map) {{constraint}} =>
                        state.Pending is { } pending ? BeforeEnterAsync(model, pending.Check).Map(map) : {{runtime}}Cmd<{{message}}>.None;
                    """);
            output.AppendLine("}");
            if (!app.ContainingNamespace.IsGlobalNamespace)
                output.AppendLine("}");
        }
        return output.ToString();
    }

    static bool RuntimeGeneric(INamedTypeSymbol type, string name) => type.Name == name && type.TypeArguments.Length == 1 && type.ContainingNamespace.ToDisplayString() == "Kumiko.Runtime";

    static string? GuardError(INamedTypeSymbol app)
    {
        Console.Error.WriteLine($"{app.Locations.FirstOrDefault()}: error KUM017: Supply at most one static BeforeEnter(Model, Route) returning RouteDecision<Route> and/or BeforeEnterAsync(Model, RouteGuardRequest<Route>) returning Cmd<RouteGuardReply<Route>>, using the same non-null route type.");
        return null;
    }
}
