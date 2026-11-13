using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Kumiko.Analyzers;

public sealed partial class KumikoAnalyzer
{
    sealed partial class Purity(CompilationAnalysisContext context)
    {
        readonly HashSet<string> reported = [];
        readonly HashSet<string> visited = [];
        readonly HashSet<ITypeSymbol> messageTypes = new(SymbolEqualityComparer.Default);
        readonly Dictionary<IAssemblySymbol, bool> componentAssemblies = new(SymbolEqualityComparer.Default);

        internal void Report(DiagnosticDescriptor rule, Location location, string detail)
        {
            if (reported.Add($"{rule.Id}:{location.SourceTree?.FilePath}:{location.SourceTree?.GetHashCode()}:{location.SourceSpan.Start}"))
                context.ReportDiagnostic(Diagnostic.Create(rule, location, detail));
        }

        internal void Root(IMethodSymbol method, SyntaxNode node, string kind)
        {
            if (kind == "update" && method.Parameters.Length > 1)
                messageTypes.Add(method.Parameters[1].Type);
            if (!method.IsStatic)
                Report(Static, node.GetLocation(), method.Name);
            Walk(method, node, kind);
        }

        internal void PureHook(IMethodSymbol method, SyntaxNode node)
        {
            // Entry creation's route argument is model data, not a message union to exhaustiveness-check.
            if (!method.IsStatic)
                Report(Static, node.GetLocation(), method.Name);
            Walk(method, node, "update");
        }

        internal void Delegate(ExpressionSyntax expression, SemanticModel model, string kind)
        {
            if (expression is AnonymousFunctionExpressionSyntax lambda)
            {
                if (kind is "view" or "update" && !lambda.Modifiers.Any(SyntaxKind.StaticKeyword))
                    Report(Static, expression.GetLocation(), "a non-static lambda");
                if (model.GetSymbolInfo(lambda).Symbol is IMethodSymbol symbol)
                    Walk(symbol, lambda, kind);
                return;
            }
            if (model.GetSymbolInfo(expression).Symbol is IMethodSymbol method)
            {
                if (kind is "view" or "update" && !method.IsStatic)
                    Report(Static, expression.GetLocation(), method.Name);
                Follow(method, expression.GetLocation(), kind);
            }
            else if (kind is "view" or "update")
                Report(Static, expression.GetLocation(), "an opaque delegate");
        }

        void Follow(IMethodSymbol method, Location call, string kind, bool owned = false)
        {
            if (method.MethodKind == MethodKind.DelegateInvoke)
                return;
            if (method.PartialImplementationPart is { } implementation)
            {
                Follow(implementation, call, kind, owned);
                return;
            }
            if (kind != "effect" && method.ContainingType.Locations.Any(l => l.IsInSource) &&
                (method.IsExtern || method.IsAbstract || method.ContainingType.TypeKind == TypeKind.Interface || method.IsVirtual && !method.IsSealed && !method.ContainingType.IsSealed))
            {
                Report(kind == "view" ? ViewMutation : UpdateEffect, call, "an unchecked helper implementation: " + method.Name);
                return;
            }
            if (method.DeclaringSyntaxReferences.Length > 0)
            {
                foreach (var reference in method.DeclaringSyntaxReferences)
                    Walk(method, reference.GetSyntax(context.CancellationToken), kind, owned);
                return;
            }
            // A referenced component's update and views were checked as roots when its own assembly compiled.
            if (Component(method.ContainingType) && Kind(method) is not null)
                return;
            if (ViewContract(method.ContainingType) is not null && method.ContainingType.IsSealed &&
                (method.MethodKind == MethodKind.Constructor || method.Name == "Render"))
                return;
            var ns = method.ContainingNamespace.ToDisplayString();
            if (UI(method.ContainingType))
            {
                if (kind != "view")
                    Report(UpdateEffect, call, method.ToDisplayString());
                return;
            }
            if (kind == "effect")
                return;
            if (Effectful(method) || Mutable(method) || !KnownPure(method, ns))
                Report(kind == "view" ? ViewMutation : UpdateEffect, call, method.ToDisplayString());
        }

