using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace ObFramework.Generator.CDE
{
    /// <summary>
    /// Engine-neutral CDE source generator (T07/T09/T10/T11). It turns
    /// [ObservableProperty] fields into public properties with change notifications and
    /// [RelayCommand] methods into nested OBF command types. The generator only depends on the
    /// attribute and runtime metadata names, so engine adapters can reference the emitted View
    /// surface without a Framework source-project reference or a Roslyn AdditionalFile pipeline.
    /// </summary>
    [Generator]
    public sealed class CdeGenerator : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var observableFields = context.SyntaxProvider
                .CreateSyntaxProvider(
                    static (node, _) => IsAttributedFieldCandidate(node),
                    static (syntaxContext, cancellationToken) => GetAttributedField(syntaxContext, cancellationToken))
                .Where(static field => field != null)
                .Select(static (field, _) => field!);

            var relayMethods = context.SyntaxProvider
                .CreateSyntaxProvider(
                    static (node, _) => IsAttributedMethodCandidate(node),
                    static (syntaxContext, cancellationToken) => GetAttributedMethod(syntaxContext, cancellationToken))
                .Where(static method => method != null)
                .Select(static (method, _) => method!);

            var plan = observableFields
                .Collect()
                .Combine(relayMethods.Collect())
                .Combine(context.CompilationProvider)
                .Select(static (combined, cancellationToken) =>
                    CdePlanner.Plan(combined.Left.Left, combined.Left.Right, combined.Right, cancellationToken));

            context.RegisterSourceOutput(plan, static (productionContext, generatedPlan) =>
            {
                foreach (var diagnostic in generatedPlan.Diagnostics)
                    productionContext.ReportDiagnostic(diagnostic);
                foreach (var source in generatedPlan.Sources)
                    productionContext.AddSource(source.HintName, SourceText.From(source.Text, Encoding.UTF8));
            });
        }

        private static bool IsAttributedFieldCandidate(SyntaxNode node)
        {
            if (node is not VariableDeclaratorSyntax declarator)
                return false;
            if (declarator.Parent is not VariableDeclarationSyntax declaration)
                return false;
            return declaration.Parent is FieldDeclarationSyntax field && field.AttributeLists.Count > 0;
        }

        private static bool IsAttributedMethodCandidate(SyntaxNode node)
        {
            return node is MethodDeclarationSyntax method && method.AttributeLists.Count > 0;
        }

        private static IFieldSymbol? GetAttributedField(GeneratorSyntaxContext syntaxContext, System.Threading.CancellationToken cancellationToken)
        {
            var declarator = (VariableDeclaratorSyntax)syntaxContext.Node;
            if (syntaxContext.SemanticModel.GetDeclaredSymbol(declarator, cancellationToken) is not IFieldSymbol field)
                return null;
            return CdeSymbols.HasAttribute(field, CdeSymbols.ObservablePropertyAttribute) ? field : null;
        }

        private static IMethodSymbol? GetAttributedMethod(GeneratorSyntaxContext syntaxContext, System.Threading.CancellationToken cancellationToken)
        {
            var declaration = (MethodDeclarationSyntax)syntaxContext.Node;
            if (syntaxContext.SemanticModel.GetDeclaredSymbol(declaration, cancellationToken) is not IMethodSymbol method)
                return null;
            return CdeSymbols.HasAttribute(method, CdeSymbols.RelayCommandAttribute) ? method : null;
        }
    }
}
