using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
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
            var controllers = context.SyntaxProvider
                .ForAttributeWithMetadataName($"{BindingNamespace}.BindViewAttribute",
                static (node, _) => node is ClassDeclarationSyntax,
                static (item, ct) => Analyze(item, ct)).WithTrackingName("ViewBindingAnalysis");
            context.RegisterSourceOutput(controllers, static (output, result) => EmitResult(output, result));

            var data = context.SyntaxProvider.ForAttributeWithMetadataName(
                UIBindingSurface.DataAttributeName,
                static (node, _) => node is VariableDeclaratorSyntax,
                static (item, ct) => FindMissingBindView(item, ct));
            var commands = context.SyntaxProvider.ForAttributeWithMetadataName(
                UIBindingSurface.CommandAttributeName,
                static (node, _) => node is VariableDeclaratorSyntax,
                static (item, ct) => FindMissingBindView(item, ct));
            context.RegisterSourceOutput(data, static (output, diagnostic) => Report(output, diagnostic));
            context.RegisterSourceOutput(commands, static (output, diagnostic) => Report(output, diagnostic));
        }

        private static GenerationResult Analyze(GeneratorAttributeSyntaxContext item, CancellationToken ct)
        {
            var controller = (INamedTypeSymbol)item.TargetSymbol;
            var compilation = item.SemanticModel.Compilation;
            var binding = item.Attributes[0];
            var location = binding.ApplicationSyntaxReference?.GetSyntax(ct).GetLocation();
            var diagnostics = new List<DiagnosticResult>();
            string name = controller.ToDisplayString();

            if (controller.ContainingType != null || controller.Arity != 0 || controller.IsAbstract ||
                !DerivesFrom(controller, compilation.GetTypeByMetadataName("UnityEngine.MonoBehaviour")) ||
                controller.DeclaringSyntaxReferences.Any(reference =>
                    reference.GetSyntax(ct) is not ClassDeclarationSyntax declaration ||
                    !declaration.Modifiers.Any(SyntaxKind.PartialKeyword)))
            {
                Report(diagnostics, InvalidController, location, $"'{controller}' must be a concrete, top-level, non-generic partial MonoBehaviour.");
                return Finish();
            }

            if (binding.ConstructorArguments.Length != 2)
                return Finish();

            var view = binding.ConstructorArguments[0].Value as INamedTypeSymbol;
            var contract = binding.ConstructorArguments[1].Value as INamedTypeSymbol;
            if (contract is not { TypeKind: TypeKind.Interface } || contract.IsUnboundGenericType ||
                !compilation.IsSymbolAccessibleWithin(contract, controller) ||
                !controller.AllInterfaces.Any(type => Same(type, contract)))
            {
                Report(diagnostics, InvalidContract, location, $"'{controller}' must implement the accessible interface '{contract}' declared as its view contract.");
                return Finish();
            }

            if (view is not { TypeKind: TypeKind.Class } || view.IsAbstract || view.IsUnboundGenericType ||
                !compilation.IsSymbolAccessibleWithin(view, controller) ||
                DerivesFrom(view, compilation.GetTypeByMetadataName("UnityEngine.Object")))
            {
                Report(diagnostics, InvalidView, location, $"'{view}' must be a concrete, accessible C# class outside the UnityEngine.Object hierarchy.");
                return Finish();
            }

            var constructors = view.InstanceConstructors.Where(ctor => ctor.DeclaredAccessibility == Accessibility.Public).ToArray();
            if (constructors.Length != 1 || constructors[0].IsVararg)
            {
                Report(diagnostics, InvalidView, location, $"'{view}' must have exactly one public constructor.");
                return Finish();
            }

            var generatedInterface = compilation.GetTypeByMetadataName($"{BindingNamespace}.IGeneratedViewBinding");
            if (controller.AllInterfaces.Any(type => Same(type, generatedInterface)) ||
                HasBoundBase(controller, binding.AttributeClass))
            {
                Report(diagnostics, InvalidController, location, $"'{controller}' already implements or inherits a generated view binding.");
                return Finish();
            }

            foreach (string callback in ReservedCallbacks)
            {
                for (var type = controller; type != null; type = type.BaseType)
                {
                    var member = type.GetMembers(callback).FirstOrDefault();
                    if (member == null)
                        continue;
                    var memberLocation = member.Locations.FirstOrDefault(item => item.IsInSource);
                    Report(diagnostics, ReservedCallback, memberLocation ?? location,
                        $"'{controller}' reserves Unity callback '{callback}', already declared on '{type}'.");
                    return Finish();
                }
            }

            var arguments = new List<string>();
            foreach (var parameter in constructors[0].Parameters)
            {
                string? expression = GetArgument(compilation, parameter, contract, controller);
                if (expression == null)
                {
                    Report(diagnostics, InvalidParameter, location,
                        $"'{view}' parameter '{parameter.Name}' ({parameter.Type}) needs the declared view contract, IArchitecture, an unambiguous System/Model type, or a concrete Plugin type requested by a controller implementing ICanGetPlugin.");
                    return Finish();
                }
                arguments.Add(expression);
            }

            UIBindingPlan? plan = null;
            var surface = UIBindingSurface.Resolve(compilation);
            if (surface.Available)
            {
                var result = UIBindingPlanner.Plan(compilation, controller, view, surface, ct);
                foreach (var diagnostic in result.Diagnostics)
                    diagnostics.Add(DiagnosticResult.From(diagnostic));
                plan = result.Plan;
            }

            return Finish(BuildSource(controller, view, arguments, plan != null),
                plan != null ? UIBindingEmitter.Build(plan) : null);

            GenerationResult Finish(string? source = null, string? uiSource = null) =>
                new GenerationResult(name, source, uiSource, diagnostics.ToImmutableArray());
        }

        private static void EmitResult(SourceProductionContext output, GenerationResult result)
        {
            if (result.Source != null)
                output.AddSource($"{result.Name}.ViewBinding.g.cs", SourceText.From(result.Source, Encoding.UTF8));
            if (result.UISource != null)
                output.AddSource($"{result.Name}.UIBinding.g.cs", SourceText.From(result.UISource, Encoding.UTF8));
            foreach (var diagnostic in result.Diagnostics)
                output.ReportDiagnostic(diagnostic.ToDiagnostic());
        }

        private static DiagnosticResult? FindMissingBindView(GeneratorAttributeSyntaxContext item, CancellationToken ct)
        {
            if (item.TargetSymbol is not IFieldSymbol field)
                return null;
            var type = field.ContainingType;
            var compilation = item.SemanticModel.Compilation;
            if (!UIBindingSurface.Resolve(compilation).Available)
                return null;
            if (type.GetAttributes().Any(attribute =>
                    Same(attribute.AttributeClass, compilation.GetTypeByMetadataName(
                        $"{BindingNamespace}.BindViewAttribute"))))
                return null;

            var dataAttribute = compilation.GetTypeByMetadataName(UIBindingSurface.DataAttributeName);
            var commandAttribute = compilation.GetTypeByMetadataName(UIBindingSurface.CommandAttributeName);
            foreach (var member in type.GetMembers().OfType<IFieldSymbol>())
            {
                var first = member.GetAttributes().FirstOrDefault(attribute =>
                    Same(attribute.AttributeClass, dataAttribute) || Same(attribute.AttributeClass, commandAttribute));
                if (first == null)
                    continue;
                if (!Same(member, field) || !item.Attributes.Any(attribute => Same(attribute.AttributeClass, first.AttributeClass)))
                    return null;
                var location = first.ApplicationSyntaxReference?.GetSyntax(ct).GetLocation();
                return DiagnosticResult.Create(UIBindingDiagnostics.MissingBindView, location,
                    $"'{type}' declares [UIBindData]/[UIBindCommand] fields but has no [BindView(typeof(View), typeof(Contract))] attribute, so no engine binding is generated.");
            }
            return null;
        }

        private static void Report(SourceProductionContext output, DiagnosticResult? diagnostic)
        {
            if (diagnostic.HasValue)
                output.ReportDiagnostic(diagnostic.Value.ToDiagnostic());
        }

        private static void Report(List<DiagnosticResult> diagnostics, DiagnosticDescriptor descriptor,
            Location? location, string message) =>
            diagnostics.Add(DiagnosticResult.Create(descriptor, location, message));

        private static string? GetArgument(Compilation compilation, IParameterSymbol parameter,
            INamedTypeSymbol contract, INamedTypeSymbol controller)
        {
            if (parameter.RefKind != RefKind.None || parameter.Type is not INamedTypeSymbol type ||
                (type.TypeKind != TypeKind.Class && type.TypeKind != TypeKind.Interface))
                return null;
            if (Same(type, contract))
                return $"({Qualified(contract)})this";
            if (Same(type, compilation.GetTypeByMetadataName($"{CoreNamespace}IArchitecture")))
                return "architecture";

            bool system = Implements("ISystem");
            bool model = Implements("IModel");
            bool plugin = Implements("IPlugin");
            if ((system ? 1 : 0) + (model ? 1 : 0) + (plugin ? 1 : 0) != 1)
                return null;
            if (plugin && (type.TypeKind != TypeKind.Class || type.IsAbstract))
                return null;

            if (plugin)
            {
                var capability = compilation.GetTypeByMetadataName($"{CoreNamespace}ICanGetPlugin");
                if (capability == null || !controller.AllInterfaces.Any(item => Same(item, capability)))
                    return null;
                return $"global::ObFramework.Extension.ICanExtension.GetPlugin<{Qualified(type)}>(this)";
            }

            string method = system ? "GetSystem" : "GetModel";
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
            for (var current = type; current != null; current = current.BaseType)
                if (Same(current, baseType))
                    return true;
            return false;
        }

        private static string BuildSource(INamedTypeSymbol controller, INamedTypeSymbol view,
            List<string> arguments, bool uiBindings)
        {
            var source = new StringBuilder("// <auto-generated/>\n");
            var space = controller.ContainingNamespace;
            if (!space.IsGlobalNamespace)
                source.Append("namespace ").Append(space.ToDisplayString()).AppendLine(" {");

            string generated = $"global::{BindingNamespace}.IGeneratedViewBinding";
            string callbacks = $"global::{BindingNamespace}.GeneratedViewBinding";
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
                foreach (string callback in ReservedCallbacks)
                    source.Append("    private void ").Append(callback).Append("() => ").Append(callbacks)
                        .Append('.').Append(callback).AppendLine("(this);");
            }
            source.AppendLine("}");
            if (!space.IsGlobalNamespace)
                source.AppendLine("}");

            return source.ToString();
        }

        private sealed class GenerationResult : IEquatable<GenerationResult>
        {
            public readonly string Name;
            public readonly string? Source;
            public readonly string? UISource;
            public readonly ImmutableArray<DiagnosticResult> Diagnostics;

            public GenerationResult(string name, string? source, string? uiSource, ImmutableArray<DiagnosticResult> diagnostics)
            {
                Name = name;
                Source = source;
                UISource = uiSource;
                Diagnostics = diagnostics;
            }

            public bool Equals(GenerationResult? other) =>
                other != null && Name == other.Name && Source == other.Source &&
                UISource == other.UISource && Diagnostics.SequenceEqual(other.Diagnostics);

            public override bool Equals(object? other) => Equals(other as GenerationResult);
            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = Name.GetHashCode();
                    hash = hash * 31 + (Source?.GetHashCode() ?? 0);
                    hash = hash * 31 + (UISource?.GetHashCode() ?? 0);
                    foreach (var diagnostic in Diagnostics)
                        hash = hash * 31 + diagnostic.GetHashCode();
                    return hash;
                }
            }
        }

        private readonly struct DiagnosticResult : IEquatable<DiagnosticResult>
        {
            private readonly DiagnosticDescriptor _descriptor;
            private readonly string _message;
            private readonly string? _path;
            private readonly TextSpan _span;
            private readonly LinePositionSpan _lineSpan;

            private DiagnosticResult(DiagnosticDescriptor descriptor, string message, Location? location)
            {
                _descriptor = descriptor;
                _message = message;
                if (location != null && location.IsInSource)
                {
                    var span = location.GetLineSpan();
                    _path = span.Path;
                    _span = location.SourceSpan;
                    _lineSpan = span.Span;
                }
                else
                {
                    _path = null;
                    _span = default;
                    _lineSpan = default;
                }
            }

            public static DiagnosticResult Create(DiagnosticDescriptor descriptor, Location? location, string message) =>
                new DiagnosticResult(descriptor, message, location);

            public static DiagnosticResult From(Diagnostic diagnostic) =>
                new DiagnosticResult(diagnostic.Descriptor, diagnostic.GetMessage(), diagnostic.Location);

            public Diagnostic ToDiagnostic() => Diagnostic.Create(_descriptor,
                _path == null ? Location.None : Location.Create(_path, _span, _lineSpan), _message);

            public bool Equals(DiagnosticResult other) =>
                _descriptor.Id == other._descriptor.Id && _message == other._message &&
                _path == other._path && _span.Equals(other._span) && _lineSpan.Equals(other._lineSpan);

            public override bool Equals(object? other) => other is DiagnosticResult otherResult && Equals(otherResult);
            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = _descriptor.Id.GetHashCode();
                    hash = hash * 31 + _message.GetHashCode();
                    hash = hash * 31 + (_path?.GetHashCode() ?? 0);
                    hash = hash * 31 + _span.GetHashCode();
                    return hash * 31 + _lineSpan.GetHashCode();
                }
            }
        }

        private static bool Same(ISymbol? left, ISymbol? right) =>
            left != null && right != null && SymbolEqualityComparer.Default.Equals(left, right);

        private static string Qualified(ITypeSymbol type) =>
            type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        private static DiagnosticDescriptor Error(string id, string title) =>
            new(id, title, "{0}", "ViewBinding", DiagnosticSeverity.Error, isEnabledByDefault: true);

    }
}