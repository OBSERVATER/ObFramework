using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System.Linq;
using System.Text;

namespace ObFramework.Generator.Integrated.Save
{
    [Generator]
    public class SaveGenerator : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var compilation = context.CompilationProvider;
            var syntax = context.SyntaxProvider.ForAttributeWithMetadataName("ObFramework.Integrated.Save.CanSaveAttribute",
                // 进一步判断
                static (node,_) => node is ClassDeclarationSyntax,
                static (syntaxContext, _) =>
                {
                    var symbol = syntaxContext.TargetSymbol as INamedTypeSymbol;
                    var saveFieldAttributeSymbol = syntaxContext.SemanticModel.Compilation
                                .GetTypeByMetadataName("ObFramework.Integrated.Save.SaveFieldAttribute");
                    var members = symbol!.GetMembers().OfType<IFieldSymbol>();
                    var attr = members.Where(x => x.GetAttributes()
                            .Any(y => SymbolEqualityComparer.Default.Equals(y.AttributeClass, saveFieldAttributeSymbol)))
                            .ToArray();
                    var ns = symbol.ContainingNamespace.IsGlobalNamespace ? "" : symbol.ContainingNamespace.ToDisplayString();
                    var name = symbol.Name;
                    var key = syntaxContext.Attributes[0].ConstructorArguments[0].Value as string;
                    return (Attrs:attr,Namespace:ns,ClassName:name,Key:key);
                });
            context.RegisterSourceOutput(syntax,static (output,models) =>
            {
                var source = SaveEmitter.Generate(models.ClassName, models.Namespace, models.Attrs, models.Key!);
                var hintName = $"{models.ClassName}.Save.g.cs";
                output.AddSource(hintName, SourceText.From(source, Encoding.UTF8));
            });
        }
    }
}
