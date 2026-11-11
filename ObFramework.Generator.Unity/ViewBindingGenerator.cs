using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace ObFramework.Generator.Unity
{
    [Generator]
    public sealed class ViewBindingGenerator : ISourceGenerator
    {
        private const string BindingNamespace = "ObFramework.Adapter.Unity.ViewBinding";
        private const string CoreNamespace = "ObFramework.Core.";
        private static readonly string[] ReservedCallbacks = { "OnEnable", "OnDisable", "OnDestroy" };
        private static readonly DiagnosticDescriptor InvalidController = Error("OBFV001", "Invalid view controller");
        private static readonly DiagnosticDescriptor InvalidContract = Error("OBFV002", "Invalid view contract");
        private static readonly DiagnosticDescriptor InvalidView = Error("OBFV003", "Invalid business view");
        private static readonly DiagnosticDescriptor InvalidParameter = Error("OBFV004", "Unsupported view constructor parameter");
        private static readonly DiagnosticDescriptor ReservedCallback = Error("OBFV005", "Reserved Unity callback");

        public void Initialize(GeneratorInitializationContext context) =>
            context.RegisterForSyntaxNotifications(() => new Candidates());

        public void Execute(GeneratorExecutionContext context)
        {
            if (!(context.SyntaxReceiver is Candidates candidates))
                return;

            var attribute = context.Compilation.GetTypeByMetadataName(BindingNamespace + ".BindViewAttribute");
            if (attribute == null)
                return;

            var surface = UIBindingSurface.Resolve(context.Compilation);
            var seen = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
            var controllers = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
            foreach (var declaration in candidates.Classes)
            {
                var model = context.Compilation.GetSemanticModel(declaration.SyntaxTree);
                if (!(model.GetDeclaredSymbol(declaration, context.CancellationToken) is INamedTypeSymbol controller) ||
                    !seen.Add(controller))
                    continue;

                var binding = controller.GetAttributes().FirstOrDefault(item => Same(item.AttributeClass, attribute));
                if (binding != null)
                {
                    controllers.Add(controller);
                    Generate(context, controller, binding, surface);
                }
            }

            if (surface.Available)
                ReportMissingBindView(context, candidates, attribute, controllers);
        }

        private static void Generate(GeneratorExecutionContext context, INamedTypeSymbol controller, AttributeData binding,
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

        private static void Emit(GeneratorExecutionContext context, INamedTypeSymbol controller, INamedTypeSymbol view,
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

        private static void ReportMissingBindView(GeneratorExecutionContext context, Candidates candidates,
            INamedTypeSymbol bindViewAttribute, HashSet<INamedTypeSymbol> controllers)
        {
            var locations = new Dictionary<INamedTypeSymbol, Location>(SymbolEqualityComparer.Default);
            foreach (var declaration in candidates.Types)
            {
                if (!(declaration is ClassDeclarationSyntax))
                    continue;
                var location = FindBindingFieldLocation(declaration);
                if (location == null)
                    continue;
                var model = context.Compilation.GetSemanticModel(declaration.SyntaxTree);
                if (!(model.GetDeclaredSymbol(declaration, context.CancellationToken) is INamedTypeSymbol type))
                    continue;
                if (!locations.ContainsKey(type))
                    locations.Add(type, location);
            }

            foreach (var pair in locations)
            {
                var type = pair.Key;
                if (controllers.Contains(type) ||
                    type.GetAttributes().Any(item => Same(item.AttributeClass, bindViewAttribute)))
                    continue;
                context.ReportDiagnostic(Diagnostic.Create(UIBindingDiagnostics.MissingBindView, pair.Value,
                    $"'{type}' declares [UIBindData]/[UIBindCommand] fields but has no [BindView(typeof(View), typeof(Contract))] attribute, so no engine binding is generated."));
            }
        }

        private static Location? FindBindingFieldLocation(TypeDeclarationSyntax declaration)
        {
            foreach (var member in declaration.Members)
            {
                if (!(member is FieldDeclarationSyntax field))
                    continue;
                foreach (var list in field.AttributeLists)
                {
                    foreach (var attribute in list.Attributes)
                    {
                        var name = AttributeSimpleName(attribute.Name);
                        if (name == "UIBindData" || name == "UIBindDataAttribute" ||
                            name == "UIBindCommand" || name == "UIBindCommandAttribute")
                            return attribute.GetLocation();
                    }
                }
            }
            return null;
        }

        private static string AttributeSimpleName(NameSyntax name)
        {
            if (name is QualifiedNameSyntax qualified)
                return AttributeSimpleName(qualified.Right);
            if (name is AliasQualifiedNameSyntax alias)
                return AttributeSimpleName(alias.Name);
            return name is IdentifierNameSyntax identifier ? identifier.Identifier.Text : name.ToString();
        }

        private static bool Same(ISymbol? left, ISymbol? right) =>
            left != null && right != null && SymbolEqualityComparer.Default.Equals(left, right);

        private static string Qualified(ITypeSymbol type) =>
            type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        private static DiagnosticDescriptor Error(string id, string title) =>
            new(id, title, "{0}", "ViewBinding", DiagnosticSeverity.Error, isEnabledByDefault: true);

        private static void Report(GeneratorExecutionContext context, DiagnosticDescriptor descriptor, Location? location, string message) =>
            context.ReportDiagnostic(Diagnostic.Create(descriptor, location, message));

        private sealed class Candidates : ISyntaxReceiver
        {
            public List<TypeDeclarationSyntax> Classes { get; } = new();
            public List<TypeDeclarationSyntax> Types { get; } = new();

            public void OnVisitSyntaxNode(SyntaxNode syntaxNode)
            {
                if (!(syntaxNode is TypeDeclarationSyntax declaration))
                    return;
                Types.Add(declaration);
                if (declaration.AttributeLists.Count > 0)
                    Classes.Add(declaration);
            }
        }
    }
}
