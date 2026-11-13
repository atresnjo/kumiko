using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Kumiko.Analyzers;

public sealed partial class KumikoAnalyzer
{
    sealed partial class Purity
    {
        readonly HashSet<ITypeSymbol> states = new(SymbolEqualityComparer.Default);

        internal void State(ITypeSymbol type, Location location)
        {
            if (!states.Add(type) || type.TypeKind == TypeKind.TypeParameter)
                return;
            if (type.SpecialType is >= SpecialType.System_Boolean and <= SpecialType.System_Decimal || type.SpecialType is SpecialType.System_String or SpecialType.System_Single or SpecialType.System_Double || type.TypeKind == TypeKind.Enum)
                return;
            if (type is not INamedTypeSymbol named)
            {
                Report(Immutable, location, type.ToDisplayString());
                return;
            }
            var name = named.OriginalDefinition.ToDisplayString();
            if (name is "System.DateTime" or "System.DateTimeOffset" or "System.DateOnly" or "System.TimeOnly" or "System.TimeSpan" or "System.Guid")
                return;
            if (named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T || named.IsTupleType ||
                named.ContainingNamespace.ToDisplayString() == "System.Collections.Immutable" && named.Name is
                    "ImmutableArray" or "ImmutableList" or "ImmutableDictionary" or "ImmutableSortedDictionary" or "ImmutableHashSet" or "ImmutableSortedSet")
            {
                foreach (var argument in named.TypeArguments)
                    State(argument, location);
                return;
            }
            if (!named.IsRecord && !(named.IsValueType && named.IsReadOnly))
            {
                Report(Immutable, location, named.ToDisplayString());
                return;
            }
            if (named.BaseType is { SpecialType: not (SpecialType.System_Object or SpecialType.System_ValueType) } parent)
                State(parent, location);
            // Referenced records expose their compiler-generated EqualityContract as an ordinary property.
            foreach (var member in named.GetMembers().Where(m => !m.IsStatic && !m.IsImplicitlyDeclared && !(named.IsRecord && m.Name == "EqualityContract")))
            {
                if (member is IPropertySymbol property)
                {
                    if (property.SetMethod is { IsInitOnly: false })
                        Report(Immutable, property.Locations.FirstOrDefault() ?? location, property.ToDisplayString());
                    State(property.Type, location);
                    if (property.GetMethod is { } getter)
                    {
                        Follow(getter, property.Locations.FirstOrDefault() ?? location, "update");
                        foreach (var reference in property.DeclaringSyntaxReferences)
                            if (reference.GetSyntax(context.CancellationToken) is PropertyDeclarationSyntax { Initializer: { } initializer })
                                Walk(getter, initializer, "update", true);
                    }
                    if (property.SetMethod is { IsInitOnly: true } setter)
                        Follow(setter, property.Locations.FirstOrDefault() ?? location, "update", true);
                }
                if (member is IFieldSymbol field)
                {
                    if (!field.IsReadOnly)
                        Report(Immutable, field.Locations.FirstOrDefault() ?? location, field.ToDisplayString());
                    State(field.Type, location);
                }
            }
            foreach (var constructor in named.InstanceConstructors)
                foreach (var reference in constructor.DeclaringSyntaxReferences)
                    if (reference.GetSyntax(context.CancellationToken) is ConstructorDeclarationSyntax syntax)
                        Walk(constructor, syntax, "update", true);
            foreach (var child in Types(context.Compilation.Assembly.GlobalNamespace).Where(t => SymbolEqualityComparer.Default.Equals(t.BaseType, named)))
                State(child, location);
        }
    }
}
