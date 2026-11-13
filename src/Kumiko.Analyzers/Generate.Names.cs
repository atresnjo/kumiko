using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;

namespace Kumiko.Analyzers;

static partial class Generate
{
    /// <summary>
    /// System.Text.Json names a type's metadata after the type alone. Components nest messages such as
    /// <c>TodoItemMessage.ChangedDraft</c> and <c>TodoListMessage.ChangedDraft</c>; one of two equal names would
    /// silently get no metadata. Colliding records use full names; every closed generic type, including
    /// collections, gets a stable name derived from its fully qualified constructed type.
    /// </summary>
    static string DistinctNames(Compilation compilation, params ITypeSymbol[] roots)
    {
        var seen = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        foreach (var root in roots)
            Visit(root);
        var output = new StringBuilder();
        var candidates = seen.Where(t => !roots.Contains(t, SymbolEqualityComparer.Default));
        var generic = candidates.Where(t => t.IsGenericType && !t.IsUnboundGenericType);
        var colliding = candidates.Where(t => !t.IsGenericType && !System(t)).GroupBy(t => t.Name)
            .Where(g => g.Count() > 1 || g.Key is "Model" or "Message").SelectMany(g => g);
        foreach (var type in generic.Concat(colliding).OrderBy(t => t.ToDisplayString(), StringComparer.Ordinal))
        {
            var fullName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var name = type.IsGenericType ? "KumikoType_" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fullName)))
                : new string([.. type.ToDisplayString().Where(char.IsLetterOrDigit)]);
            output.Append($"[global::System.Text.Json.Serialization.JsonSerializable(typeof({fullName}), TypeInfoPropertyName = \"{name}\")]\n");
        }
        return output.ToString();

        void Visit(ITypeSymbol type)
        {
            if (type is IArrayTypeSymbol array)
            {
                Visit(array.ElementType);
                return;
            }
            if (type is not INamedTypeSymbol symbol || symbol.SpecialType != SpecialType.None || !seen.Add(symbol))
                return;
            foreach (var argument in symbol.TypeArguments)
                Visit(argument);
            if (symbol.TypeKind == TypeKind.Enum || System(symbol))
                return;
            foreach (var property in symbol.GetMembers().OfType<IPropertySymbol>().Where(p => !p.IsStatic && p.DeclaredAccessibility == Accessibility.Public))
                Visit(property.Type);
            if (symbol.BaseType is { } parent)
                Visit(parent);
            if (!symbol.IsAbstract)
                return;
            // Variants are nested in their union or, for unions declared in this project, anywhere in it.
            foreach (var variant in Types(symbol).Concat(Types(compilation.Assembly.GlobalNamespace)).Where(t => Derives(t, symbol)))
                Visit(variant);
        }
    }

    static bool System(INamedTypeSymbol type) => type.ContainingNamespace.ToDisplayString() is var ns &&
        (ns == "System" || ns.StartsWith("System.", StringComparison.Ordinal));
}
