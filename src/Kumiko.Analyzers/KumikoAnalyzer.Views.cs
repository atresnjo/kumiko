using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Kumiko.Analyzers;

public sealed partial class KumikoAnalyzer
{
    internal static INamedTypeSymbol? ViewContract(INamedTypeSymbol? type)
    {
        for (var next = type?.BaseType; next is not null; next = next.BaseType)
            if (KumikoType(next, "View") || KumikoType(next, "WindowScreen") ||
                (KumikoType(next, "Screen") || KumikoType(next, "NavigationShell")) && next.TypeArguments.Length == 2)
                return next;
        return null;
    }

    sealed partial class Purity
    {
        internal void ViewClass(INamedTypeSymbol type, TypeDeclarationSyntax syntax)
        {
            if (ViewContract(type) is not { } contract)
                return;
            if (contract.TypeArguments.Length == 3)
                messageTypes.Add(contract.TypeArguments[2]);
            if (!type.IsSealed)
                Report(ViewShape, syntax.Identifier.GetLocation(), "view classes must be sealed");
            foreach (var state in contract.TypeArguments)
                State(state, syntax.Identifier.GetLocation());
            foreach (var field in type.GetMembers().OfType<IFieldSymbol>().Where(f => !f.IsImplicitlyDeclared && !f.IsReadOnly && !f.IsConst))
                Report(ViewShape, field.Locations.First(), "fields must be readonly: " + field.Name);
            foreach (var property in type.GetMembers().OfType<IPropertySymbol>().Where(p => p.SetMethod is not null))
                Report(ViewShape, property.Locations.First(), "properties must be read-only: " + property.Name);
            var model = context.Compilation.GetSemanticModel(syntax.SyntaxTree);
            foreach (var method in syntax.Members.OfType<MethodDeclarationSyntax>())
            {
                if (model.GetDeclaredSymbol(method) is not IMethodSymbol symbol)
                    continue;
                if (symbol.Parameters.Any(p => IsContext(p.Type) || p.Type is INamedTypeSymbol { Name: "Ui", ContainingNamespace: { } ns } && ns.ToDisplayString() == "Kumiko.UI" ||
                    SymbolEqualityComparer.Default.Equals(p.Type, contract.TypeArguments[0])))
                    Report(ViewShape, method.Identifier.GetLocation(), "helpers use Ui, C and Model instead of parameters");
                Walk(symbol, method, "view", true);
            }
            foreach (var call in syntax.DescendantNodes().OfType<InvocationExpressionSyntax>())
                if (model.GetSymbolInfo(call).Symbol is IMethodSymbol { IsStatic: true } helper &&
                    helper.Parameters.Any(p => IsContext(p.Type) || p.Type is INamedTypeSymbol { Name: "Ui" } ui && ui.ContainingNamespace.ToDisplayString() == "Kumiko.UI") &&
                    helper.Parameters.Any(p => SymbolEqualityComparer.Default.Equals(p.Type, contract.TypeArguments[0])))
                    Report(ViewShape, call.GetLocation(), "compose a child View instead of forwarding Ui/context and the whole model to a static helper");
            var constructor = type.InstanceConstructors.FirstOrDefault();
            if (constructor is not null)
            {
                Walk(constructor, syntax, "view", true);
                foreach (var argument in syntax.DescendantNodes().OfType<ArgumentSyntax>())
                {
                    if (argument.Parent?.Parent is not InvocationExpressionSyntax call ||
                        model.GetSymbolInfo(call).Symbol is not IMethodSymbol called ||
                        model.GetTypeInfo(argument.Expression).ConvertedType?.TypeKind != TypeKind.Delegate)
                        continue;
                    // Bindings may invoke callbacks after this render, including UIKit extensions.
                    var receiver = called.ReducedFrom?.Parameters.FirstOrDefault()?.Type as INamedTypeSymbol ?? called.ContainingType;
                    if (!(receiver.Name is "Ui" or "DesktopActions" && receiver.ContainingNamespace.ToDisplayString() == "Kumiko.UI") &&
                        !((KumikoType(receiver, "Screen") || KumikoType(receiver, "NavigationShell")) && called.Name == "Map") &&
                        !(IsContext(receiver) && called.Name is "TextField" or "Checkbox" or "Toggle" or "Slider"))
                        continue;
                    if (CapturesView(argument.Expression, model, type, new(SymbolEqualityComparer.Default)))
                        Report(ViewCapture, argument.Expression.GetLocation(), called.Name);
                }
                foreach (var target in syntax.DescendantNodes().Select(node => node switch
                {
                    AssignmentExpressionSyntax assignment => assignment.Left,
                    PostfixUnaryExpressionSyntax postfix when postfix.IsKind(SyntaxKind.PostIncrementExpression) || postfix.IsKind(SyntaxKind.PostDecrementExpression) => postfix.Operand,
                    PrefixUnaryExpressionSyntax prefix when prefix.IsKind(SyntaxKind.PreIncrementExpression) || prefix.IsKind(SyntaxKind.PreDecrementExpression) => prefix.Operand,
                    _ => null
                }).OfType<ExpressionSyntax>())
                    if (model.GetSymbolInfo(target).Symbol is IParameterSymbol { ContainingSymbol: IMethodSymbol { MethodKind: MethodKind.Constructor } } parameter &&
                        SymbolEqualityComparer.Default.Equals(parameter.ContainingType, type) && target.Ancestors().OfType<MethodDeclarationSyntax>().Any())
                        Report(ViewShape, target.GetLocation(), "primary-constructor props are read-only during rendering: " + parameter.Name);
            }
        }

