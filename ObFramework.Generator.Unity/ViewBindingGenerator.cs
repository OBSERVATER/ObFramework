using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace ObFramework.Generator.Unity
{
    [Generator]
    public sealed class ViewBindingGenerator : IIncrementalGenerator
    {
        private const string BindingNamespace = "ObFramework.Adapter.Unity.ViewBinding";
        private const string CoreNamespace = "ObFramework.Core.";
        private static readonly string[] ReservedCallbacks = { "OnEnable", "OnDisable", "OnDestroy" };
        private static readonly DiagnosticDescriptor InvalidController = Error("OBFV001", "Invalid view controller");
        private static readonly DiagnosticDescriptor InvalidContract = Error("OBFV002", "Invalid view contract");
        private static readonly DiagnosticDescriptor InvalidView = Error("OBFV003", "Invalid business view");
        private static readonly DiagnosticDescriptor InvalidParameter = Error("OBFV004", "Unsupported view constructor parameter");
        private static readonly DiagnosticDescriptor ReservedCallback = Error("OBFV005", "Reserved Unity callback");

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            // Only classes that actually declare attributes are candidates for [BindView]; the
            // interface generator then validates [UIBindData]/[UIBindCommand] declarations on types
            // that carry attributed members. Both providers are syntax-guided and cacheable.
            var controllers = context.SyntaxProvider.CreateSyntaxProvider(
                    static (node, _) => node is ClassDeclarationSyntax declaration && declaration.AttributeLists.Count > 0,
                    static (syntaxContext, _) =>
                        syntaxContext.SemanticModel.GetDeclaredSymbol(syntaxContext.Node) as INamedTypeSymbol)
                .Where(static symbol => symbol != null)
                .Collect();

            var typesWithAttributedMembers = context.SyntaxProvider.CreateSyntaxProvider(
                    static (node, _) => node is MemberDeclarationSyntax member &&
                                        member.AttributeLists.Count > 0 &&
                                        member.Parent is TypeDeclarationSyntax,
                    static (syntaxContext, _) =>
                        syntaxContext.SemanticModel.GetDeclaredSymbol(syntaxContext.Node.Parent!) as INamedTypeSymbol)
                .Where(static symbol => symbol != null)
                .Collect();

            var combined = controllers.Combine(typesWithAttributedMembers).Combine(context.CompilationProvider);
            context.RegisterSourceOutput(combined, static (output, value) =>
            {
                var declarations = value.Item1.Item1;
                var attributedTypes = value.Item1.Item2;
                Run(output, value.Item2, declarations, attributedTypes);
            });
        }

        private static void Run(SourceProductionContext output, Compilation compilation,
            ImmutableArray<INamedTypeSymbol?> declarations, ImmutableArray<INamedTypeSymbol?> attributedTypes)
        {
            var attribute = compilation.GetTypeByMetadataName(BindingNamespace + ".BindViewAttribute");
            if (attribute == null)
                return;

            var context = new GenerationContext(output, compilation);
            var surface = UIBindingSurface.Resolve(compilation);
            var seen = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
            var controllers = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
            foreach (var controller in declarations)
            {
                if (controller == null || !seen.Add(controller))
                    continue;

                var binding = controller.GetAttributes().FirstOrDefault(item => Same(item.AttributeClass, attribute));
                if (binding != null)
                {
                    controllers.Add(controller);
                    Generate(context, controller, binding, surface);
                }
            }

            if (!surface.Available)
                return;

            var candidateTypes = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
            foreach (var type in attributedTypes)
            {
                if (type != null)
                    candidateTypes.Add(type);
            }
            ReportMissingBindView(context, candidateTypes, attribute, controllers);
        }

        private static void Generate(GenerationContext context, INamedTypeSymbol controller, AttributeData binding,
            UIBindingSurface surface)
        {
            var compilation = context.Compilation;
            var location = binding.ApplicationSyntaxReference?.GetSyntax(context.CancellationToken).GetLocation();
            if (controller.ContainingType != null || controller.Arity != 0 || controller.IsAbstract ||
                !DerivesFrom(controller, compilation.GetTypeByMetadataName("UnityEngine.MonoBehaviour")) ||
                controller.DeclaringSyntaxReferences.Any(reference =>
                    !(reference.GetSyntax(context.CancellationToken) is ClassDeclarationSyntax declaration) ||
                    !declaration.Modifiers.Any(SyntaxKind.PartialKeyword)))
            {
                Report(context, InvalidController, location, $"'{controller}' must be a concrete, top-level, non-generic partial MonoBehaviour.");
                return;
            }

            if (binding.ConstructorArguments.Length != 2)
                return;

            var view = binding.ConstructorArguments[0].Value as INamedTypeSymbol;
            var contract = binding.ConstructorArguments[1].Value as INamedTypeSymbol;
            if (contract == null || contract.TypeKind != TypeKind.Interface || contract.IsUnboundGenericType ||
                !compilation.IsSymbolAccessibleWithin(contract, controller) ||
                !controller.AllInterfaces.Any(type => Same(type, contract)))
            {
                Report(context, InvalidContract, location, $"'{controller}' must implement the accessible interface '{contract}' declared as its view contract.");
                return;
            }

            if (view == null || view.TypeKind != TypeKind.Class || view.IsAbstract || view.IsUnboundGenericType ||
                !compilation.IsSymbolAccessibleWithin(view, controller) ||
                DerivesFrom(view, compilation.GetTypeByMetadataName("UnityEngine.Object")))
            {
                Report(context, InvalidView, location, $"'{view}' must be a concrete, accessible C# class outside the UnityEngine.Object hierarchy.");
                return;
            }

            var constructors = view.InstanceConstructors.Where(ctor => ctor.DeclaredAccessibility == Accessibility.Public).ToArray();
            if (constructors.Length != 1 || constructors[0].IsVararg)
            {
                Report(context, InvalidView, location, $"'{view}' must have exactly one public constructor.");
                return;
            }

            var generatedInterface = compilation.GetTypeByMetadataName(BindingNamespace + ".IGeneratedViewBinding");
            if (controller.AllInterfaces.Any(type => Same(type, generatedInterface)) ||
                HasBoundBase(controller, binding.AttributeClass))
            {
                Report(context, InvalidController, location, $"'{controller}' already implements or inherits a generated view binding.");
                return;
            }

            foreach (var callback in ReservedCallbacks)
            {
                for (INamedTypeSymbol? type = controller; type != null; type = type.BaseType)
                {
                    var member = type.GetMembers(callback).FirstOrDefault();
                    if (member == null)
                        continue;
                    var memberLocation = member.Locations.FirstOrDefault(item => item.IsInSource);
                    Report(context, ReservedCallback, memberLocation ?? location,
                        $"'{controller}' reserves Unity callback '{callback}', already declared on '{type}'.");
                    return;
                }
            }

            var arguments = new List<string>();
            foreach (var parameter in constructors[0].Parameters)
            {
                var expression = GetArgument(compilation, parameter, contract);
                if (expression == null)
                {
                    Report(context, InvalidParameter, location,
                        $"'{view}' parameter '{parameter.Name}' ({parameter.Type}) needs the declared view contract, IArchitecture, an unambiguous System/Model type, or a concrete Plugin type.");
                    return;
                }
                arguments.Add(expression);
            }

            UIBindingPlan? plan = null;
            if (surface.Available)
            {
                var result = UIBindingPlanner.Plan(compilation, controller, view, surface, context.CancellationToken);
                foreach (var diagnostic in result.Diagnostics)
                    context.ReportDiagnostic(diagnostic);
                plan = result.Plan;
            }

            Emit(context, controller, view, arguments, plan != null);
            if (plan != null)
                UIBindingEmitter.Emit(context, plan);
        }

        private static string? GetArgument(Compilation compilation, IParameterSymbol parameter, INamedTypeSymbol contract)
        {
            if (parameter.RefKind != RefKind.None || !(parameter.Type is INamedTypeSymbol type) ||
                (type.TypeKind != TypeKind.Class && type.TypeKind != TypeKind.Interface))
                return null;
            if (Same(type, contract))
                return $"({Qualified(contract)})this";
            if (Same(type, compilation.GetTypeByMetadataName(CoreNamespace + "IArchitecture")))
                return "architecture";

            var system = Implements("ISystem");
            var model = Implements("IModel");
            var plugin = Implements("IPlugin");
            if ((system ? 1 : 0) + (model ? 1 : 0) + (plugin ? 1 : 0) != 1)
                return null;
            if (plugin && (type.TypeKind != TypeKind.Class || type.IsAbstract))
                return null;

            var method = system ? "GetSystem" : model ? "GetModel" : "GetPlugin";
            return $"architecture.{method}<{Qualified(type)}>()";

            bool Implements(string name)
            {
                var marker = compilation.GetTypeByMetadataName(CoreNamespace + name);
                return marker != null && type.AllInterfaces.Any(item => Same(item, marker));
            }
        }

        private static bool HasBoundBase(INamedTypeSymbol controller, INamedTypeSymbol? attribute)
        {
            for (var type = controller.BaseType; type != null; type = type.BaseType)
                if (type.GetAttributes().Any(item => Same(item.AttributeClass, attribute)))
                    return true;
            return false;
        }

        private static bool DerivesFrom(INamedTypeSymbol type, INamedTypeSymbol? baseType)
        {
            for (INamedTypeSymbol? current = type; current != null; current = current.BaseType)
                if (Same(current, baseType))
                    return true;
            return false;
        }

        private static void Emit(GenerationContext context, INamedTypeSymbol controller, INamedTypeSymbol view,
            List<string> arguments, bool uiBindings)
        {
            var source = new StringBuilder("// <auto-generated/>\n");
            var space = controller.ContainingNamespace;
            if (!space.IsGlobalNamespace)
                source.Append("namespace ").Append(space.ToDisplayString()).AppendLine(" {");

            var generated = "global::" + BindingNamespace + ".IGeneratedViewBinding";
            var callbacks = "global::" + BindingNamespace + ".GeneratedViewBinding";
            source.Append("partial class @").Append(controller.Name).Append(" : ").Append(generated).AppendLine(" {");
            source.Append("    global::System.Type ").Append(generated).Append(".ControllerType => typeof(")
                .Append(Qualified(controller)).AppendLine(");");
            if (uiBindings)
            {
                source.Append("    object ").Append(generated).Append(".CreateView(global::").Append(CoreNamespace)
                    .AppendLine("IArchitecture architecture)");
                source.AppendLine("    {");
                source.Append("        var view = new ").Append(Qualified(view)).Append('(')
                    .Append(string.Join(", ", arguments)).AppendLine(");");
                source.AppendLine("        __UIBindingCreated(view);");
                source.AppendLine("        return view;");
                source.AppendLine("    }");
                source.Append("    partial void __UIBindingCreated(").Append(Qualified(view)).AppendLine(" view);");
                source.AppendLine();
                source.AppendLine("    private void OnEnable()");
                source.AppendLine("    {");
                source.Append("        ").Append(callbacks).AppendLine(".OnEnable(this);");
                source.AppendLine("        __OnUIBindingEnabled();");
                source.AppendLine("    }");
                source.AppendLine("    partial void __OnUIBindingEnabled();");
                source.AppendLine();
                source.AppendLine("    private void OnDisable()");
                source.AppendLine("    {");
                source.AppendLine("        __OnUIBindingDisabled();");
                source.Append("        ").Append(callbacks).AppendLine(".OnDisable(this);");
                source.AppendLine("    }");
                source.AppendLine("    partial void __OnUIBindingDisabled();");
                source.AppendLine();
                source.AppendLine("    private void OnDestroy()");
                source.AppendLine("    {");
                source.AppendLine("        __OnUIBindingDestroyed();");
                source.Append("        ").Append(callbacks).AppendLine(".OnDestroy(this);");
                source.AppendLine("    }");
                source.AppendLine("    partial void __OnUIBindingDestroyed();");
            }
            else
            {
                source.Append("    object ").Append(generated).Append(".CreateView(global::").Append(CoreNamespace)
                    .Append("IArchitecture architecture) => new ").Append(Qualified(view)).Append('(')
                    .Append(string.Join(", ", arguments)).AppendLine(");");
                foreach (var callback in ReservedCallbacks)
                    source.Append("    private void ").Append(callback).Append("() => ").Append(callbacks)
                        .Append('.').Append(callback).AppendLine("(this);");
            }
            source.AppendLine("}");
            if (!space.IsGlobalNamespace)
                source.AppendLine("}");

            context.AddSource(controller.ToDisplayString() + ".ViewBinding.g.cs", SourceText.From(source.ToString(), Encoding.UTF8));
        }

        private static void ReportMissingBindView(GenerationContext context, IEnumerable<INamedTypeSymbol> candidateTypes,
            INamedTypeSymbol bindViewAttribute, HashSet<INamedTypeSymbol> controllers)
        {
            var dataAttributes = new[]
            {
                BindingNamespace + ".UIBindDataAttribute",
                BindingNamespace + ".UIBindCommandAttribute"
            };

            foreach (var type in candidateTypes.OrderBy(static item => item.ToDisplayString(), StringComparer.Ordinal))
            {
                if (controllers.Contains(type) ||
                    type.GetAttributes().Any(item => Same(item.AttributeClass, bindViewAttribute)))
                    continue;

                Location? location = null;
                foreach (var field in type.GetMembers().OfType<IFieldSymbol>())
                {
                    var dataAttribute = field.GetAttributes().FirstOrDefault(attribute =>
                        attribute.AttributeClass != null &&
                        dataAttributes.Contains(attribute.AttributeClass.ToDisplayString(), StringComparer.Ordinal));
                    if (dataAttribute == null)
                        continue;

                    location = dataAttribute.ApplicationSyntaxReference?
                        .GetSyntax(context.CancellationToken)
                        .GetLocation();
                    break;
                }

                if (location == null)
                    continue;

                context.ReportDiagnostic(Diagnostic.Create(UIBindingDiagnostics.MissingBindView, location,
                    $"'{type}' declares [UIBindData]/[UIBindCommand] fields but has no [BindView(typeof(View), typeof(Contract))] attribute, so no engine binding is generated."));
            }
        }

        private static bool Same(ISymbol? left, ISymbol? right) =>
            left != null && right != null && SymbolEqualityComparer.Default.Equals(left, right);

        private static string Qualified(ITypeSymbol type) =>
            type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        private static DiagnosticDescriptor Error(string id, string title) =>
            new(id, title, "{0}", "ViewBinding", DiagnosticSeverity.Error, isEnabledByDefault: true);

        private static void Report(GenerationContext context, DiagnosticDescriptor descriptor, Location? location, string message) =>
            context.ReportDiagnostic(Diagnostic.Create(descriptor, location, message));
    }
}