        void Walk(IMethodSymbol method, SyntaxNode syntax, string kind, bool owned = false)
        {
            if (!visited.Add($"{method.ToDisplayString()}:{syntax.SyntaxTree.GetHashCode()}:{syntax.SpanStart}:{kind}:{owned}"))
                return;
            var model = context.Compilation.GetSemanticModel(syntax.SyntaxTree);
            foreach (var node in syntax.DescendantNodesAndSelf())
            {
                var phase = Deferred(node, syntax, model) ? "effect" : kind;
                ExpressionSyntax? target = node switch
                {
                    AssignmentExpressionSyntax a when a.Parent is not InitializerExpressionSyntax => a.Left,
                    PrefixUnaryExpressionSyntax p when p.IsKind(SyntaxKind.PreIncrementExpression) || p.IsKind(SyntaxKind.PreDecrementExpression) => p.Operand,
                    PostfixUnaryExpressionSyntax p when p.IsKind(SyntaxKind.PostIncrementExpression) || p.IsKind(SyntaxKind.PostDecrementExpression) => p.Operand,
                    ArgumentSyntax a when a.RefKindKeyword.Kind() is SyntaxKind.RefKeyword or SyntaxKind.OutKeyword &&
                        model.GetSymbolInfo(a.Expression).Symbol is IFieldSymbol or IPropertySymbol => a.Expression,
                    _ => null
                };
                if (phase != "effect" && target is not null && Persistent(target, model, owned ? method.ContainingType : null))
                    Report(phase == "view" ? ViewMutation : UpdateEffect, target.GetLocation(), target.ToString());
                if (node is InvocationExpressionSyntax call)
                {
                    if (model.GetSymbolInfo(call).Symbol is IMethodSymbol called)
                        Follow(called, call.GetLocation(), phase, OwnedReceiver(call.Expression, model, method, owned));
                    else if (model.GetTypeInfo(call.Expression).Type?.TypeKind == TypeKind.Dynamic)
                        Report(phase == "view" ? ViewMutation : UpdateEffect, call.GetLocation(), "an unchecked dynamic call");
                }
                if (node is BaseObjectCreationExpressionSyntax creation && model.GetSymbolInfo(creation).Symbol is IMethodSymbol constructor)
                    Follow(constructor, creation.GetLocation(), phase, true);
                if (node is ConstructorInitializerSyntax baseInitializer && model.GetSymbolInfo(baseInitializer).Symbol is IMethodSymbol baseConstructor)
                    Follow(baseConstructor, baseInitializer.GetLocation(), phase, true);
                if (node is BinaryExpressionSyntax or CastExpressionSyntax && model.GetSymbolInfo(node).Symbol is IMethodSymbol operation)
                    Follow(operation, node.GetLocation(), phase);
                if (node is IdentifierNameSyntax or MemberAccessExpressionSyntax && model.GetSymbolInfo(node).Symbol is { } member)
                {
                    if (node.Parent is MemberAccessExpressionSyntax parent && parent.Name == node)
                        continue;
                    if (member is IPropertySymbol property)
                    {
                        if (UI(property.ContainingType))
                        {
                            if (phase != "view")
                                Report(UpdateEffect, node.GetLocation(), property.ToDisplayString());
                        }
                        else if (property.GetMethod is { } getter)
                            Follow(getter, node.GetLocation(), phase, OwnedReceiver((ExpressionSyntax)node, model, method, owned));
                    }
                    if (phase != "effect" && member is IFieldSymbol field && !field.ContainingType.IsTupleType && !field.IsConst && (!field.IsReadOnly || !CaptureSafe(field.Type, new(SymbolEqualityComparer.Default))) &&
                        !WriteTarget(node) && !(owned && !field.IsStatic && SymbolEqualityComparer.Default.Equals(field.ContainingType, method.ContainingType)) && field.ContainingNamespace.ToDisplayString() != "Kumiko.UI")
                        Report(Static, node.GetLocation(), field.Name);
                    if (phase != "effect" && member is IFieldSymbol { IsReadOnly: true } constant && CaptureSafe(constant.Type, new(SymbolEqualityComparer.Default)))
                        foreach (var reference in constant.DeclaringSyntaxReferences)
                            if (reference.GetSyntax(context.CancellationToken) is VariableDeclaratorSyntax { Initializer: { } initializer })
                                Walk(method, initializer, phase);
                }
                if (phase != "effect" && node is SwitchExpressionSyntax expression)
                    CheckSwitch(expression.GoverningExpression, expression.Arms.Where(a => a.WhenClause is null).Select(a => a.Pattern), model);
                if (phase != "effect" && node is SwitchStatementSyntax statement)
                    CheckSwitch(statement.Expression, statement.Sections.SelectMany(s => s.Labels).OfType<CasePatternSwitchLabelSyntax>()
                        .Where(l => l.WhenClause is null).Select(l => l.Pattern), model, statement.Sections.SelectMany(s => s.Labels).OfType<CaseSwitchLabelSyntax>().Select(l => l.Value));
            }
        }

