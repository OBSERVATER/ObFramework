using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ObFramework.Generator.Unity
{
    internal static class UIBindingDiagnostics
    {
        private static DiagnosticDescriptor Error(string id, string title) =>
            new DiagnosticDescriptor(id, title, "{0}", "UnityUIBinding", DiagnosticSeverity.Error, isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor UnsupportedControl =
            Error("OBFU101", "Unsupported binding control");

        public static readonly DiagnosticDescriptor MissingDeclaration =
            Error("OBFU102", "Missing View source declaration");

        public static readonly DiagnosticDescriptor UnsupportedMode =
            Error("OBFU103", "Unsupported binding mode");

        public static readonly DiagnosticDescriptor IncompatibleValue =
            Error("OBFU104", "Incompatible binding value conversion");

        public static readonly DiagnosticDescriptor DuplicateTarget =
            Error("OBFU105", "Duplicate binding target");

        public static readonly DiagnosticDescriptor MissingBindView =
            Error("OBFU106", "Missing BindView attribute");

        public static readonly DiagnosticDescriptor InvalidArgument =
            Error("OBFU107", "Invalid binding attribute argument");

        public static readonly DiagnosticDescriptor InvalidDeclaration =
            Error("OBFU108", "Invalid binding declaration");
    }

    internal enum ControlKind
    {
        None = 0,
        Button,
        Text,
        TmpText,
        InputField,
        TmpInputField,
        Toggle,
        Slider,
    }

    internal enum ValueSourceKind
    {
        None = 0,
        String,
        Bool,
        Float,
    }

    internal sealed class UIBindingSurface
    {
        public const string DataAttributeName = "ObFramework.Adapter.Unity.ViewBinding.UIBindDataAttribute";
        public const string CommandAttributeName = "ObFramework.Adapter.Unity.ViewBinding.UIBindCommandAttribute";
        public const string GeneratedBindingInterfaceName = "ObFramework.Adapter.Unity.ViewBinding.IGeneratedUIBinding";

        public INamedTypeSymbol? DataAttribute;
        public INamedTypeSymbol? CommandAttribute;
        public INamedTypeSymbol? GeneratedBindingInterface;

        public bool Available => DataAttribute != null && CommandAttribute != null && GeneratedBindingInterface != null;

        public static UIBindingSurface Resolve(Compilation compilation)
        {
            return new UIBindingSurface
            {
                DataAttribute = compilation.GetTypeByMetadataName(DataAttributeName),
                CommandAttribute = compilation.GetTypeByMetadataName(CommandAttributeName),
                GeneratedBindingInterface = compilation.GetTypeByMetadataName(GeneratedBindingInterfaceName),
            };
        }
    }

    internal sealed class ViewPropertyInfo
    {
        public string Name = string.Empty;
        public ITypeSymbol Type = null!;
        public bool Writable;
        public bool Generated;
    }

    internal sealed class ViewCommandInfo
    {
        public string TypeName = string.Empty;
        public bool Generated;
        public bool Async;
        public bool IsUICommand;
        public bool HasValue;
        public ITypeSymbol? ValueType;
        public bool HasViewConstructor;
        public bool HasParameterlessConstructor;
        public bool HasViewValueConstructor;
        public ITypeSymbol? ViewValueConstructorType;
        public bool HasValueConstructor;
        public ITypeSymbol? ValueConstructorType;
    }

    internal sealed class ViewSourceModel
    {
        private static readonly string ObservablePropertyName = "ObFramework.Integrated.UI.ObservablePropertyAttribute";
        private static readonly string RelayCommandName = "ObFramework.Integrated.UI.RelayCommandAttribute";

        public INamedTypeSymbol View = null!;
        public bool HasSource;
        public readonly Dictionary<string, ViewPropertyInfo> ObservableProperties =
            new Dictionary<string, ViewPropertyInfo>(StringComparer.Ordinal);
        public readonly Dictionary<string, ViewCommandInfo> GeneratedCommands =
            new Dictionary<string, ViewCommandInfo>(StringComparer.Ordinal);

        public static ViewSourceModel Analyze(Compilation compilation, INamedTypeSymbol view, CancellationToken cancellationToken)
        {
            var model = new ViewSourceModel { View = view, HasSource = view.DeclaringSyntaxReferences.Length > 0 };
            if (!model.HasSource)
                return model;

            var observableAttribute = compilation.GetTypeByMetadataName(ObservablePropertyName);
            var relayCommandAttribute = compilation.GetTypeByMetadataName(RelayCommandName);

            foreach (var member in view.GetMembers())
            {
                if (member is IFieldSymbol field)
                {
                    if (!HasAttribute(field, observableAttribute))
                        continue;
                    var propertyName = PropertyNameFor(field.Name);
                    if (propertyName.Length == 0)
                        continue;
                    model.ObservableProperties[propertyName] = new ViewPropertyInfo
                    {
                        Name = propertyName,
                        Type = field.Type,
                        Writable = true,
                        Generated = true,
                    };
                }
                else if (member is IMethodSymbol method && HasAttribute(method, relayCommandAttribute))
                {
                    var command = AnalyzeGeneratedCommand(method);
                    if (command != null)
                        model.GeneratedCommands[command.TypeName] = command;
                }
            }

            return model;
        }

        private static ViewCommandInfo? AnalyzeGeneratedCommand(IMethodSymbol method)
        {
            if (method.IsStatic || method.MethodKind != MethodKind.Ordinary)
                return null;

            var valueCount = 0;
            ITypeSymbol? valueType = null;
            foreach (var parameter in method.Parameters)
            {
                if (parameter.RefKind != RefKind.None || IsCancellationToken(parameter.Type))
                    continue;
                valueCount++;
                if (valueCount > 1)
                    return null;
                valueType = parameter.Type;
            }

            return new ViewCommandInfo
            {
                TypeName = CommandTypeNameFor(method.Name),
                Generated = true,
                Async = IsAsyncReturn(method.ReturnType),
                IsUICommand = true,
                HasValue = valueType != null,
                ValueType = valueType,
                HasViewConstructor = valueType == null,
                HasViewValueConstructor = valueType != null,
                ViewValueConstructorType = valueType,
            };
        }

        /// <summary>Resolves a View property for a [UIBindData] attribute argument.</summary>
        public ViewPropertyInfo? FindProperty(string name, Compilation compilation, INamedTypeSymbol controller)
        {
            if (ObservableProperties.TryGetValue(name, out var generated))
                return generated;

            foreach (var member in View.GetMembers(name))
            {
                if (member is IPropertySymbol property)
                {
                    if (property.IsStatic || !IsAccessible(compilation, property.GetMethod, controller))
                        continue;
                    var setter = property.SetMethod;
                    return new ViewPropertyInfo
                    {
                        Name = name,
                        Type = property.Type,
                        Writable = setter != null && !setter.IsInitOnly && IsAccessible(compilation, setter, controller),
                        Generated = false,
                    };
                }

                if (member is IFieldSymbol field && !field.IsStatic && !field.IsConst)
                {
                    return new ViewPropertyInfo
                    {
                        Name = name,
                        Type = field.Type,
                        Writable = true,
                        Generated = false,
                    };
                }
            }

            return null;
        }

        /// <summary>
        /// Resolves a View command for a [UIBindCommand] attribute argument. Generated commands come
        /// from the [RelayCommand] source convention; otherwise a nested hand-written command type is
        /// inspected with the convention constructor shapes.
        /// </summary>
        public ViewCommandInfo? FindCommand(INamedTypeSymbol controller, Compilation compilation, string typeName,
            List<Diagnostic> diagnostics, Location? location)
        {
            if (GeneratedCommands.TryGetValue(typeName, out var generated))
                return generated;

            var nested = View.GetTypeMembers(typeName).FirstOrDefault(type => type.TypeKind == TypeKind.Class);
            if (nested == null || !compilation.IsSymbolAccessibleWithin(nested, controller))
                return null;

            var result = AnalyzeHandWrittenCommand(nested, compilation, controller);
            if (result == null)
            {
                diagnostics.Add(Diagnostic.Create(UIBindingDiagnostics.InvalidDeclaration, location,
                    $"Hand-written command '{typeName}' must derive from ObFramework.Core.ICommand or ObFramework.Core.IAsyncCommand and expose a public convention constructor."));
                return null;
            }

            return result;
        }

        private static ViewCommandInfo? AnalyzeHandWrittenCommand(INamedTypeSymbol command, Compilation compilation,
            INamedTypeSymbol controller)
        {
            var asyncInterface = compilation.GetTypeByMetadataName("ObFramework.Core.IAsyncCommand");
            var syncInterface = compilation.GetTypeByMetadataName("ObFramework.Core.ICommand");
            var uiCommand = compilation.GetTypeByMetadataName("ObFramework.Integrated.UI.AUICommand");
            var uiAsyncCommand = compilation.GetTypeByMetadataName("ObFramework.Integrated.UI.AUIAsyncCommand");

            var isAsync = DerivesFrom(command, asyncInterface) || DerivesFrom(command, uiAsyncCommand);
            var isSync = DerivesFrom(command, syncInterface) || DerivesFrom(command, uiCommand);
            if (!isAsync && !isSync)
                return null;

            var view = command.ContainingType;
            if (view == null)
                return null;

            var info = new ViewCommandInfo
            {
                TypeName = command.Name,
                Generated = false,
                Async = isAsync && !isSync,
                IsUICommand = DerivesFrom(command, uiCommand) || DerivesFrom(command, uiAsyncCommand),
            };

            foreach (var constructor in command.InstanceConstructors)
            {
                if (constructor.IsStatic || constructor.DeclaredAccessibility != Accessibility.Public ||
                    !compilation.IsSymbolAccessibleWithin(constructor, controller))
                    continue;
                var parameters = constructor.Parameters;
                if (parameters.Any(parameter => parameter.RefKind != RefKind.None))
                    continue;

                if (parameters.Length == 0)
                {
                    info.HasParameterlessConstructor = true;
                }
                else if (parameters.Length == 1)
                {
                    if (IsViewParameter(parameters[0].Type, view))
                    {
                        info.HasViewConstructor = true;
                    }
                    else
                    {
                        info.HasValueConstructor = true;
                        info.ValueConstructorType ??= parameters[0].Type;
                    }
                }
                else if (parameters.Length == 2 && IsViewParameter(parameters[0].Type, view))
                {
                    info.HasViewValueConstructor = true;
                    info.ViewValueConstructorType ??= parameters[1].Type;
                }
            }

            if (info.HasViewValueConstructor)
                info.ValueType = info.ViewValueConstructorType;
            else if (info.HasValueConstructor)
                info.ValueType = info.ValueConstructorType;
            else
                info.ValueType = null;
            info.HasValue = info.ValueType != null;
            return info;
        }

        private static bool IsViewParameter(ITypeSymbol parameterType, INamedTypeSymbol view) =>
            Same(parameterType, view) || DerivesFrom(view, parameterType as INamedTypeSymbol);

        private static bool IsAccessible(Compilation compilation, IMethodSymbol? method, INamedTypeSymbol within) =>
            method != null && method.DeclaredAccessibility != Accessibility.Private &&
            compilation.IsSymbolAccessibleWithin(method, within);

        private static bool IsCancellationToken(ITypeSymbol type) =>
            type is INamedTypeSymbol named && named.Name == "CancellationToken" &&
            named.ContainingNamespace.ToDisplayString() == "System.Threading";

        internal static bool IsAsyncReturn(ITypeSymbol type) =>
            type is INamedTypeSymbol named &&
            (named.Name == "Task" || named.Name == "ValueTask") &&
            named.ContainingNamespace.ToDisplayString() == "System.Threading.Tasks";

        internal static string PropertyNameFor(string fieldName)
        {
            var name = fieldName ?? string.Empty;
            while (name.Length > 1 && name[0] == '_')
                name = name.Substring(1);
            if (name.StartsWith("m_", StringComparison.Ordinal) && name.Length > 2)
                name = name.Substring(2);
            if (name.Length == 0 || !char.IsLetter(name[0]))
                return string.Empty;
            return char.ToUpperInvariant(name[0]) + name.Substring(1);
        }

        internal static string CommandTypeNameFor(string methodName)
        {
            var name = methodName ?? string.Empty;
            if (name.Length > "Async".Length && name.EndsWith("Async", StringComparison.Ordinal))
                name = name.Substring(0, name.Length - "Async".Length);
            return name + "Command";
        }

        internal static bool HasAttribute(ISymbol member, INamedTypeSymbol? attribute) =>
            attribute != null && member.GetAttributes().Any(item => Same(item.AttributeClass, attribute));

        internal static bool Same(ISymbol? left, ISymbol? right) =>
            left != null && right != null && SymbolEqualityComparer.Default.Equals(left, right);

        internal static bool DerivesFrom(ITypeSymbol? type, INamedTypeSymbol? baseType)
        {
            if (type == null || baseType == null)
                return false;
            if (Same(type, baseType))
                return true;
            for (var current = type as INamedTypeSymbol; current != null; current = current.BaseType)
            {
                if (Same(current, baseType))
                    return true;
            }
            return baseType.TypeKind == TypeKind.Interface && type.AllInterfaces.Any(item => Same(item, baseType));
        }

        internal static string? ArgumentName(AttributeData attribute, int index, CancellationToken cancellationToken)
        {
            if (attribute.ConstructorArguments.Length > index)
            {
                var value = attribute.ConstructorArguments[index].Value as string;
                if (!string.IsNullOrEmpty(value))
                    return value;
            }

            if (!(attribute.ApplicationSyntaxReference?.GetSyntax(cancellationToken) is AttributeSyntax syntax) ||
                syntax.ArgumentList == null || syntax.ArgumentList.Arguments.Count <= index)
                return null;
            return NameFromExpression(syntax.ArgumentList.Arguments[index].Expression);
        }

        internal static string? NameFromExpression(ExpressionSyntax expression)
        {
            if (expression is InvocationExpressionSyntax invocation &&
                invocation.Expression is IdentifierNameSyntax identifier &&
                identifier.Identifier.Text == "nameof" &&
                invocation.ArgumentList.Arguments.Count == 1)
            {
                expression = invocation.ArgumentList.Arguments[0].Expression;
            }

            if (expression is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression))
                return literal.Token.ValueText;

            var text = expression.ToString().Trim();
            if (text.Length == 0)
                return null;
            var dot = text.LastIndexOf('.');
            if (dot >= 0 && dot + 1 < text.Length)
                text = text.Substring(dot + 1);
            return text.Length == 0 ? null : text;
        }
    }

    internal sealed class ValueConversion
    {
        public ITypeSymbol TargetType = null!;
        public bool Parsed;
        public string Expression = string.Empty;
        public string? ParseStatement;

        public string TargetTypeExpression => TargetType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        public static ValueConversion? Create(ValueSourceKind source, ITypeSymbol? target)
        {
            if (target == null || target.TypeKind == TypeKind.Error)
                return null;

            switch (source)
            {
                case ValueSourceKind.String:
                    switch (target.SpecialType)
                    {
                        case SpecialType.System_String:
                        case SpecialType.System_Object:
                            return Direct(target, "__value");
                        case SpecialType.System_Int32:
                            return Parse(target, "global::System.Int32");
                        case SpecialType.System_Int64:
                            return Parse(target, "global::System.Int64");
                        case SpecialType.System_Single:
                            return Parse(target, "global::System.Single");
                        case SpecialType.System_Double:
                            return Parse(target, "global::System.Double");
                        case SpecialType.System_Decimal:
                            return Parse(target, "global::System.Decimal");
                    }
                    return null;
                case ValueSourceKind.Bool:
                    return target.SpecialType == SpecialType.System_Boolean || target.SpecialType == SpecialType.System_Object
                        ? Direct(target, "__value")
                        : null;
                case ValueSourceKind.Float:
                    switch (target.SpecialType)
                    {
                        case SpecialType.System_Single:
                        case SpecialType.System_Double:
                        case SpecialType.System_Object:
                            return Direct(target, "__value");
                        case SpecialType.System_Int32:
                            return Direct(target, "(int)global::System.Math.Round(__value)");
                        case SpecialType.System_Int64:
                            return Direct(target, "(long)global::System.Math.Round(__value)");
                    }
                    return null;
                default:
                    return null;
            }
        }

        private static ValueConversion Direct(ITypeSymbol target, string expression) =>
            new ValueConversion { TargetType = target, Expression = expression };

        private static ValueConversion Parse(ITypeSymbol target, string parserType) =>
            new ValueConversion
            {
                TargetType = target,
                Parsed = true,
                Expression = "__converted",
                ParseStatement = "if (!" + parserType + ".TryParse(__value, out var __converted)) return;",
            };
    }

    internal sealed class DataBindingPlan
    {
        public IFieldSymbol Field = null!;
        public string FieldName = string.Empty;
        public string MemberName = string.Empty;
        public ControlKind Control;
        public string PropertyName = string.Empty;
        public ITypeSymbol PropertyType = null!;
        public bool TwoWay;
        public string? FormatLiteral;
        public ValueSourceKind Source;
        public ValueConversion? WriteConversion;
        public string RefreshMethod = string.Empty;
        public string? HandlerMethod;
    }

    internal sealed class CommandBindingPlan
    {
        public IFieldSymbol Field = null!;
        public string FieldName = string.Empty;
        public string MemberName = string.Empty;
        public ControlKind Control;
        public ValueSourceKind Source;
        public string CommandTypeName = string.Empty;
        public string CommandTypeExpression = string.Empty;
        public bool Async;
        public bool IsUICommand;
        public bool DispatchTakesView;
        public bool DispatchNeedsValue;
        public bool RefreshTakesView;
        public bool RefreshNeedsValue;
        public bool HasStoredValue;
        public bool HasCanExecute;
        public bool HasRunningCheck;
        public ValueConversion? Conversion;
        public ITypeSymbol? StoredValueType;
        public string StoredValueFieldName = string.Empty;
        public string HandlerMethod = string.Empty;
        public string RefreshMethod = string.Empty;
        public bool RefreshOnAnyCommandStateChange;

        public string StoredValueTypeExpression =>
            (StoredValueType ?? throw new InvalidOperationException("Stored value type is not set."))
            .ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    }

    internal sealed class UIBindingPlan
    {
        public INamedTypeSymbol Controller = null!;
        public INamedTypeSymbol View = null!;
        public string ViewTypeExpression = string.Empty;
        public readonly List<DataBindingPlan> Data = new List<DataBindingPlan>();
        public readonly List<CommandBindingPlan> Commands = new List<CommandBindingPlan>();
    }

    internal sealed class UIBindingPlanResult
    {
        public UIBindingPlan? Plan;
        public bool HasErrors => Diagnostics.Count > 0;
        public readonly List<Diagnostic> Diagnostics = new List<Diagnostic>();
    }

    internal static class UIBindingPlanner
    {
        private const int OneWayMode = 0;
        private const int TwoWayMode = 1;
        private static readonly string AUIViewName = "ObFramework.Integrated.UI.AUIView";

        public static UIBindingPlanResult Plan(Compilation compilation, INamedTypeSymbol controller, INamedTypeSymbol view,
            UIBindingSurface surface, CancellationToken cancellationToken)
        {
            var result = new UIBindingPlanResult();
            var fields = new List<IFieldSymbol>();
            foreach (var member in controller.GetMembers())
            {
                if (member is IFieldSymbol field && HasSurfaceAttribute(field, surface))
                    fields.Add(field);
            }

            if (fields.Count == 0)
                return result;

            var controllerLocation = fields[0].Locations.FirstOrDefault(location => location.IsInSource) ?? Location.None;
            var auiView = compilation.GetTypeByMetadataName(AUIViewName);
            if (auiView == null || !ViewSourceModel.DerivesFrom(view, auiView))
            {
                Add(result, UIBindingDiagnostics.InvalidDeclaration, controllerLocation,
                    $"Business view '{view}' must derive from '{AUIViewName}' to participate in Unity engine bindings.");
                return result;
            }

            if (surface.GeneratedBindingInterface != null &&
                controller.AllInterfaces.Any(item => ViewSourceModel.Same(item, surface.GeneratedBindingInterface)))
            {
                Add(result, UIBindingDiagnostics.InvalidDeclaration, controllerLocation,
                    $"'{controller}' already implements '{surface.GeneratedBindingInterface.Name}'; the generated engine binding cannot be added.");
                return result;
            }

            var model = ViewSourceModel.Analyze(compilation, view, cancellationToken);
            if (!model.HasSource)
            {
                Add(result, UIBindingDiagnostics.MissingDeclaration, controllerLocation,
                    $"Business view '{view}' has no source declarations in this compilation; engine bindings validate [ObservableProperty]/[RelayCommand] source conventions.");
                return result;
            }

            var plan = new UIBindingPlan
            {
                Controller = controller,
                View = view,
                ViewTypeExpression = Qualified(view),
            };

            var twoWayProperties = new HashSet<string>(StringComparer.Ordinal);
            var memberNames = new HashSet<string>(StringComparer.Ordinal);

            foreach (var field in fields)
            {
                var location = field.Locations.FirstOrDefault(item => item.IsInSource) ?? controllerLocation;
                var dataAttribute = FindAttribute(field, surface.DataAttribute);
                var commandAttribute = FindAttribute(field, surface.CommandAttribute);

                if (dataAttribute != null && commandAttribute != null)
                {
                    Add(result, UIBindingDiagnostics.DuplicateTarget, location,
                        $"Field '{controller.Name}.{field.Name}' cannot declare both [UIBindData] and [UIBindCommand].");
                    continue;
                }

                if (field.IsStatic || field.IsConst)
                {
                    Add(result, UIBindingDiagnostics.InvalidDeclaration, location,
                        $"Binding field '{controller.Name}.{field.Name}' must be an instance field.");
                    continue;
                }

                if (!(field.Type is INamedTypeSymbol))
                {
                    Add(result, UIBindingDiagnostics.UnsupportedControl, location,
                        $"Binding field '{controller.Name}.{field.Name}' has unsupported control type '{field.Type}'.");
                    continue;
                }

                var control = ResolveControl(field.Type);
                var memberName = UniqueMemberName(field.Name, memberNames);

                if (dataAttribute != null)
                {
                    var binding = BuildDataBinding(compilation, controller, model, field, dataAttribute, control,
                        memberName, result.Diagnostics, location, cancellationToken);
                    if (binding == null)
                        continue;
                    if (binding.TwoWay && !twoWayProperties.Add(binding.PropertyName))
                    {
                        Add(result, UIBindingDiagnostics.DuplicateTarget, location,
                            $"TwoWay bindings for '{view.Name}.{binding.PropertyName}' are ambiguous; only one control may write that property.");
                        continue;
                    }
                    plan.Data.Add(binding);
                }
                else if (commandAttribute != null)
                {
                    var binding = BuildCommandBinding(compilation, controller, model, field, commandAttribute, control,
                        memberName, result.Diagnostics, location, cancellationToken);
                    if (binding != null)
                        plan.Commands.Add(binding);
                }
            }

            if (!result.HasErrors)
                result.Plan = plan;
            return result;
        }

        private static DataBindingPlan? BuildDataBinding(Compilation compilation, INamedTypeSymbol controller,
            ViewSourceModel model, IFieldSymbol field, AttributeData attribute, ControlKind control, string memberName,
            List<Diagnostic> diagnostics, Location location, CancellationToken cancellationToken)
        {
            var propertyName = ViewSourceModel.ArgumentName(attribute, 0, cancellationToken);
            if (string.IsNullOrEmpty(propertyName))
            {
                Add(diagnostics, UIBindingDiagnostics.InvalidArgument, location,
                    $"[UIBindData] on '{controller.Name}.{field.Name}' needs a non-empty View property name, for example nameof(MyView.Title).");
                return null;
            }

            if (!TryReadMode(attribute, out var mode, out var modeReported))
            {
                if (modeReported)
                {
                    Add(diagnostics, UIBindingDiagnostics.InvalidArgument, location,
                        $"[UIBindData] on '{controller.Name}.{field.Name}' declares an invalid UIBindMode value.");
                }
                return null;
            }

            if (control == ControlKind.None)
            {
                Add(diagnostics, UIBindingDiagnostics.UnsupportedControl, location,
                    $"Field '{controller.Name}.{field.Name}' has unsupported control type '{field.Type}'. Supported data controls: Text, TMP_Text, InputField, TMP_InputField, Toggle, Slider.");
                return null;
            }

            if (control == ControlKind.Button)
            {
                Add(diagnostics, UIBindingDiagnostics.UnsupportedControl, location,
                    $"Field '{controller.Name}.{field.Name}' is a Button; buttons raise commands, use [UIBindCommand] instead of [UIBindData].");
                return null;
            }

            var isDisplayOnlyText = control == ControlKind.Text || control == ControlKind.TmpText;
            if (isDisplayOnlyText && mode == TwoWayMode)
            {
                Add(diagnostics, UIBindingDiagnostics.UnsupportedMode, location,
                    $"Field '{controller.Name}.{field.Name}' binds display-only '{Describe(control)}' control as TwoWay; use UIBindMode.OneWay.");
                return null;
            }

            var property = model.FindProperty(propertyName!, compilation, controller);
            if (property == null)
            {
                Add(diagnostics, UIBindingDiagnostics.MissingDeclaration, location,
                    $"Business view '{model.View.Name}' has no [ObservableProperty] source field generating property '{propertyName}' and no readable member with that name.");
                return null;
            }

            var twoWay = mode == TwoWayMode;
            if (twoWay && !property.Writable)
            {
                Add(diagnostics, UIBindingDiagnostics.InvalidDeclaration, location,
                    $"TwoWay binding target '{model.View.Name}.{propertyName}' has no accessible setter.");
                return null;
            }

            ValueConversion? writeConversion = null;
            var source = SourceFor(control);
            if (twoWay)
            {
                writeConversion = ValueConversion.Create(source, property.Type);
                if (writeConversion == null)
                {
                    Add(diagnostics, UIBindingDiagnostics.IncompatibleValue, location,
                        $"TwoWay binding target '{model.View.Name}.{propertyName}' of type '{property.Type}' cannot be written from '{Describe(control)}' value '{Describe(source)}'.");
                    return null;
                }
            }

            if (control == ControlKind.Toggle && property.Type.SpecialType != SpecialType.System_Boolean)
            {
                Add(diagnostics, UIBindingDiagnostics.IncompatibleValue, location,
                    $"Toggle binding target '{model.View.Name}.{propertyName}' must be bool, but is '{property.Type}'.");
                return null;
            }

            if (control == ControlKind.Slider && property.Type.SpecialType != SpecialType.System_Single &&
                property.Type.SpecialType != SpecialType.System_Int32 && property.Type.SpecialType != SpecialType.System_Double &&
                property.Type.SpecialType != SpecialType.System_Int64)
            {
                Add(diagnostics, UIBindingDiagnostics.IncompatibleValue, location,
                    $"Slider binding target '{model.View.Name}.{propertyName}' must be float or int, but is '{property.Type}'.");
                return null;
            }

            var format = ReadFormat(attribute);
            return new DataBindingPlan
            {
                Field = field,
                FieldName = EscapeIdentifier(field.Name),
                MemberName = memberName,
                Control = control,
                PropertyName = propertyName!,
                PropertyType = property.Type,
                TwoWay = twoWay,
                FormatLiteral = format == null ? null : SymbolDisplay.FormatLiteral(format, quote: true),
                Source = source,
                WriteConversion = writeConversion,
                RefreshMethod = "__RefreshData" + memberName,
                HandlerMethod = twoWay && source != ValueSourceKind.None ? "__OnData" + memberName + "ValueChanged" : null,
            };
        }

        private static CommandBindingPlan? BuildCommandBinding(Compilation compilation, INamedTypeSymbol controller,
            ViewSourceModel model, IFieldSymbol field, AttributeData attribute, ControlKind control, string memberName,
            List<Diagnostic> diagnostics, Location location, CancellationToken cancellationToken)
        {
            var typeName = ViewSourceModel.ArgumentName(attribute, 0, cancellationToken);
            if (string.IsNullOrEmpty(typeName) || typeName!.Length <= "Command".Length ||
                !typeName.EndsWith("Command", StringComparison.Ordinal))
            {
                Add(diagnostics, UIBindingDiagnostics.InvalidArgument, location,
                    $"[UIBindCommand] on '{controller.Name}.{field.Name}' needs a nested command type name ending in 'Command', for example nameof(MyView.SubmitCommand).");
                return null;
            }

            if (control == ControlKind.None || control == ControlKind.Text || control == ControlKind.TmpText)
            {
                Add(diagnostics, UIBindingDiagnostics.UnsupportedControl, location,
                    $"Field '{controller.Name}.{field.Name}' of type '{field.Type}' cannot raise command events. Supported command controls: Button, InputField, TMP_InputField, Toggle, Slider.");
                return null;
            }

            var source = SourceFor(control);
            var command = model.FindCommand(controller, compilation, typeName!, diagnostics, location);
            if (command == null)
            {
                Add(diagnostics, UIBindingDiagnostics.MissingDeclaration, location,
                    $"Business view '{model.View.Name}' declares neither a [RelayCommand] method generating '{typeName}' nor a nested hand-written command type with that name.");
                return null;
            }

            var plan = new CommandBindingPlan
            {
                Field = field,
                FieldName = EscapeIdentifier(field.Name),
                MemberName = memberName,
                Control = control,
                Source = source,
                CommandTypeName = typeName!,
                CommandTypeExpression = Qualified(model.View) + "." + typeName,
                Async = command.Async,
                IsUICommand = command.IsUICommand,
                HasCanExecute = command.IsUICommand,
                HasRunningCheck = command.IsUICommand && command.Async,
                HandlerMethod = "__OnCommand" + memberName,
                RefreshMethod = "__RefreshCommand" + memberName,
                RefreshOnAnyCommandStateChange = !command.Generated && command.IsUICommand,
            };

            if (command.Generated)
            {
                if (source == ValueSourceKind.None && command.HasValue)
                {
                    Add(diagnostics, UIBindingDiagnostics.IncompatibleValue, location,
                        $"Button-bound command '{model.View.Name}.{typeName}' takes a value parameter, but a button click has no event value.");
                    return null;
                }

                if (command.HasValue)
                {
                    var conversion = ValueConversion.Create(source, command.ValueType);
                    if (conversion == null)
                    {
                        Add(diagnostics, UIBindingDiagnostics.IncompatibleValue, location,
                            $"Command '{model.View.Name}.{typeName}' parameter of type '{command.ValueType}' cannot accept a '{Describe(source)}' event value from '{Describe(control)}'.");
                        return null;
                    }
                    plan.Conversion = conversion;
                    plan.DispatchTakesView = true;
                    plan.DispatchNeedsValue = true;
                    plan.RefreshTakesView = true;
                    plan.RefreshNeedsValue = true;
                    plan.HasStoredValue = true;
                    plan.StoredValueType = conversion.TargetType;
                    plan.StoredValueFieldName = "__value" + memberName;
                }
                else
                {
                    plan.DispatchTakesView = true;
                    plan.DispatchNeedsValue = false;
                    plan.RefreshTakesView = true;
                    plan.RefreshNeedsValue = false;
                }
                return plan;
            }

            return BuildHandWrittenCommandBinding(plan, command, diagnostics, location);
        }

        private static CommandBindingPlan? BuildHandWrittenCommandBinding(CommandBindingPlan plan, ViewCommandInfo command,
            List<Diagnostic> diagnostics, Location location)
        {
            var source = plan.Source;
            ValueConversion? conversion = null;

            if (source == ValueSourceKind.None)
            {
                if (command.HasViewConstructor)
                {
                    plan.DispatchTakesView = true;
                }
                else if (command.HasParameterlessConstructor)
                {
                    plan.DispatchTakesView = false;
                }
                else
                {
                    Add(diagnostics, UIBindingDiagnostics.IncompatibleValue, location,
                        $"Hand-written command '{plan.CommandTypeName}' has no public constructor compatible with a button click; expected (View) or () .");
                    return null;
                }
            }
            else
            {
                if (command.HasViewValueConstructor)
                {
                    conversion = ValueConversion.Create(source, command.ViewValueConstructorType);
                }

                if (conversion != null)
                {
                    plan.DispatchTakesView = true;
                    plan.DispatchNeedsValue = true;
                    plan.RefreshTakesView = true;
                    plan.RefreshNeedsValue = true;
                    plan.HasStoredValue = true;
                    plan.StoredValueType = conversion.TargetType;
                    plan.StoredValueFieldName = "__value" + plan.MemberName;
                }
                else if (command.HasValueConstructor && (conversion = ValueConversion.Create(source, command.ValueConstructorType)) != null)
                {
                    plan.DispatchNeedsValue = true;
                    plan.RefreshNeedsValue = true;
                    plan.HasStoredValue = true;
                    plan.StoredValueType = conversion.TargetType;
                    plan.StoredValueFieldName = "__value" + plan.MemberName;
                }
                else if (command.HasViewConstructor)
                {
                    plan.DispatchTakesView = true;
                }
                else if (command.HasParameterlessConstructor)
                {
                    plan.DispatchNeedsValue = false;
                }
                else
                {
                    Add(diagnostics, UIBindingDiagnostics.IncompatibleValue, location,
                        $"Hand-written command '{plan.CommandTypeName}' has no public constructor accepting a '{Describe(source)}' event value.");
                    return null;
                }
            }

            plan.Conversion = conversion;

            if (!plan.IsUICommand)
                return plan;

            // Interactable refresh needs a command instance; prefer a constructor that needs no value.
            if (command.HasViewConstructor || command.HasParameterlessConstructor)
            {
                plan.RefreshNeedsValue = false;
                plan.RefreshTakesView = command.HasViewConstructor;
                return plan;
            }

            if (plan.HasStoredValue && plan.DispatchNeedsValue)
                return plan;

            Add(diagnostics, UIBindingDiagnostics.InvalidDeclaration, location,
                $"Hand-written command '{plan.CommandTypeName}' exposes CanExecute state but cannot be constructed without an event value, so interactable refresh is not possible.");
            return null;
        }

        private static ValueSourceKind SourceFor(ControlKind control)
        {
            switch (control)
            {
                case ControlKind.InputField:
                case ControlKind.TmpInputField:
                    return ValueSourceKind.String;
                case ControlKind.Toggle:
                    return ValueSourceKind.Bool;
                case ControlKind.Slider:
                    return ValueSourceKind.Float;
                default:
                    return ValueSourceKind.None;
            }
        }

        private static bool HasSurfaceAttribute(IFieldSymbol field, UIBindingSurface surface) =>
            ViewSourceModel.HasAttribute(field, surface.DataAttribute) ||
            ViewSourceModel.HasAttribute(field, surface.CommandAttribute);

        private static AttributeData? FindAttribute(ISymbol member, INamedTypeSymbol? attribute)
        {
            if (attribute == null)
                return null;
            foreach (var item in member.GetAttributes())
            {
                if (ViewSourceModel.Same(item.AttributeClass, attribute))
                    return item;
            }
            return null;
        }

        private static bool TryReadMode(AttributeData attribute, out int mode, out bool reported)
        {
            mode = OneWayMode;
            reported = false;
            foreach (var named in attribute.NamedArguments)
            {
                if (named.Key != "Mode")
                    continue;
                if (!(named.Value.Value is int raw) || (raw != OneWayMode && raw != TwoWayMode))
                {
                    reported = true;
                    return false;
                }
                mode = raw;
            }
            return true;
        }

        private static string? ReadFormat(AttributeData attribute)
        {
            foreach (var named in attribute.NamedArguments)
            {
                if (named.Key == "Format")
                    return named.Value.Value as string;
            }
            return null;
        }

        private static ControlKind ResolveControl(ITypeSymbol type)
        {
            for (var current = type as INamedTypeSymbol; current != null; current = current.BaseType)
            {
                var kind = ControlKindFor(FullName(current));
                if (kind != ControlKind.None)
                    return kind;
            }
            return ControlKind.None;
        }

        private static ControlKind ControlKindFor(string fullName)
        {
            switch (fullName)
            {
                case "UnityEngine.UI.Button":
                    return ControlKind.Button;
                case "UnityEngine.UI.Text":
                    return ControlKind.Text;
                case "UnityEngine.UI.InputField":
                    return ControlKind.InputField;
                case "UnityEngine.UI.Toggle":
                    return ControlKind.Toggle;
                case "UnityEngine.UI.Slider":
                    return ControlKind.Slider;
                case "TMPro.TMP_Text":
                    return ControlKind.TmpText;
                case "TMPro.TMP_InputField":
                    return ControlKind.TmpInputField;
                default:
                    return ControlKind.None;
            }
        }

        private static string FullName(INamedTypeSymbol type)
        {
            var name = type.Name;
            for (var current = type.ContainingType; current != null; current = current.ContainingType)
                name = current.Name + "." + name;
            var space = type.ContainingNamespace;
            if (space == null || space.IsGlobalNamespace)
                return name;
            return space.ToDisplayString() + "." + name;
        }

        private static string UniqueMemberName(string fieldName, HashSet<string> used)
        {
            var name = fieldName ?? string.Empty;
            while (name.Length > 1 && name[0] == '_')
                name = name.Substring(1);
            if (name.StartsWith("m_", StringComparison.Ordinal) && name.Length > 2)
                name = name.Substring(2);
            if (name.Length == 0)
                name = "Binding";
            if (!char.IsLetter(name[0]))
                name = "Value" + name;
            var candidate = name;
            var index = 2;
            while (!used.Add(candidate))
                candidate = name + index++;
            return candidate;
        }

        internal static string EscapeIdentifier(string name)
        {
            switch (SyntaxFacts.GetKeywordKind(name))
            {
                case SyntaxKind.None:
                    return name;
                default:
                    return "@" + name;
            }
        }

        internal static string Qualified(ITypeSymbol type) =>
            type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        internal static string Describe(ControlKind control)
        {
            switch (control)
            {
                case ControlKind.Button: return "Button";
                case ControlKind.Text: return "Text";
                case ControlKind.TmpText: return "TMP_Text";
                case ControlKind.InputField: return "InputField";
                case ControlKind.TmpInputField: return "TMP_InputField";
                case ControlKind.Toggle: return "Toggle";
                case ControlKind.Slider: return "Slider";
                default: return "unknown";
            }
        }

        internal static string Describe(ValueSourceKind source)
        {
            switch (source)
            {
                case ValueSourceKind.String: return "string";
                case ValueSourceKind.Bool: return "bool";
                case ValueSourceKind.Float: return "float";
                default: return "none";
            }
        }

        private static void Add(List<Diagnostic> diagnostics, DiagnosticDescriptor descriptor, Location? location,
            string message) => diagnostics.Add(Diagnostic.Create(descriptor, location, message));

        private static void Add(UIBindingPlanResult result, DiagnosticDescriptor descriptor, Location? location,
            string message) => Add(result.Diagnostics, descriptor, location, message);
    }
}
