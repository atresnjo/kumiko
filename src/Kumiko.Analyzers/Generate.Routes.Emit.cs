using System.Text;
using Microsoft.CodeAnalysis;

namespace Kumiko.Analyzers;

static partial class Generate
{
    static void EmitRoutes(StringBuilder output, INamedTypeSymbol union, List<RouteVariant> variants, string property)
    {
        if (!union.ContainingNamespace.IsGlobalNamespace)
            output.AppendLine($"namespace {union.ContainingNamespace.ToDisplayString()} {{");
        var access = union.DeclaredAccessibility == Accessibility.Public ? "public" : "internal";
        output.AppendLine($"[global::System.Text.Json.Serialization.JsonPolymorphic(TypeDiscriminatorPropertyName = {Literal(property)})]");
        foreach (var route in variants)
            output.AppendLine($"[global::System.Text.Json.Serialization.JsonDerivedType(typeof({Full(route.Type)}), {Literal(route.Tag)})]");
        output.AppendLine($"abstract partial record {union.Name};");
        output.AppendLine($"{access} enum {union.Name}Kind {{ {string.Join(", ", variants.Select(v => $"{v.Name} = {v.Kind}"))} }}");
        output.AppendLine($"{access} static class {union.Name}Routes {{");
        output.AppendLine($"public static {union.Name}Kind Kind({Full(union)} route) => route switch {{");
        foreach (var route in variants)
            output.AppendLine($"{Full(route.Type)} => {union.Name}Kind.{route.Name},");
        output.AppendLine("_ => throw new global::System.ArgumentOutOfRangeException(nameof(route)) };");
        output.AppendLine($"public static {Full(union)} Root({union.Name}Kind kind) => kind switch {{");
        foreach (var route in variants.Where(v => v.Constructor.Parameters.Length == 0))
            output.AppendLine($"{union.Name}Kind.{route.Name} => new {Full(route.Type)}(),");
        output.AppendLine("_ => throw new global::System.ArgumentOutOfRangeException(nameof(kind), \"This route requires parameters.\") };");
        output.AppendLine($"public static {Full(union)}? Parse(string? path) {{ var parts = global::Kumiko.Runtime.RoutePath.Segments(path); if (parts.IsDefault) return null;");
        foreach (var route in variants)
        {
            var parts = Parts(route.Template);
            var tests = new List<string> { $"parts.Length == {parts.Length}" };
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var i = 0; i < parts.Length; i++)
            {
                if (!Parameter(parts[i]))
                {
                    tests.Add($"parts[{i}] == {Literal(parts[i])}");
                    continue;
                }
                var parameter = route.Constructor.Parameters.Single(p => p.Name == parts[i][1..^1]);
                var value = $"p{route.Kind}_{i}";
                if (parameter.Type.SpecialType == SpecialType.System_String)
                    values[parameter.Name] = $"parts[{i}]";
                else
                {
                    var parse = parameter.Type.ToDisplayString() == "System.Guid"
                        ? $"global::System.Guid.TryParseExact(parts[{i}], \"D\", out var {value})"
                        : $"{Full(parameter.Type)}.TryParse(parts[{i}], global::System.Globalization.NumberStyles.Integer, global::System.Globalization.CultureInfo.InvariantCulture, out var {value})";
                    tests.Add(parse);
                    values[parameter.Name] = value;
                }
            }
            output.AppendLine($"if ({string.Join(" && ", tests)}) return new {Full(route.Type)}({string.Join(", ", route.Constructor.Parameters.Select(p => values[p.Name]))});");
        }
        output.AppendLine("return null; }");
        output.AppendLine($"public static string Format({Full(union)} route) => route switch {{");
        foreach (var route in variants)
        {
            var parts = Parts(route.Template).Select(part =>
            {
                if (!Parameter(part))
                    return Literal(part);
                var parameter = route.Constructor.Parameters.Single(p => p.Name == part[1..^1]);
                var value = "value." + parameter.Name;
                if (parameter.Type.SpecialType != SpecialType.System_String)
                    value += parameter.Type.ToDisplayString() == "System.Guid" ? ".ToString(\"D\")" : ".ToString(global::System.Globalization.CultureInfo.InvariantCulture)";
                return $"global::Kumiko.Runtime.RoutePath.Encode({value})";
            }).ToArray();
            var value = parts.Length == 0 ? "\"/\"" : "\"/\" + " + string.Join(" + \"/\" + ", parts);
            output.AppendLine($"{Full(route.Type)} value => {value},");
        }
        output.AppendLine("_ => throw new global::System.ArgumentOutOfRangeException(nameof(route)) };");
        var view = variants[0].Contract;
        output.AppendLine($"public static void Render(global::Kumiko.UI.Ui<{Full(view.TypeArguments[1])}> ui, {Full(view.TypeArguments[0])} model, {Full(union)} route) {{ switch(route) {{");
        foreach (var route in variants)
            output.AppendLine($"case {Full(route.Type)} value: ui.Child(new {Full(route.Page)}(model, value), \"route\", static message => message); break;");
        output.AppendLine("default: throw new global::System.ArgumentOutOfRangeException(nameof(route)); } } }");
        if (!union.ContainingNamespace.IsGlobalNamespace)
            output.AppendLine("}");
    }
}