        static bool OwnedReceiver(ExpressionSyntax expression, SemanticModel model, IMethodSymbol caller, bool owned)
        {
            if (expression is IdentifierNameSyntax)
                return owned;
            if (expression is not MemberAccessExpressionSyntax member)
                return false;
            if (member.Expression is BaseObjectCreationExpressionSyntax)
                return true;
            if (member.Expression is ThisExpressionSyntax)
                return owned;
            if (model.GetSymbolInfo(member.Expression).Symbol is ILocalSymbol local)
                return local.DeclaringSyntaxReferences.Any(r => r.GetSyntax() is VariableDeclaratorSyntax { Initializer.Value: BaseObjectCreationExpressionSyntax }) &&
                    !local.DeclaringSyntaxReferences.Any(r => r.SyntaxTree.GetRoot().DescendantNodes().OfType<AssignmentExpressionSyntax>().Any(a => SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(a.Left).Symbol, local)));
            return false;
        }

        static bool CaptureSafe(ITypeSymbol type, HashSet<ITypeSymbol> seen)
        {
            if (!seen.Add(type) || type.TypeKind == TypeKind.Enum || type.SpecialType is >= SpecialType.System_Boolean and <= SpecialType.System_Decimal ||
                type.SpecialType is SpecialType.System_String or SpecialType.System_Single or SpecialType.System_Double)
                return true;
            if (type is not INamedTypeSymbol named)
                return false;
            if (named.ContainingNamespace.ToDisplayString() == "System.Collections.Immutable" && !named.Name.Contains("Builder", StringComparison.Ordinal))
                return named.TypeArguments.All(t => CaptureSafe(t, seen));
            if (named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T || named.IsTupleType)
                return named.TypeArguments.All(t => CaptureSafe(t, seen));
            if (named.ToDisplayString() is "System.DateTime" or "System.DateTimeOffset" or "System.DateOnly" or "System.TimeOnly" or "System.TimeSpan" or "System.Guid")
                return true;
            if (named.BaseType is { SpecialType: not (SpecialType.System_Object or SpecialType.System_ValueType) } parent && !CaptureSafe(parent, seen))
                return false;
            return (named.IsRecord || named.IsValueType && named.IsReadOnly) && named.GetMembers().All(m => m switch
            {
                IPropertySymbol p when !p.IsStatic && !p.IsImplicitlyDeclared => (p.SetMethod is null || p.SetMethod.IsInitOnly) && CaptureSafe(p.Type, seen),
                IFieldSymbol f when !f.IsStatic && !f.IsImplicitlyDeclared => f.IsReadOnly && CaptureSafe(f.Type, seen),
                _ => true
            });
        }

        static bool WriteTarget(SyntaxNode node) => node.Ancestors().Any(a => a switch
        {
            AssignmentExpressionSyntax assignment => assignment.Left.Span == node.Span,
            PrefixUnaryExpressionSyntax prefix when prefix.IsKind(SyntaxKind.PreIncrementExpression) || prefix.IsKind(SyntaxKind.PreDecrementExpression) => prefix.Operand.Span == node.Span,
            PostfixUnaryExpressionSyntax postfix when postfix.IsKind(SyntaxKind.PostIncrementExpression) || postfix.IsKind(SyntaxKind.PostDecrementExpression) => postfix.Operand.Span == node.Span,
            _ => false
        });