        static bool FrameValue(ITypeSymbol? type) => IsContext(type) || type is INamedTypeSymbol named &&
            (named.Name is "Ui" or "DesktopActions" && named.ContainingNamespace.ToDisplayString() == "Kumiko.UI" || ViewContract(named) is not null);

        static bool DelegateCapturesView(ILocalSymbol callback, SemanticModel model, INamedTypeSymbol owner, HashSet<ISymbol> seen)
        {
            foreach (var reference in callback.DeclaringSyntaxReferences)
            {
                var declaration = reference.GetSyntax();
                if (declaration is VariableDeclaratorSyntax { Initializer.Value: { } initializer } &&
                    CapturesView(initializer, model, owner, seen))
                    return true;
                var scope = declaration.Ancestors().FirstOrDefault(node => node is
                    MethodDeclarationSyntax or ConstructorDeclarationSyntax or LocalFunctionStatementSyntax or AnonymousFunctionExpressionSyntax);
                if (scope is null)
                    continue;
                // Conservative across branches and later assignments: deferred aliases can observe either value.
                foreach (var assignment in scope.DescendantNodes().OfType<AssignmentExpressionSyntax>())
                    if (SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(assignment.Left).Symbol, callback) &&
                        CapturesView(assignment.Right, model, owner, seen))
                        return true;
            }
            return false;
        }

        static bool CapturesView(SyntaxNode body, SemanticModel model, INamedTypeSymbol owner, HashSet<ISymbol> seen)
        {
            foreach (var node in body.DescendantNodesAndSelf())
            {
                if (node is ThisExpressionSyntax)
                    return true;
                if (node is not IdentifierNameSyntax name || name.Parent is MemberAccessExpressionSyntax access && access.Name == name)
                    continue;
                var symbol = model.GetSymbolInfo(name).Symbol;
                if ((symbol is ILocalSymbol local && FrameValue(local.Type) || symbol is IParameterSymbol parameter && FrameValue(parameter.Type)) &&
                    !symbol.DeclaringSyntaxReferences.Any(r => r.SyntaxTree == body.SyntaxTree && body.Span.Contains(r.Span)))
                    return true;
                if (symbol is IParameterSymbol { ContainingSymbol: IMethodSymbol { MethodKind: MethodKind.Constructor } } prop &&
                    SymbolEqualityComparer.Default.Equals(prop.ContainingType, owner))
                    return true;
                if (symbol is ILocalSymbol { Type.TypeKind: TypeKind.Delegate } callback &&
                    !callback.DeclaringSyntaxReferences.Any(r => r.SyntaxTree == body.SyntaxTree && body.Span.Contains(r.Span)) &&
                    seen.Add(callback) && DelegateCapturesView(callback, model, owner, seen))
                    return true;
                if (symbol is IMethodSymbol { MethodKind: MethodKind.LocalFunction } function && seen.Add(function))
                    foreach (var reference in function.DeclaringSyntaxReferences)
                        if (CapturesView(reference.GetSyntax(), model, owner, seen))
                            return true;
                if (symbol is { IsStatic: false } && (symbol is IFieldSymbol or IPropertySymbol || symbol is IMethodSymbol { MethodKind: MethodKind.Ordinary }) &&
                    (SymbolEqualityComparer.Default.Equals(symbol.ContainingType, owner) || Derives(owner, symbol.ContainingType)))
                    return true;
            }
            return false;
        }
    }
}
