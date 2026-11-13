using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Kumiko.Analyzers;
/// <summary>Checks view authoring mistakes at the call site without analyzing unrelated APIs.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ViewAnalyzer : DiagnosticAnalyzer
{
    static DiagnosticDescriptor Rule(
        string id,
        string title,
        string message
    ) => new(id, title, message, "Kumiko", DiagnosticSeverity.Error, isEnabledByDefault: true);
    static readonly DiagnosticDescriptor Scope = Rule("KUM001", "Dispose layout scopes", "Use using for {0}, or return the scope to its caller");
    static readonly DiagnosticDescriptor Identity = Rule("KUM002", "Key repeated elements", "Give {0} a stable key when constructing elements in a loop");
    static readonly DiagnosticDescriptor Thread = Rule("KUM003", "Dispatch UI work", "Call {0} on the UI thread through App.Dispatch");
    static readonly DiagnosticDescriptor Animation = Rule(
        "KUM004",
        "Use node animation",
        "Use a node's animation channels and Motion presets instead of a computed Animate ID"
    );
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Scope, Identity, Thread, Animation];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(Analyze, SyntaxKind.InvocationExpression);
    }

    static void Analyze(SyntaxNodeAnalysisContext context)
    {
        var call = (InvocationExpressionSyntax)context.Node;
        if (context.SemanticModel.GetSymbolInfo(call, context.CancellationToken).Symbol is not IMethodSymbol method)
            return;
        var type = method.ContainingType.ToDisplayString();
        bool typed = method.ContainingType.Name == "Ui" && method.ContainingNamespace.ToDisplayString() == "Kumiko.UI";
        bool ui = typed || type is "Kumiko.UI.Context" or "Kumiko.UI.Div" or "Kumiko.Window" ||
            type == "Kumiko.App" && method.Name is not ("Dispatch" or "Schedule");
        bool retained = method.ReturnType.ToDisplayString() == "Kumiko.UI.RetainedScope" ||
            method.ReturnType is INamedTypeSymbol scopeType && scopeType.Name == "NativeStackScope" && scopeType.ContainingNamespace.ToDisplayString() == "Kumiko.iOS";
        bool nativePage = method.ContainingType.Name == "NativeStackScope" && method.ReturnType.ToDisplayString() == "Kumiko.UI.Div";
        if (!ui && !retained && !nativePage)
            return;
        if ((retained || nativePage) && !Owned(call, context.SemanticModel))
            context.ReportDiagnostic(Diagnostic.Create(Scope, call.GetLocation(), method.Name));
        if (typed && method.Name == "Form" && !Owned(call, context.SemanticModel))
            context.ReportDiagnostic(Diagnostic.Create(Scope, call.GetLocation(), method.Name));
        if (type == "Kumiko.UI.Context" && method.ReturnType.ToDisplayString() == "Kumiko.UI.Div" && !Owned(call, context.SemanticModel))
            context.ReportDiagnostic(Diagnostic.Create(Scope, call.GetLocation(), method.Name));
        if ((typed || type == "Kumiko.UI.Context") && Elements.Contains(method.Name) && !Keyed(call, method) && InUnkeyedLoop(call, context.SemanticModel))
            context.ReportDiagnostic(Diagnostic.Create(Identity, call.GetLocation(), method.Name));
        if (InWorker(call, context.SemanticModel))
            context.ReportDiagnostic(Diagnostic.Create(Thread, call.GetLocation(), method.Name));
        if (context.ContainingSymbol?.Name != "AnimateNode" && type == "Kumiko.UI.Context" && method.Name == "Animate" && call.ArgumentList.Arguments.FirstOrDefault() is { } id && !context.SemanticModel.GetConstantValue(id.Expression)
            .HasValue)
            context.ReportDiagnostic(Diagnostic.Create(Animation, id.GetLocation()));
    }

    static readonly ImmutableHashSet<string> Elements = ImmutableHashSet.Create(
        "Div",
        "RetainedScope",
        "Card",
        "Text",
        "RichText",
        "Heading",
        "Label",
        "Icon",
        "Image",
        "Spacer",
        "Divider",
        "Button",
        "Form",
        "Submit",
        "Checkbox",
        "Toggle",
        "Slider",
        "Progress",
        "Segmented",
        "Badge",
        "Kbd",
        "TextField"
    );
    static bool ScopeType(ITypeSymbol? type) => type?.ToDisplayString() is "Kumiko.UI.Div" or "Kumiko.UI.RetainedScope" ||
        type is INamedTypeSymbol named && named.Name == "NativeStackScope" && named.ContainingNamespace.ToDisplayString() == "Kumiko.iOS";
    static bool Owned(InvocationExpressionSyntax call, SemanticModel model)
    {
        SyntaxNode outer = call;
        while (outer.Parent is MemberAccessExpressionSyntax or InvocationExpressionSyntax or ParenthesizedExpressionSyntax)
            outer = outer.Parent;
        if (outer.Parent is ReturnStatementSyntax or ArrowExpressionClauseSyntax)
            return ScopeType(model.GetTypeInfo(outer).Type);
        if (outer.Ancestors().OfType<UsingStatementSyntax>().Any(u =>
            u.Expression?.Span.Contains(call.Span) == true || u.Declaration?.Span.Contains(call.Span) == true))
            return true;
        var local = outer.Ancestors().OfType<LocalDeclarationStatementSyntax>().FirstOrDefault();
        if (local is { UsingKeyword.RawKind: not 0 })
            return true;
        if (local?.Declaration.Variables.FirstOrDefault() is not { } variable)
            return false;
        var owner = local.Ancestors().FirstOrDefault(n => n is MethodDeclarationSyntax or LocalFunctionStatementSyntax);
        var returnType = owner switch
        {
            MethodDeclarationSyntax method => model.GetDeclaredSymbol(method)?.ReturnType,
            LocalFunctionStatementSyntax function => model.GetDeclaredSymbol(function)?.ReturnType,
            _ => null
        };
        return ScopeType(returnType) && owner!.DescendantNodes().OfType<ReturnStatementSyntax>()
            .Any(r => r.Expression?.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>()
                .Any(name => name.Identifier.ValueText == variable.Identifier.ValueText) == true);
    }
    static bool Keyed(InvocationExpressionSyntax call, IMethodSymbol method)
    {
        var parameter = method.Parameters.FirstOrDefault(p => p.Name == "key");
        if (parameter is null)
            return false;
        return call.ArgumentList.Arguments.Any(a => a.NameColon?.Name.Identifier.ValueText == "key" || a.NameColon is null && call.ArgumentList.Arguments.IndexOf(a) == parameter.Ordinal);
    }

    static bool HasKey(SyntaxNode node, SemanticModel model) => node.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>()
        .Any(c => model.GetSymbolInfo(c).Symbol is IMethodSymbol method && method.ReturnType.ToDisplayString() is "Kumiko.UI.Div" or "Kumiko.UI.RetainedScope" && Keyed(c, method));
    static bool InUnkeyedLoop(InvocationExpressionSyntax call, SemanticModel model)
    {
        foreach (var ancestor in call.Ancestors())
        {
            if (ancestor is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax)
                return false;
            if (ancestor is UsingStatementSyntax use && (use.Expression is { } expression && HasKey(expression, model) || use.Declaration is { } declaration && HasKey(declaration, model)))
                return false;
            if (ancestor is BlockSyntax block)
            {
                var statement = call.AncestorsAndSelf().OfType<StatementSyntax>().FirstOrDefault(s => s.Parent == block);
                if (statement is not null && block.Statements.TakeWhile(s => s != statement)
                    .OfType<LocalDeclarationStatementSyntax>()
                    .Any(s => s.UsingKeyword.RawKind != 0 && HasKey(s, model)))
                    return false;
            }

            if (ancestor is ForStatementSyntax or ForEachStatementSyntax or ForEachVariableStatementSyntax or WhileStatementSyntax or DoStatementSyntax)
                return true;
        }

        return false;
    }

    static bool InWorker(SyntaxNode node, SemanticModel model)
    {
        foreach (var ancestor in node.Ancestors())
        {
            if (ancestor is InvocationExpressionSyntax invocation && model.GetSymbolInfo(invocation).Symbol is IMethodSymbol method)
            {
                var type = method.ContainingType.ToDisplayString();
                if (type == "Kumiko.App" && method.Name is "Dispatch" or "Schedule")
                    return false;
                if (type is "System.Threading.Tasks.Task" or "System.Threading.Tasks.TaskFactory" && method.Name is "Run" or "StartNew" || type == "System.Threading.ThreadPool" && method.Name.Contains("Queue", StringComparison.Ordinal))
                    return true;
            }

            if (ancestor is ObjectCreationExpressionSyntax creation && model.GetTypeInfo(creation)
                .Type?.ToDisplayString() == "System.Threading.Thread")
                return true;
        }

        return false;
    }
}