        static bool UI(INamedTypeSymbol type) => type.ContainingNamespace.ToDisplayString().StartsWith("Kumiko.UI", StringComparison.Ordinal) && !type.IsRecord && !(type.IsValueType && type.IsReadOnly) ||
            (type.ContainingNamespace.ToDisplayString() == "Kumiko.iOS" && type.Name == "NativeStackScope") ||
            type.ToDisplayString() is "Kumiko.App" or "Kumiko.Window" or "Kumiko.Desktop.Appearance" or "Kumiko.Rendering.TextSystem" or "Kumiko.iOS.NativeComponents";

        static bool Persistent(ExpressionSyntax target, SemanticModel model, INamedTypeSymbol? owner)
        {
            if (target is TupleExpressionSyntax tuple)
                return tuple.Arguments.Any(a => Persistent(a.Expression, model, owner));
            if (target is ElementAccessExpressionSyntax ownedElement && model.GetSymbolInfo(ownedElement.Expression).Symbol is IFieldSymbol ownedField &&
                !ownedField.IsStatic && SymbolEqualityComparer.Default.Equals(ownedField.ContainingType, owner))
                return false;
            if (target is ElementAccessExpressionSyntax element && model.GetSymbolInfo(element.Expression).Symbol is ILocalSymbol local)
                return !local.DeclaringSyntaxReferences.Any(r => r.GetSyntax() is VariableDeclaratorSyntax { Initializer.Value: ArrayCreationExpressionSyntax or ImplicitArrayCreationExpressionSyntax or StackAllocArrayCreationExpressionSyntax or ImplicitStackAllocArrayCreationExpressionSyntax });
            var symbol = model.GetSymbolInfo(target).Symbol;
            if (target is MemberAccessExpressionSyntax access && model.GetSymbolInfo(access.Expression).Symbol is IPropertySymbol { Name: "Input" or "TextSystem", ContainingType: { } holder } && IsContext(holder))
                return false;
            if (symbol is IFieldSymbol field)
                return field.IsStatic || !SymbolEqualityComparer.Default.Equals(field.ContainingType, owner);
            if (symbol is IPropertySymbol property)
                return !UI(property.ContainingType) && (property.IsStatic || !SymbolEqualityComparer.Default.Equals(property.ContainingType, owner));
            return target is ElementAccessExpressionSyntax;
        }

        static bool Mutable(IMethodSymbol method) => method.ContainingNamespace.ToDisplayString() is
            "System.Collections.Generic" or "System.Collections" or "System.Collections.Concurrent" && method.Name is
            "Add" or "Remove" or "RemoveAt" or "RemoveAll" or "Clear" or "Insert" or "InsertRange" or "AddRange" or "Sort" or "Reverse" or
            "Enqueue" or "Dequeue" or "Push" or "Pop" or "TryAdd" or "TryRemove" or "UnionWith" or "ExceptWith";

        static bool Effectful(IMethodSymbol method)
        {
            var type = method.ContainingType.ToDisplayString();
            var ns = method.ContainingNamespace.ToDisplayString();
            return ns.StartsWith("System.IO", StringComparison.Ordinal) && type != "System.IO.Path" || ns.StartsWith("System.Net", StringComparison.Ordinal) ||
                ns.StartsWith("System.Threading", StringComparison.Ordinal) || type is "System.Console" or "System.Random" or "System.Environment" ||
                type == "System.Guid" && method.Name == "NewGuid" || type is "System.DateTime" or "System.DateTimeOffset" && method.Name is "get_Now" or "get_UtcNow" or "get_Today";
        }

        /// <summary>
        /// Whether an assembly declares components. Their model and message records were checked as Kumiko state when
        /// that assembly compiled, so their accessors and constructors are trusted.
        /// </summary>
        bool ComponentAssembly(IAssemblySymbol assembly)
        {
            if (!componentAssemblies.TryGetValue(assembly, out var declares))
                componentAssemblies[assembly] = declares = Types(assembly.GlobalNamespace).Any(t => Component(t) || ViewContract(t) is not null);
            return declares;
        }

