using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Kumiko.Analyzers;

static partial class Generate
{
    sealed record RouteVariant(INamedTypeSymbol Type, string Template, string Tag, int Kind, string Name,
        IMethodSymbol Constructor, INamedTypeSymbol Page, INamedTypeSymbol Contract);

    static string? Routes(Compilation compilation)
    {
        var output = new StringBuilder();
        var types = Types(compilation.Assembly.GlobalNamespace).ToArray();
        foreach (var union in types.Where(t => Attribute(t, "RouteUnionAttribute") is not null))
        {
            var annotation = Attribute(union, "RouteUnionAttribute")!;
            if (!union.IsAbstract || union.Arity != 0 || union.ContainingType is not null ||
                union.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() is not RecordDeclarationSyntax declaration ||
                !declaration.Modifiers.Any(SyntaxKind.PartialKeyword) ||
                union.GetAttributes().Any(a => a.AttributeClass?.Name is "JsonPolymorphicAttribute" or "JsonDerivedTypeAttribute"))
                return RouteError(union, "KUM016", "Route unions must be top-level, non-generic abstract partial records without manual JSON attributes.");
            var variants = new List<RouteVariant>();
            foreach (var type in types.Where(t => Derives(t, union)))
            {
                if (!type.IsSealed || type.IsAbstract || type.Arity != 0 || !SymbolEqualityComparer.Default.Equals(type.ContainingType, union) ||
                    Attribute(type, "RouteAttribute") is not { ConstructorArguments.Length: 3 } route)
                    return RouteError(type, "KUM016", "Every route must be a nested sealed record with one Route declaration.");
                var template = route.ConstructorArguments[0].Value as string;
                var tag = route.ConstructorArguments[1].Value as string;
                var kind = route.ConstructorArguments[2].Value as int?;
                var name = route.NamedArguments.FirstOrDefault(p => p.Key == "Name").Value.Value as string ?? type.Name;
                var constructor = type.InstanceConstructors.FirstOrDefault(c => !c.Parameters.Any(p => SymbolEqualityComparer.Default.Equals(p.Type, type)));
                if (template is null || string.IsNullOrWhiteSpace(tag) || kind is null or < 0 || !SyntaxFacts.IsValidIdentifier(name) ||
                    SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None || constructor is null || !ValidTemplate(template, constructor))
                    return RouteError(type, "KUM016", "Route template, discriminator, kind or payload is invalid. Use whole-segment parameters with string, int, long or Guid values.");
                var pages = types.Where(t => KumikoAnalyzer.ViewContract(t) is { TypeArguments.Length: 2 } contract && contract.Name == "View" && t.IsSealed && t.Arity == 0 &&
                    t.ContainingType is null && t.InstanceConstructors.Any(c => c.Parameters.Length == 2 && c.DeclaredAccessibility is Accessibility.Public or Accessibility.Internal &&
                    SymbolEqualityComparer.Default.Equals(c.Parameters[0].Type, contract.TypeArguments[0]) &&
                    SymbolEqualityComparer.Default.Equals(c.Parameters[1].Type, type))).ToArray();
                if (pages.Length != 1)
                    return RouteError(type, pages.Length == 0 ? "KUM014" : "KUM015", pages.Length == 0
                        ? "Route has no page. Supply a sealed View with a (Model, RouteVariant) constructor."
                        : "Route has more than one matching page.");
                if (variants.Any(v => v.Tag == tag || v.Kind == kind || v.Name == name || Overlaps(v.Template, template)))
                    return RouteError(type, "KUM016", "Route discriminators, names and kinds must be unique, and templates must not overlap.");
                var view = KumikoAnalyzer.ViewContract(pages[0])!;
                if (variants.Any(v => !SymbolEqualityComparer.Default.Equals(v.Contract, view)))
                    return RouteError(type, "KUM016", "All pages of one route union must use the same model and message types.");
                variants.Add(new(type, template, tag, kind.Value, name, constructor, pages[0], view));
            }
            if (variants.Count == 0)
                return RouteError(union, "KUM016", "A route union needs at least one variant.");
            var property = annotation.ConstructorArguments.FirstOrDefault().Value as string ?? "route";
            if (string.IsNullOrWhiteSpace(property))
                return RouteError(union, "KUM016", "The JSON discriminator property must be nonempty.");
            EmitRoutes(output, union, variants, property);
        }
        return output.ToString();
    }

    static AttributeData? Attribute(INamedTypeSymbol type, string name) => type.GetAttributes().FirstOrDefault(a =>
        a.AttributeClass?.Name == name && a.AttributeClass.ContainingNamespace.ToDisplayString() == "Kumiko.Runtime");

    static string? RouteError(INamedTypeSymbol type, string diagnostic, string detail)
    {
        Console.Error.WriteLine($"{type.Locations.FirstOrDefault()}: error {diagnostic}: {detail}");
        return null;
    }

    static string[] Parts(string template) => template.Length == 0 ? [] : template.Split('/');
    static bool Parameter(string part) => part.StartsWith('{') && part.EndsWith('}');
    static bool ValidTemplate(string template, IMethodSymbol constructor)
    {
        var parts = Parts(template);
        if (parts.Any(p => p.Length == 0 || p.Any(c => char.IsWhiteSpace(c) || c is '%' or '?' or '#' or '\\' or ':') ||
            !Parameter(p) && p.IndexOfAny(['{', '}']) >= 0))
            return false;
        var parameters = parts.Where(Parameter).Select(p => p[1..^1]).ToArray();
        return parameters.Distinct(StringComparer.Ordinal).Count() == parameters.Length && parameters.Length == constructor.Parameters.Length &&
            constructor.Parameters.All(p => parameters.Contains(p.Name, StringComparer.Ordinal) && Payload(p.Type));
    }

    static bool Payload(ITypeSymbol type) => type.SpecialType is SpecialType.System_String or SpecialType.System_Int32 or SpecialType.System_Int64 || type.ToDisplayString() == "System.Guid";

    static bool Overlaps(string left, string right)
    {
        var a = Parts(left);
        var b = Parts(right);
        return a.Length == b.Length && a.Zip(b).All(p => Parameter(p.First) || Parameter(p.Second) || p.First == p.Second);
    }

    static string Full(ITypeSymbol type) => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    static string Literal(string value) => SymbolDisplay.FormatLiteral(value, true);
}
