using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace ObFramework.Generator.Godot
{
    [Generator]
    public sealed class ViewBindingGenerator : IIncrementalGenerator
    {
        private const string BindingNamespace = "ObFramework.Adapter.Godot.ViewBinding";
        private const string CoreNamespace = "ObFramework.Core.";

        private static readonly DiagnosticDescriptor InvalidController = Error("OBFV001", "Invalid view controller");
        private static readonly DiagnosticDescriptor InvalidContract = Error("OBFV002", "Invalid view contract");
        private static readonly DiagnosticDescriptor InvalidView = Error("OBFV003", "Invalid business view");
        private static readonly DiagnosticDescriptor InvalidParameter = Error("OBFV004", "Unsupported view constructor parameter");

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var controllers = context.SyntaxProvider
                .ForAttributeWithMetadataName(
                    $"{BindingNamespace}.BindViewAttribute",
                    static (node, _) => node is ClassDeclarationSyntax,
                    static (item, ct) => Analyze(item, ct))
                .WithTrackingName("ViewBindingAnalysis");

            context.RegisterSourceOutput(controllers, static (output, result) => EmitResult(output, result));
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
                !DerivesFrom(controller, compilation.GetTypeByMetadataName("Godot.Node")) ||
                controller.DeclaringSyntaxReferences.Any(reference =>
                    reference.GetSyntax(ct) is not ClassDeclarationSyntax declaration ||
                    !declaration.Modifiers.Any(SyntaxKind.PartialKeyword)))
            {
                Report(diagnostics, InvalidController, location,
                    $"'{controller}' must be a concrete, top-level, non-generic partial Node.");
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
                Report(diagnostics, InvalidContract, location,
                    $"'{controller}' must implement the accessible interface '{contract}' declared as its view contract.");
                return Finish();
            }

            if (view is not { TypeKind: TypeKind.Class } || view.IsAbstract || view.IsUnboundGenericType ||
                !compilation.IsSymbolAccessibleWithin(view, controller) ||
                DerivesFrom(view, compilation.GetTypeByMetadataName("Godot.GodotObject")))
            {
                Report(diagnostics, InvalidView, location,
                    $"'{view}' must be a concrete, accessible C# class outside the GodotObject hierarchy.");
                return Finish();
            }

            var constructors = view.InstanceConstructors
                .Where(ctor => ctor.DeclaredAccessibility == Accessibility.Public)
                .ToArray();

            if (constructors.Length != 1 || constructors[0].IsVararg)
            {
                Report(diagnostics, InvalidView, location,
                    $"'{view}' must have exactly one public constructor.");
                return Finish();
            }

            var generatedInterface = compilation.GetTypeByMetadataName(
                $"{BindingNamespace}.IGeneratedViewBinding`1");

            if (controller.AllInterfaces.Any(type => Same(type.OriginalDefinition, generatedInterface)) ||
                HasBoundBase(controller, binding.AttributeClass))
            {
                Report(diagnostics, InvalidController, location,
                    $"'{controller}' already implements or inherits a generated view binding.");
                return Finish();
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

            return Finish(BuildSource(controller, view, arguments));

            GenerationResult Finish(string? source = null) =>
                new GenerationResult(name, source, diagnostics.ToImmutableArray());
        }

        private static void EmitResult(SourceProductionContext output, GenerationResult result)
        {
            if (result.Source != null)
                output.AddSource($"{result.Name}.ViewBinding.g.cs",
                    SourceText.From(result.Source, Encoding.UTF8));

            foreach (var diagnostic in result.Diagnostics)
                output.ReportDiagnostic(diagnostic.ToDiagnostic());
        }

        private static string? GetArgument(
            Compilation compilation, IParameterSymbol parameter,
            INamedTypeSymbol contract, INamedTypeSymbol controller)
        {
            if (parameter.RefKind != RefKind.None ||
                parameter.Type is not INamedTypeSymbol type ||
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

        private static string BuildSource(
            INamedTypeSymbol controller, INamedTypeSymbol view,
            List<string> arguments)
        {
            var source = new StringBuilder("// <auto-generated/>\n");
            var space = controller.ContainingNamespace;

            if (!space.IsGlobalNamespace)
                source.Append("namespace ").Append(space.ToDisplayString()).AppendLine(" {");

            string viewType = Qualified(view);
            string generated = $"global::{BindingNamespace}.IGeneratedViewBinding<{viewType}>";

            source.Append("partial class @").Append(controller.Name)
                .Append(" : ").Append(generated).AppendLine(" {");

            source.Append("    global::System.Type ").Append(generated)
                .Append(".ControllerType => typeof(")
                .Append(Qualified(controller)).AppendLine(");");

            source.Append("    ").Append(viewType).Append(' ').Append(generated)
                .Append(".CreateView(global::").Append(CoreNamespace)
                .Append("IArchitecture architecture) => new ")
                .Append(viewType).Append('(')
                .Append(string.Join(", ", arguments)).AppendLine(");");

            source.AppendLine("}");

            if (!space.IsGlobalNamespace)
                source.AppendLine("}");

            return source.ToString();
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

        private static void Report(
            List<DiagnosticResult> diagnostics, DiagnosticDescriptor descriptor,
            Location? location, string message) =>
            diagnostics.Add(new DiagnosticResult(descriptor, location, message));

        private static bool Same(ISymbol? left, ISymbol? right) =>
            left != null && right != null && SymbolEqualityComparer.Default.Equals(left, right);

        private static string Qualified(ITypeSymbol type) =>
            type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        private static DiagnosticDescriptor Error(string id, string title) =>
            new(id, title, "{0}", "ViewBinding", DiagnosticSeverity.Error, isEnabledByDefault: true);

        private sealed class GenerationResult : IEquatable<GenerationResult>
        {
            public readonly string Name;
            public readonly string? Source;
            public readonly ImmutableArray<DiagnosticResult> Diagnostics;

            public GenerationResult(string name, string? source, ImmutableArray<DiagnosticResult> diagnostics)
            {
                Name = name;
                Source = source;
                Diagnostics = diagnostics;
            }

            public bool Equals(GenerationResult? other) =>
                other != null && Name == other.Name &&
                Source == other.Source &&
                Diagnostics.SequenceEqual(other.Diagnostics);

            public override bool Equals(object? other) => Equals(other as GenerationResult);

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = Name.GetHashCode();
                    hash = hash * 31 + (Source?.GetHashCode() ?? 0);

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

            public DiagnosticResult(
                DiagnosticDescriptor descriptor, Location? location, string message)
            {
                _descriptor = descriptor;
                _message = message;

                if (location != null && location.IsInSource)
                {
                    _path = location.SourceTree?.FilePath;
                    _span = location.SourceSpan;
                    _lineSpan = location.GetLineSpan().Span;
                }
                else
                {
                    _path = null;
                    _span = default;
                    _lineSpan = default;
                }
            }

            public Diagnostic ToDiagnostic() =>
                Diagnostic.Create(_descriptor,
                    _path == null ? Location.None : Location.Create(_path, _span, _lineSpan),
                    _message);

            public bool Equals(DiagnosticResult other) =>
                _descriptor.Id == other._descriptor.Id &&
                _message == other._message &&
                _path == other._path &&
                _span.Equals(other._span) &&
                _lineSpan.Equals(other._lineSpan);

            public override bool Equals(object? other) =>
                other is DiagnosticResult result && Equals(result);

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
    }
}