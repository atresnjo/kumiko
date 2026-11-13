using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Kumiko.Analyzers;

/// <summary>Enforces Kumiko roots and follows source helpers, accessors, and deferred effects.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed partial class KumikoAnalyzer : DiagnosticAnalyzer
{
    static DiagnosticDescriptor Rule(string id, string title, string message) => new(id, title, message, "Kumiko", DiagnosticSeverity.Error, true);
    static readonly DiagnosticDescriptor ViewMutation = Rule("KUM005", "Dispatch application state changes", "Kumiko views must dispatch messages instead of mutating or executing {0}");
    static readonly DiagnosticDescriptor UpdateEffect = Rule("KUM006", "Keep updates deterministic", "Move {0} into a command or subscription; effects cannot read UI state");
    static readonly DiagnosticDescriptor Exhaustive = Rule("KUM007", "Handle every message", "Message switch is missing an unguarded case for {0}");
    static readonly DiagnosticDescriptor Immutable = Rule("KUM008", "Use deeply immutable models and messages", "Kumiko state contains mutable or unsupported type/member {0}");
    static readonly DiagnosticDescriptor Static = Rule("KUM009", "Kumiko functions capture no application state", "Kumiko update/runtime roots must be static and cannot capture mutable application state: {0}");
    static readonly DiagnosticDescriptor Hosting = Rule("KUM010", "Host a Kumiko program", "Applications must host a Kumiko program instead of using {0}");
    static readonly DiagnosticDescriptor ViewShape = Rule("KUM012", "Use render-local view classes", "Invalid Kumiko view: {0}");
    static readonly DiagnosticDescriptor ViewCapture = Rule("KUM013", "Do not retain a render-local view", "Copy immutable values to locals before a {0} callback; do not capture the view");
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [ViewMutation, UpdateEffect, Exhaustive, Immutable, Static, Hosting, ViewShape, ViewCapture];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationAction(Analyze);
    }

    static bool IsContext(ITypeSymbol? type) => type?.ToDisplayString() == "Kumiko.UI.Context";
    static bool IsCmd(ITypeSymbol? type) => type is INamedTypeSymbol { Name: "Cmd" } named && named.ContainingNamespace.ToDisplayString() == "Kumiko.Runtime";
    static bool IsDispatch(ITypeSymbol? type) => type is INamedTypeSymbol { Name: "Dispatch" } named && named.ContainingNamespace.ToDisplayString() == "Kumiko.Runtime";
    static bool KumikoType(INamedTypeSymbol? type, string name) => type?.Name == name && type.ContainingNamespace.ToDisplayString() == "Kumiko.Runtime";

    /// <summary>Whether a type implements IKumikoComponent, whose members are checked where it is compiled.</summary>
    internal static bool Component(INamedTypeSymbol? type) => type is not null && type.AllInterfaces.Any(i => KumikoType(i, "IKumikoComponent"));

    static string? Kind(IMethodSymbol method)
    {
        if (method.IsStatic && method.Name is "Root" or "WindowRoot" &&
            method.ContainingType.AllInterfaces.Any(i => KumikoType(i, "IKumiko") || KumikoType(i, "IKumikoWindows")))
            return "view";
        if (method.IsStatic && method.Name == "Windows" && method.ContainingType.AllInterfaces.Any(i => KumikoType(i, "IKumikoWindows")))
            return "update";
        if (method.IsStatic && Component(method.ContainingType))
        {
            if (method.Name == "Update" && method.Parameters.Length == 2)
                return "update";
            if (method.Parameters.Length > 0 && method.Parameters[0].Type is INamedTypeSymbol { Name: "Ui" } view &&
                view.ContainingNamespace.ToDisplayString() == "Kumiko.UI")
                return "view";
        }
        var attributes = method.GetAttributes().Select(a => a.AttributeClass?.ToDisplayString()).ToArray();
        if (method.Parameters.Length == 2 && method.Parameters[0].Type is INamedTypeSymbol { Name: "Ui" } ui && ui.ContainingNamespace.ToDisplayString() == "Kumiko.UI" ||
            attributes.Contains("Kumiko.Runtime.KumikoViewAttribute") || method.Parameters.Length == 3 &&
            IsContext(method.Parameters[0].Type) && IsDispatch(method.Parameters[2].Type))
            return "view";
        if (attributes.Contains("Kumiko.Runtime.KumikoUpdateAttribute") || method.Parameters.Length == 2 &&
            method.ReturnType is INamedTypeSymbol { IsTupleType: true } tuple && tuple.TupleElements.Length == 2 && IsCmd(tuple.TupleElements[1].Type))
            return "update";
        return null;
    }

    static void Analyze(CompilationAnalysisContext context)
    {
        if (context.Options.AnalyzerConfigOptionsProvider.GlobalOptions.TryGetValue("build_property.KumikoStrict", out var strict) && strict == "false")
            return;
        var analysis = new Purity(context);
        foreach (var tree in context.Compilation.SyntaxTrees)
        {
            var model = context.Compilation.GetSemanticModel(tree);
            foreach (var node in tree.GetRoot(context.CancellationToken).DescendantNodes())
            {
                if (node is TypeDeclarationSyntax declaration && model.GetDeclaredSymbol(declaration) is INamedTypeSymbol view)
                {
                    analysis.ViewClass(view, declaration);
                    if (view.AllInterfaces.Any(i => KumikoType(i, "INavigationEntry")))
                        foreach (var policy in declaration.Members.OfType<MethodDeclarationSyntax>().Where(m => m.Identifier.ValueText is "Create" or "IsValidPath"))
                            if (model.GetDeclaredSymbol(policy) is IMethodSymbol hook)
                                analysis.PureHook(hook, policy);
                }
                if (node is MethodDeclarationSyntax or LocalFunctionStatementSyntax && model.GetDeclaredSymbol(node) is IMethodSymbol method && Kind(method) is { } kind)
                    analysis.Root(method, node, kind);
                if (node is MethodDeclarationSyntax hookSyntax && model.GetDeclaredSymbol(hookSyntax) is IMethodSymbol admission &&
                    admission.Name is "BeforeEnter" or "BeforeEnterAsync" && admission.ContainingType.AllInterfaces.Any(i => KumikoType(i, "IKumiko")))
                    analysis.PureHook(admission, hookSyntax);
                if (node is BaseObjectCreationExpressionSyntax creation && model.GetTypeInfo(creation).Type is INamedTypeSymbol type && KumikoType(type, "Program"))
                {
                    foreach (var state in type.TypeArguments)
                        analysis.State(state, creation.GetLocation());
                    if (creation.ArgumentList is { } arguments && model.GetSymbolInfo(creation).Symbol is IMethodSymbol constructor)
                        foreach (var argument in arguments.Arguments)
                        {
                            var index = arguments.Arguments.IndexOf(argument);
                            var parameter = argument.NameColon is { } name ? constructor.Parameters.FirstOrDefault(p => p.Name == name.Name.Identifier.ValueText) :
                                index < constructor.Parameters.Length ? constructor.Parameters[index] : null;
                            var role = parameter?.Name;
                            if (role is "view" or "update" or "root")
                                analysis.Delegate(argument.Expression, model, role == "root" ? "view" : role);
                            if (role == "subscriptions")
                                analysis.Delegate(argument.Expression, model, "effect");
                        }
                }
                if (node is BaseTypeSyntax baseType && model.GetTypeInfo(baseType.Type).Type is INamedTypeSymbol contract && (KumikoType(contract, "IKumiko") || KumikoType(contract, "IKumikoComponent")) && contract.TypeArguments.Length >= 2)
                    foreach (var state in contract.TypeArguments)
                        analysis.State(state, baseType.GetLocation());
                if (node is InvocationExpressionSyntax or BaseObjectCreationExpressionSyntax)
                {
                    var info = model.GetSymbolInfo(node);
                    var called = info.Symbol as IMethodSymbol ?? info.CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault(FreeStyle);
                    if ((called is { Name: "BeforeLoad" } && KumikoType(called.ContainingType, "RouteGuard") ||
                        called is { Name: "Resolve" } && KumikoType(called.ContainingType, "NavigationGuard")) && node is InvocationExpressionSyntax guard)
                        foreach (var argument in guard.ArgumentList.Arguments)
                        {
                            var index = guard.ArgumentList.Arguments.IndexOf(argument);
                            var parameter = argument.NameColon is { } name ? called.Parameters.FirstOrDefault(p => p.Name == name.Name.Identifier.ValueText) :
                                index < called.Parameters.Length ? called.Parameters[index] : null;
                            if (parameter?.Name is "beforeLoad" or "beforeEnter")
                                analysis.Delegate(argument.Expression, model, "update");
                        }
                    if (called is not null && FreeStyle(called) || LegacySyntax(node, model))
                        analysis.Report(Hosting, node.GetLocation(), called?.Name ?? "free-style API");
                }
                if (node is AssignmentExpressionSyntax assignment && assignment.Left is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Changed" } changed &&
                    IsContext(model.GetTypeInfo(changed.Expression).Type))
                    analysis.Report(Hosting, assignment.GetLocation(), "Context.Changed");
            }
        }
    }

    static bool ViewDelegate(ExpressionSyntax expression, SemanticModel model)
    {
        if (expression is SimpleLambdaExpressionSyntax || expression is ParenthesizedLambdaExpressionSyntax { ParameterList.Parameters.Count: 1 })
            return true;
        var info = model.GetSymbolInfo(expression);
        if (info.Symbol is IMethodSymbol method)
            return method.Parameters.Length == 1 && IsContext(method.Parameters[0].Type);
        if (info.CandidateSymbols.OfType<IMethodSymbol>().Any(m => m.Parameters.Length == 1 && IsContext(m.Parameters[0].Type)))
            return true;
        return model.GetTypeInfo(expression).Type is INamedTypeSymbol { Name: "Action", TypeArguments.Length: 1 } action && IsContext(action.TypeArguments[0]);
    }

    static bool LegacySyntax(SyntaxNode node, SemanticModel model)
    {
        if (node is BaseObjectCreationExpressionSyntax { ArgumentList.Arguments.Count: > 0 } creation &&
            model.GetTypeInfo(creation).Type?.ToDisplayString() == "Kumiko.UI.Tester")
            return ViewDelegate(creation.ArgumentList.Arguments[0].Expression, model);
        if (node is not InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax member } call)
            return false;
        var type = model.GetTypeInfo(member.Expression).Type ?? model.GetSymbolInfo(member.Expression).Symbol as ITypeSymbol;
        var arguments = call.ArgumentList.Arguments;
        return type?.ToDisplayString() switch
        {
            "Kumiko.Dev" when member.Name.Identifier.ValueText == "Run" && arguments.Count > 3 => ViewDelegate(arguments[3].Expression, model),
            "Kumiko.App" when member.Name.Identifier.ValueText == "NewWindow" && arguments.Count > 1 => ViewDelegate(arguments[1].Expression, model),
            "Kumiko.UI.Context" when member.Name.Identifier.ValueText is "Checkbox" or "Toggle" or "Slider" => arguments.Any(a => !a.RefKindKeyword.IsKind(SyntaxKind.None)),
            "Kumiko.UI.Context" when member.Name.Identifier.ValueText == "TextField" && arguments.Count > 0 => model.GetTypeInfo(arguments[0].Expression).Type?.ToDisplayString() == "Kumiko.UI.TextBuffer",
            _ => false
        };
    }

    static bool FreeStyle(IMethodSymbol method) =>
        method.ContainingType.ToDisplayString() is "Kumiko.Dev" or "Kumiko.App" or "Kumiko.UI.Tester" &&
        method.Parameters.Any(p => p.Type is INamedTypeSymbol { Name: "Action", TypeArguments.Length: 1 } action && IsContext(action.TypeArguments[0])) ||
        IsContext(method.ContainingType) && method.Name is "Checkbox" or "Toggle" or "Slider" or "TextField" &&
        method.Parameters.Any(p => p.RefKind != RefKind.None || p.Type.ToDisplayString() == "Kumiko.UI.TextBuffer");

    static bool Derives(INamedTypeSymbol type, INamedTypeSymbol parent)
    {
        for (var next = type.BaseType; next is not null; next = next.BaseType)
            if (SymbolEqualityComparer.Default.Equals(next, parent))
                return true;
        return false;
    }

    static IEnumerable<INamedTypeSymbol> Types(INamespaceOrTypeSymbol parent)
    {
        foreach (var member in parent.GetMembers())
        {
            if (member is INamedTypeSymbol type)
                yield return type;
            if (member is INamespaceOrTypeSymbol nested)
                foreach (var child in Types(nested))
                    yield return child;
        }
    }
}