        bool KnownPure(IMethodSymbol method, string ns) => ns == "Kumiko.Runtime" ||
            method.ContainingType.ToDisplayString() == "System.Type" && method.Name is "op_Equality" or "op_Inequality" || ns.StartsWith("System.Collections.Immutable", StringComparison.Ordinal) ||
            ns == "System.Linq" || ns == "System.Collections.Generic" && method.MethodKind == MethodKind.PropertyGet ||
            ns == "System" && method.ContainingType.Name is "ReadOnlySpan" or "Span" or "MemoryExtensions" || ns == "System.Globalization" || method.ContainingType.SpecialType is >= SpecialType.System_Boolean and <= SpecialType.System_Decimal ||
            method.ContainingType.SpecialType is SpecialType.System_String or SpecialType.System_Single or SpecialType.System_Double or SpecialType.System_Object ||
            (method.ContainingType.IsRecord || method.ContainingType.IsValueType && method.ContainingType.IsReadOnly) &&
                (method.ContainingAssembly.Name is "Kumiko" or "Kumiko.iOS" or "System.Private.CoreLib" || method.ContainingType.Locations.Any(l => l.IsInSource) ||
                    ComponentAssembly(method.ContainingAssembly)) || method.ContainingType.TypeKind == TypeKind.Enum || method.ContainingType.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T ||
            method.ContainingType.ToDisplayString() is "System.Math" or "System.MathF" or "System.Convert" or "System.Enum" or "System.Array" or
            "System.IO.Path" or "System.DateTime" or "System.DateTimeOffset" or "System.DateOnly" or "System.TimeOnly" or "System.StringComparer" or "System.TimeSpan" or "System.Guid" or "System.ArgumentException" or
            "System.ArgumentOutOfRangeException" or "System.InvalidOperationException" or "System.NotSupportedException" ||
            ns == "System" && method.ContainingType.Name.StartsWith("ValueTuple", StringComparison.Ordinal);

        static bool Deferred(SyntaxNode node, SyntaxNode root, SemanticModel model)
        {
            foreach (var ancestor in node.Ancestors().TakeWhile(a => a != root))
            {
                if (ancestor is not AnonymousFunctionExpressionSyntax lambda || lambda.Parent is not ArgumentSyntax arg)
                    continue;
                var call = arg.Parent?.Parent;
                var method = call is null ? null : model.GetSymbolInfo(call).Symbol as IMethodSymbol;
                if (method is not null && (KumikoType(method.ContainingType, "Cmd") && method.Name is "Run" or "Debounce" || KumikoType(method.ContainingType, "Sub") || method.ContainingAssembly.Name == "Kumiko" && KumikoType(method.ContainingType, "DocumentEffects") && method.Name is "Save" or "Open" or "Autosave"))
                    return true;
            }
            return false;
        }

        void CheckSwitch(ExpressionSyntax value, IEnumerable<PatternSyntax> patterns, SemanticModel model, IEnumerable<ExpressionSyntax>? cases = null)
        {
            if (model.GetTypeInfo(value).Type is not INamedTypeSymbol { IsAbstract: true } type || !messageTypes.Contains(type))
                return;
            var handled = patterns.SelectMany(p => p.DescendantNodesAndSelf().OfType<PatternSyntax>()).Select(p => p switch
            {
                TypePatternSyntax t => model.GetTypeInfo(t.Type).Type,
                DeclarationPatternSyntax t => model.GetTypeInfo(t.Type).Type,
                RecursivePatternSyntax { PropertyPatternClause: null, Type: { } t } => model.GetTypeInfo(t).Type,
                ConstantPatternSyntax t => model.GetSymbolInfo(t.Expression).Symbol as ITypeSymbol,
                _ => null
            }).OfType<INamedTypeSymbol>().Concat((cases ?? []).Select(e => model.GetSymbolInfo(e).Symbol).OfType<INamedTypeSymbol>()).ToArray();
            var missing = Types(context.Compilation.Assembly.GlobalNamespace).Where(t => !t.IsAbstract && Derives(t, type))
                .Where(t => !handled.Any(h => SymbolEqualityComparer.Default.Equals(h, t) || Derives(t, h))).Select(t => t.ToDisplayString()).ToArray();
            if (missing.Length > 0)
                Report(Exhaustive, value.GetLocation(), string.Join(", ", missing));
        }
    }
}
