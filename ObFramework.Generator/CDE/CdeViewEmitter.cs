using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ObFramework.Generator.CDE
{
    /// <summary>
    /// Emits the engine-neutral generated surface of one business View: public observable
    /// properties over their single backing field and nested <see cref="AUICommand"/> /
    /// <see cref="AUIAsyncCommand"/> subclasses for [RelayCommand] methods. Generated code only uses
    /// public/protected runtime API and fully qualified framework type names.
    /// </summary>
    internal sealed class CdeViewEmitter
    {
        private readonly Compilation _compilation;
        private readonly INamedTypeSymbol _view;
        private readonly string _viewTypeName;
        private readonly List<Diagnostic> _diagnostics = new List<Diagnostic>();
        private readonly HashSet<string> _generatedMemberNames = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _propertyOwners = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, IMethodSymbol> _commandOwners = new Dictionary<string, IMethodSymbol>(StringComparer.Ordinal);

        public CdeViewEmitter(Compilation compilation, INamedTypeSymbol view)
        {
            _compilation = compilation;
            _view = view;
            _viewTypeName = CdeSymbols.DisplayType(view);
        }

        public ImmutableArray<Diagnostic> Diagnostics
        {
            get { return _diagnostics.ToImmutableArray(); }
        }

        public string? Emit(ImmutableArray<IFieldSymbol> fields, ImmutableArray<IMethodSymbol> methods)
        {
            var properties = new List<ObservablePropertyModel>();
            foreach (var field in fields)
            {
                var model = AnalyzeField(field);
                if (model != null)
                    properties.Add(model);
            }

            var commands = new List<RelayCommandModel>();
            foreach (var method in methods)
            {
                var model = AnalyzeMethod(method);
                if (model != null)
                    commands.Add(model);
            }

            if (properties.Count == 0 && commands.Count == 0)
                return null;
            return Render(properties, commands);
        }

        #region Observable properties

        private ObservablePropertyModel? AnalyzeField(IFieldSymbol field)
        {
            var location = CdeSymbols.GetLocation(field);
            if (field.IsConst)
            {
                Report(CdeDiagnostics.ObservableFieldConst, location, field.Name);
                return null;
            }
            if (field.IsStatic)
            {
                Report(CdeDiagnostics.ObservableFieldStatic, location, field.Name);
                return null;
            }
            if (field.IsReadOnly)
            {
                Report(CdeDiagnostics.ObservableFieldReadOnly, location, field.Name);
                return null;
            }
            if (field.Type.TypeKind == TypeKind.Pointer ||
                field.Type.TypeKind == TypeKind.FunctionPointer ||
                field.Type.SpecialType == SpecialType.System_Void ||
                field.Type.IsRefLikeType)
            {
                Report(CdeDiagnostics.ObservableFieldUnsupportedType, location, field.Name, field.Type.ToDisplayString());
                return null;
            }

            var propertyName = CdeSymbols.GetGeneratedPropertyName(field);
            if (propertyName == null)
            {
                Report(CdeDiagnostics.ObservableFieldNameInvalid, location, field.Name);
                return null;
            }
            var eventName = propertyName + "Changed";

            if (_propertyOwners.TryGetValue(propertyName, out var firstOwner))
            {
                Report(CdeDiagnostics.DuplicateObservableProperty, location, firstOwner, field.Name, propertyName);
                return null;
            }
            if (ConflictsWithMember(propertyName) || _generatedMemberNames.Contains(propertyName))
            {
                Report(CdeDiagnostics.ObservableMemberCollision, location, field.Name, propertyName);
                return null;
            }
            if (ConflictsWithMember(eventName) || _generatedMemberNames.Contains(eventName))
            {
                Report(CdeDiagnostics.ObservableMemberCollision, location, field.Name, eventName);
                return null;
            }

            _propertyOwners.Add(propertyName, field.Name);
            _generatedMemberNames.Add(propertyName);
            _generatedMemberNames.Add(eventName);

            var notifications = AnalyzeNotifications(field, location);
            return new ObservablePropertyModel(
                field,
                propertyName,
                eventName,
                CdeSymbols.DisplayType(field.Type),
                notifications.PropertyTargets,
                notifications.CommandTargets);
        }

        private ObservableNotifications AnalyzeNotifications(IFieldSymbol field, Location fallbackLocation)
        {
            var propertyTargets = new List<string>();
            var commandTargets = new List<string>();
            foreach (var attribute in field.GetAttributes())
            {
                var attributeClass = attribute.AttributeClass;
                if (attributeClass == null)
                    continue;
                var attributeName = attributeClass.ToDisplayString();
                if (string.Equals(attributeName, CdeSymbols.NotifyPropertyChangedForAttribute, StringComparison.Ordinal))
                {
                    var target = CdeSymbols.GetStringArgument(attribute, 0);
                    if (string.IsNullOrEmpty(target))
                        Report(CdeDiagnostics.NotifyTargetInvalid, CdeSymbols.GetAttributeLocation(attribute, fallbackLocation), field.Name, "NotifyPropertyChangedFor");
                    else if (!propertyTargets.Contains(target))
                        propertyTargets.Add(target);
                }
                else if (string.Equals(attributeName, CdeSymbols.NotifyCanExecuteChangedForAttribute, StringComparison.Ordinal))
                {
                    var target = CdeSymbols.GetStringArgument(attribute, 0);
                    if (string.IsNullOrEmpty(target))
                        Report(CdeDiagnostics.NotifyTargetInvalid, CdeSymbols.GetAttributeLocation(attribute, fallbackLocation), field.Name, "NotifyCanExecuteChangedFor");
                    else if (!commandTargets.Contains(target))
                        commandTargets.Add(target);
                }
            }
            return new ObservableNotifications(propertyTargets, commandTargets);
        }

        private bool ConflictsWithMember(string memberName)
        {
            if (string.Equals(memberName, _view.Name, StringComparison.Ordinal))
                return true;
            return _view.GetMembers(memberName).Length > 0;
        }

        #endregion

        #region Relay commands

        private RelayCommandModel? AnalyzeMethod(IMethodSymbol method)
        {
            var location = CdeSymbols.GetLocation(method);
            if (method.MethodKind != MethodKind.Ordinary)
            {
                Report(CdeDiagnostics.RelayCommandKindUnsupported, location, method.Name);
                return null;
            }
            if (method.IsStatic)
            {
                Report(CdeDiagnostics.RelayCommandStatic, location, method.Name);
                return null;
            }
            if (method.IsGenericMethod)
            {
                Report(CdeDiagnostics.RelayCommandGeneric, location, method.Name);
                return null;
            }

            var returnKind = ClassifyReturn(method.ReturnType);
            if (returnKind == RelayReturnKind.Unsupported)
            {
                Report(CdeDiagnostics.RelayCommandReturnTypeUnsupported, location, method.Name, method.ReturnType.ToDisplayString());
                return null;
            }
            var isAsync = returnKind != RelayReturnKind.Void;

            if (!TryAnalyzeParameters(method, isAsync, out var valueParameter, out var hasCancellationToken, out var reason))
            {
                Report(CdeDiagnostics.RelayCommandParameterUnsupported, location, method.Name, reason);
                return null;
            }

            var commandName = CdeSymbols.GetGeneratedCommandName(method);
            if (_commandOwners.TryGetValue(commandName, out var existingMethod))
            {
                Report(CdeDiagnostics.RelayCommandNameCollision, location, existingMethod.Name, method.Name, commandName);
                return null;
            }
            if (ConflictsWithMember(commandName) || _generatedMemberNames.Contains(commandName))
            {
                Report(CdeDiagnostics.GeneratedCommandCollision, location, method.Name, commandName);
                return null;
            }

            var attribute = CdeSymbols.FindAttribute(method, CdeSymbols.RelayCommandAttribute);
            var canExecute = ResolveCanExecute(method, attribute, valueParameter?.Type, location);
            if (canExecute == CanExecuteResolution.Failed)
                return null;

            var allowConcurrent = CdeSymbols.GetNamedBoolArgument(attribute, "AllowConcurrentExecutions") ?? false;
            var cancelOnClose = CdeSymbols.GetNamedBoolArgument(attribute, "CancelOnClose") ?? true;
            if (!isAsync)
            {
                if (CdeSymbols.HasNamedArgument(attribute, "AllowConcurrentExecutions") || CdeSymbols.HasNamedArgument(attribute, "CancelOnClose"))
                {
                    Report(
                        CdeDiagnostics.RelayCommandOptionIgnored,
                        location,
                        method.Name,
                        "AllowConcurrentExecutions/CancelOnClose");
                }
            }

            _commandOwners.Add(commandName, method);
            _generatedMemberNames.Add(commandName);

            return new RelayCommandModel(
                method,
                commandName,
                isAsync,
                returnKind == RelayReturnKind.ValueTask,
                valueParameter,
                hasCancellationToken,
                canExecute.Model,
                allowConcurrent,
                cancelOnClose);
        }

        private enum RelayReturnKind
        {
            Unsupported,
            Void,
            Task,
            ValueTask,
        }

        private RelayReturnKind ClassifyReturn(ITypeSymbol returnType)
        {
            if (returnType.SpecialType == SpecialType.System_Void)
                return RelayReturnKind.Void;
            if (returnType is not INamedTypeSymbol named || named.TypeArguments.Length != 0)
                return RelayReturnKind.Unsupported;
            if (named.Name != "Task" && named.Name != "ValueTask")
                return RelayReturnKind.Unsupported;
            var containingNamespace = named.ContainingNamespace;
            if (containingNamespace == null || !string.Equals(containingNamespace.ToDisplayString(), "System.Threading.Tasks", StringComparison.Ordinal))
                return RelayReturnKind.Unsupported;
            return named.Name == "Task" ? RelayReturnKind.Task : RelayReturnKind.ValueTask;
        }

        private bool TryAnalyzeParameters(
            IMethodSymbol method,
            bool isAsync,
            out IParameterSymbol? valueParameter,
            out bool hasCancellationToken,
            out string reason)
        {
            valueParameter = null;
            hasCancellationToken = false;
            reason = string.Empty;

            var parameters = method.Parameters;
            foreach (var parameter in parameters)
            {
                if (parameter.RefKind != RefKind.None)
                {
                    reason = $"parameter '{parameter.Name}' is passed by reference (ref/out/in)";
                    return false;
                }
                if (parameter.Type.IsRefLikeType ||
                    parameter.Type.TypeKind == TypeKind.Pointer ||
                    parameter.Type.TypeKind == TypeKind.FunctionPointer)
                {
                    reason = $"parameter '{parameter.Name}' has a ref-like or pointer type '{parameter.Type.ToDisplayString()}' and cannot be captured by a command";
                    return false;
                }
            }

            if (!isAsync)
            {
                if (parameters.Length == 0)
                    return true;
                if (parameters.Length == 1 && !IsCancellationToken(parameters[0].Type))
                {
                    valueParameter = parameters[0];
                    return true;
                }
                reason = parameters.Length == 1
                    ? "a void relay command cannot accept a CancellationToken; make the method return Task/ValueTask to receive cancellation"
                    : "void relay commands support zero parameters or exactly one UI event value parameter";
                return false;
            }

            if (parameters.Length == 0)
                return true;
            if (parameters.Length == 1)
            {
                if (IsCancellationToken(parameters[0].Type))
                    hasCancellationToken = true;
                else
                    valueParameter = parameters[0];
                return true;
            }
            if (parameters.Length == 2 && !IsCancellationToken(parameters[0].Type) && IsCancellationToken(parameters[1].Type))
            {
                valueParameter = parameters[0];
                hasCancellationToken = true;
                return true;
            }

            reason = parameters.Length == 2
                ? "the CancellationToken must be the last parameter and there must be at most one UI event value parameter"
                : "async relay commands support zero parameters, one UI event value parameter, an optional trailing CancellationToken, or one UI event value plus a trailing CancellationToken";
            return false;
        }

        private bool IsCancellationToken(ITypeSymbol type)
        {
            var cancellationToken = _compilation.GetTypeByMetadataName(CdeSymbols.CancellationTokenMetadataName);
            return cancellationToken != null && SymbolEqualityComparer.Default.Equals(type, cancellationToken);
        }

        private CanExecuteResolution ResolveCanExecute(
            IMethodSymbol relayMethod,
            AttributeData? attribute,
            ITypeSymbol? valueType,
            Location methodLocation)
        {
            var canExecuteName = CdeSymbols.GetNamedStringArgument(attribute, "CanExecute");
            if (string.IsNullOrEmpty(canExecuteName))
                return CanExecuteResolution.None;

            var candidates = FindMethodsInHierarchy(canExecuteName);
            if (candidates.Count == 0)
            {
                Report(
                    CdeDiagnostics.RelayCommandCanExecuteMissing,
                    GetCanExecuteLocation(attribute, methodLocation),
                    relayMethod.Name,
                    canExecuteName,
                    _view.ToDisplayString());
                return CanExecuteResolution.Failed;
            }

            var acceptsValue = new List<IMethodSymbol>();
            var acceptsNothing = new List<IMethodSymbol>();
            var reasons = new List<string>();
            foreach (var candidate in candidates)
            {
                if (candidate.IsStatic)
                {
                    reasons.Add($"'{candidate.Name}' must be an instance method");
                    continue;
                }
                if (candidate.IsGenericMethod)
                {
                    reasons.Add($"'{candidate.Name}' must not be generic");
                    continue;
                }
                if (candidate.ReturnType.SpecialType != SpecialType.System_Boolean)
                {
                    reasons.Add($"'{candidate.Name}' must return bool");
                    continue;
                }
                if (!_compilation.IsSymbolAccessibleWithin(candidate, _view))
                {
                    reasons.Add($"'{candidate.Name}' is not accessible from View '{_view.Name}'");
                    continue;
                }

                var parameters = candidate.Parameters;
                if (parameters.Length > 1)
                {
                    reasons.Add($"'{candidate.Name}' must take zero or one parameter");
                    continue;
                }
                if (parameters.Length == 0)
                {
                    acceptsNothing.Add(candidate);
                    continue;
                }

                var parameter = parameters[0];
                if (parameter.RefKind != RefKind.None ||
                    parameter.Type.IsRefLikeType ||
                    parameter.Type.TypeKind == TypeKind.Pointer ||
                    parameter.Type.TypeKind == TypeKind.FunctionPointer)
                {
                    reasons.Add($"parameter '{parameter.Name}' of '{candidate.Name}' cannot be passed by reference or captured");
                    continue;
                }
                if (IsCancellationToken(parameter.Type))
                {
                    reasons.Add($"'{candidate.Name}' cannot accept a CancellationToken");
                    continue;
                }
                if (valueType == null)
                {
                    reasons.Add($"'{candidate.Name}' takes a parameter but the command has no UI event value");
                    continue;
                }
                if (!_compilation.ClassifyCommonConversion(valueType, parameter.Type).IsImplicit)
                {
                    reasons.Add($"parameter '{parameter.Name}' of '{candidate.Name}' cannot accept the command value type '{valueType.ToDisplayString()}'");
                    continue;
                }
                acceptsValue.Add(candidate);
            }

            if (acceptsValue.Count > 1 || (acceptsValue.Count == 0 && acceptsNothing.Count > 1))
            {
                Report(
                    CdeDiagnostics.RelayCommandCanExecuteInvalid,
                    GetCanExecuteLocation(attribute, methodLocation),
                    relayMethod.Name,
                    canExecuteName,
                    "the name matches multiple overloads; declare exactly one matching CanExecute method");
                return CanExecuteResolution.Failed;
            }
            if (acceptsValue.Count == 1)
                return CanExecuteResolution.Success(new CanExecuteModel(acceptsValue[0], takesValue: true));
            if (acceptsNothing.Count == 1)
                return CanExecuteResolution.Success(new CanExecuteModel(acceptsNothing[0], takesValue: false));

            var detail = reasons.Count == 0 ? "no matching method" : string.Join("; ", reasons.Distinct());
            Report(
                CdeDiagnostics.RelayCommandCanExecuteInvalid,
                GetCanExecuteLocation(attribute, methodLocation),
                relayMethod.Name,
                canExecuteName,
                detail);
            return CanExecuteResolution.Failed;
        }

        private List<IMethodSymbol> FindMethodsInHierarchy(string name)
        {
            var methods = new List<IMethodSymbol>();
            for (var current = _view; current != null; current = current.BaseType)
            {
                foreach (var member in current.GetMembers(name))
                {
                    if (member is not IMethodSymbol method)
                        continue;
                    if (methods.Any(existing => SymbolEqualityComparer.Default.Equals(existing, method)))
                        continue;
                    methods.Add(method);
                }
            }
            return methods;
        }

        private Location GetCanExecuteLocation(AttributeData? attribute, Location fallback)
        {
            if (attribute == null)
                return fallback;
            var attributeSyntax = attribute.ApplicationSyntaxReference?.GetSyntax() as AttributeSyntax;
            if (attributeSyntax?.ArgumentList != null)
            {
                foreach (var argument in attributeSyntax.ArgumentList.Arguments)
                {
                    if (argument.NameEquals != null &&
                        string.Equals(argument.NameEquals.Name.Identifier.ValueText, "CanExecute", StringComparison.Ordinal))
                        return argument.GetLocation();
                }
            }
            return CdeSymbols.GetAttributeLocation(attribute, fallback);
        }

        #endregion

        #region Rendering

        private string Render(List<ObservablePropertyModel> properties, List<RelayCommandModel> commands)
        {
            var builder = new StringBuilder();
            builder.AppendLine("// <auto-generated/>");
            builder.AppendLine("// Generated by the ObFramework CDE source generator (ObservableProperty / RelayCommand). Do not edit.");
            builder.AppendLine("#nullable enable");
            builder.AppendLine();

            var namespaceName = CdeSymbols.GetNamespaceName(_view);
            var hasNamespace = namespaceName.Length > 0;
            if (hasNamespace)
            {
                builder.Append("namespace ").AppendLine(namespaceName);
                builder.AppendLine("{");
            }

            var indent = hasNamespace ? "    " : string.Empty;
            builder.Append(indent)
                .Append(GetAccessibility(_view))
                .Append(" partial class ")
                .Append(CdeSymbols.EscapeIdentifier(_view.Name))
                .AppendLine();
            builder.Append(indent).AppendLine("{");

            var memberIndent = indent + "    ";
            foreach (var property in properties)
                RenderProperty(builder, memberIndent, property);
            foreach (var command in commands)
                RenderCommand(builder, memberIndent, command);

            builder.Append(indent).AppendLine("}");
            if (hasNamespace)
                builder.AppendLine("}");
            return builder.ToString();
        }

        private static void RenderProperty(StringBuilder builder, string indent, ObservablePropertyModel model)
        {
            var fieldName = "this." + CdeSymbols.EscapeIdentifier(model.Field.Name);
            var setterIndent = indent + "        ";

            builder.Append(indent).Append("public ").Append(model.TypeName).Append(' ').Append(model.PropertyName).AppendLine();
            builder.Append(indent).AppendLine("{");
            builder.Append(indent).Append("    get => ").Append(fieldName).AppendLine(";");
            builder.Append(indent).AppendLine("    set");
            builder.Append(indent).AppendLine("    {");
            builder.Append(setterIndent)
                .Append("if (global::System.Collections.Generic.EqualityComparer<")
                .Append(model.TypeName)
                .Append(">.Default.Equals(")
                .Append(fieldName)
                .AppendLine(", value))");
            builder.Append(setterIndent).AppendLine("{");
            builder.Append(setterIndent).AppendLine("    return;");
            builder.Append(setterIndent).AppendLine("}");
            builder.AppendLine();
            builder.Append(setterIndent).Append(fieldName).AppendLine(" = value;");
            builder.Append(setterIndent).Append(model.EventName).AppendLine("?.Invoke(value);");
            builder.Append(setterIndent).Append("NotifyPropertyChanged(").Append(CdeSymbols.Quote(model.PropertyName)).AppendLine(");");
            foreach (var target in model.PropertyTargets)
            {
                if (string.Equals(target, model.PropertyName, StringComparison.Ordinal))
                    continue;
                builder.Append(setterIndent).Append("NotifyPropertyChanged(").Append(CdeSymbols.Quote(target)).AppendLine(");");
            }
            foreach (var target in model.CommandTargets)
            {
                if (string.Equals(target, model.PropertyName, StringComparison.Ordinal))
                    continue;
                builder.Append(setterIndent).Append("NotifyCommandStateChanged(").Append(CdeSymbols.Quote(target)).AppendLine(");");
            }
            builder.Append(indent).AppendLine("    }");
            builder.Append(indent).AppendLine("}");
            builder.AppendLine();
            builder.Append(indent)
                .Append("public event global::System.Action<")
                .Append(model.TypeName)
                .Append(">? ")
                .Append(model.EventName)
                .AppendLine(";");
            builder.AppendLine();
        }

        private void RenderCommand(StringBuilder builder, string indent, RelayCommandModel model)
        {
            var inner = indent + "    ";
            var body = inner + "    ";
            var commandMethodName = CdeSymbols.EscapeIdentifier(model.Method.Name);

            builder.Append(indent)
                .Append("public sealed class ")
                .Append(model.CommandName)
                .Append(" : ")
                .Append(model.IsAsync
                    ? "global::ObFramework.Integrated.UI.AUIAsyncCommand"
                    : "global::ObFramework.Integrated.UI.AUICommand")
                .AppendLine();
            builder.Append(indent).AppendLine("{");
            builder.Append(inner).Append("private readonly ").Append(_viewTypeName).AppendLine(" _view;");
            builder.AppendLine();

            builder.Append(inner).Append("public ").Append(model.CommandName).Append('(').Append(_viewTypeName).Append(" view");
            if (model.ValueParameter != null)
                builder.Append(", ").Append(CdeSymbols.DisplayType(model.ValueParameter.Type)).Append(" value");
            builder.AppendLine(")");
            builder.Append(inner).Append("    : base(view, ").Append(CdeSymbols.Quote(model.CommandName));
            if (model.ValueParameter != null)
                builder.Append(", value");
            builder.AppendLine(")");
            builder.Append(inner).AppendLine("{");
            builder.Append(body).AppendLine("_view = view;");
            if (model.IsAsync && model.AllowConcurrentExecutions)
                builder.Append(body).AppendLine("AllowConcurrentExecutions = true;");
            if (model.IsAsync && !model.CancelOnClose)
                builder.Append(body).AppendLine("CancelOnClose = false;");
            builder.Append(inner).AppendLine("}");
            builder.AppendLine();

            RenderCanExecute(builder, inner, body, model);
            RenderExecute(builder, inner, body, model, commandMethodName);

            builder.Append(indent).AppendLine("}");
            builder.AppendLine();
        }

        private static void RenderCanExecute(StringBuilder builder, string inner, string body, RelayCommandModel model)
        {
            var canExecute = model.CanExecute;
            if (canExecute == null)
                return;

            var methodName = CdeSymbols.EscapeIdentifier(canExecute.Method.Name);
            if (canExecute.TakesValue && model.ValueParameter != null)
            {
                // Cast the boxed command value to the VALUE parameter type, not to the CanExecute
                // parameter type: the latter may be a wider numeric type (`float` event value feeding
                // `CanExecute(double value)`), and casting the boxed float directly to double throws
                // InvalidCastException.
                var valueTypeName = CdeSymbols.DisplayType(model.ValueParameter.Type);
                builder.Append(inner).AppendLine("protected override bool CanExecuteCore()");
                builder.Append(inner).AppendLine("{");
                builder.Append(body).Append("return _view.").Append(methodName).Append("((").Append(valueTypeName).Append(")Parameter!);").AppendLine();
                builder.Append(inner).AppendLine("}");
                builder.AppendLine();
                builder.Append(inner).AppendLine("protected override bool CanExecuteCore(object? parameter)");
                builder.Append(inner).AppendLine("{");
                builder.Append(body).Append("return _view.").Append(methodName).Append("((").Append(valueTypeName).Append(")parameter!);").AppendLine();
                builder.Append(inner).AppendLine("}");
            }
            else
            {
                builder.Append(inner).AppendLine("protected override bool CanExecuteCore()");
                builder.Append(inner).AppendLine("{");
                builder.Append(body).Append("return _view.").Append(methodName).AppendLine("();");
                builder.Append(inner).AppendLine("}");
            }
            builder.AppendLine();
        }

        private static void RenderExecute(
            StringBuilder builder,
            string inner,
            string body,
            RelayCommandModel model,
            string commandMethodName)
        {
            var arguments = BuildBusinessArguments(model);
            if (!model.IsAsync)
            {
                builder.Append(inner).AppendLine("protected override void ExecuteCore()");
                builder.Append(inner).AppendLine("{");
                builder.Append(body).Append("_view.").Append(commandMethodName).Append('(').Append(arguments).AppendLine(");");
                builder.Append(inner).AppendLine("}");
                return;
            }

            var call = "_view." + commandMethodName + "(" + arguments + ")";
            builder.Append(inner).AppendLine("protected override global::System.Threading.Tasks.ValueTask ExecuteCore(global::System.Threading.CancellationToken ct)");
            builder.Append(inner).AppendLine("{");
            if (model.ReturnsValueTask)
                builder.Append(body).Append("return ").Append(call).AppendLine(";");
            else
                builder.Append(body).Append("return new global::System.Threading.Tasks.ValueTask(").Append(call).AppendLine(");");
            builder.Append(inner).AppendLine("}");
        }

        private static string BuildBusinessArguments(RelayCommandModel model)
        {
            var arguments = new List<string>();
            if (model.ValueParameter != null)
                arguments.Add("(" + CdeSymbols.DisplayType(model.ValueParameter.Type) + ")Parameter!");
            if (model.HasCancellationToken)
                arguments.Add("ct");
            return string.Join(", ", arguments);
        }

        private static string GetAccessibility(INamedTypeSymbol type)
        {
            switch (type.DeclaredAccessibility)
            {
                case Accessibility.Public:
                    return "public";
                case Accessibility.Internal:
                    return "internal";
                case Accessibility.Protected:
                    return "protected";
                case Accessibility.ProtectedOrInternal:
                    return "protected internal";
                case Accessibility.ProtectedAndInternal:
                    return "private protected";
                default:
                    return "internal";
            }
        }

        #endregion

        private void Report(DiagnosticDescriptor descriptor, Location location, params object[] arguments)
        {
            _diagnostics.Add(Diagnostic.Create(descriptor, location, arguments));
        }

        private sealed class ObservableNotifications
        {
            public ObservableNotifications(List<string> propertyTargets, List<string> commandTargets)
            {
                PropertyTargets = propertyTargets;
                CommandTargets = commandTargets;
            }

            public List<string> PropertyTargets { get; }

            public List<string> CommandTargets { get; }
        }

        private sealed class ObservablePropertyModel
        {
            public ObservablePropertyModel(
                IFieldSymbol field,
                string propertyName,
                string eventName,
                string typeName,
                List<string> propertyTargets,
                List<string> commandTargets)
            {
                Field = field;
                PropertyName = propertyName;
                EventName = eventName;
                TypeName = typeName;
                PropertyTargets = propertyTargets;
                CommandTargets = commandTargets;
            }

            public IFieldSymbol Field { get; }

            public string PropertyName { get; }

            public string EventName { get; }

            public string TypeName { get; }

            public List<string> PropertyTargets { get; }

            public List<string> CommandTargets { get; }
        }

        private sealed class CanExecuteModel
        {
            public CanExecuteModel(IMethodSymbol method, bool takesValue)
            {
                Method = method;
                TakesValue = takesValue;
            }

            public IMethodSymbol Method { get; }

            public bool TakesValue { get; }
        }

        private sealed class CanExecuteResolution
        {
            private CanExecuteResolution(CanExecuteModel? model, bool failed)
            {
                Model = model;
                IsFailed = failed;
            }

            public static CanExecuteResolution None { get; } = new CanExecuteResolution(null, failed: false);

            public static CanExecuteResolution Failed { get; } = new CanExecuteResolution(null, failed: true);

            public CanExecuteModel? Model { get; }

            public bool IsFailed { get; }

            public static CanExecuteResolution Success(CanExecuteModel model)
            {
                return new CanExecuteResolution(model, failed: false);
            }
        }

        private sealed class RelayCommandModel
        {
            public RelayCommandModel(
                IMethodSymbol method,
                string commandName,
                bool isAsync,
                bool returnsValueTask,
                IParameterSymbol? valueParameter,
                bool hasCancellationToken,
                CanExecuteModel? canExecute,
                bool allowConcurrentExecutions,
                bool cancelOnClose)
            {
                Method = method;
                CommandName = commandName;
                IsAsync = isAsync;
                ReturnsValueTask = returnsValueTask;
                ValueParameter = valueParameter;
                HasCancellationToken = hasCancellationToken;
                CanExecute = canExecute;
                AllowConcurrentExecutions = allowConcurrentExecutions;
                CancelOnClose = cancelOnClose;
            }

            public IMethodSymbol Method { get; }

            public string CommandName { get; }

            public bool IsAsync { get; }

            public bool ReturnsValueTask { get; }

            public IParameterSymbol? ValueParameter { get; }

            public bool HasCancellationToken { get; }

            public CanExecuteModel? CanExecute { get; }

            public bool AllowConcurrentExecutions { get; }

            public bool CancelOnClose { get; }
        }
    }
}
