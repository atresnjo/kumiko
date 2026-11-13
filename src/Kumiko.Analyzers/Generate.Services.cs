using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Kumiko.Analyzers;

static partial class Generate
{
    // Follow source helpers and mapped child commands, rather than requiring every
    // service used anywhere in a project. Opaque/library helpers resolve at execution.
    static string RequiredServices(Compilation compilation, INamedTypeSymbol app)
    {
        var required = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
        void Visit(IMethodSymbol method)
        {
            if (!visited.Add(method.OriginalDefinition))
                return;
            foreach (var reference in method.DeclaringSyntaxReferences)
            {
                var syntax = reference.GetSyntax();
                var model = compilation.GetSemanticModel(syntax.SyntaxTree);
                foreach (var call in syntax.DescendantNodes().OfType<InvocationExpressionSyntax>())
                {
                    if (model.GetSymbolInfo(call).Symbol is not IMethodSymbol called)
                        continue;
                    if (called.ContainingNamespace.ToDisplayString() == "Kumiko.Runtime" && called.ContainingType.Name is "Cmd" or "Sub" &&
                        called.Name is "Run" or "Debounce" or "From" && called.TypeArguments.Length == 1 &&
                        called.TypeArguments[0].TypeKind != TypeKind.TypeParameter)
                        required.Add(called.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
                    if (called.ContainingAssembly.Name == "Kumiko" && called.ContainingType.ToDisplayString() == "Kumiko.Runtime.DocumentEffects" &&
                        called.Name is "Save" or "Open" or "Autosave")
                        required.Add("global::Kumiko.Documents.IDocumentStore");
                    Visit(called);
                }
            }
        }
        foreach (var method in app.GetMembers().OfType<IMethodSymbol>().Where(m => m.Name is "Update" or "Subscriptions" or "BeforeEnter" or "BeforeEnterAsync"))
            Visit(method);
        return string.Join(", ", required.Order(StringComparer.Ordinal).Select(type => $"typeof({type})"));
    }
}
