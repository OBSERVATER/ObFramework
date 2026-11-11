using Microsoft.CodeAnalysis;

namespace ObFramework.Generator.CDE
{
    /// <summary>
    /// Diagnostics reported by the engine-neutral CDE source generator (T07/T09/T10/T11).
    /// IDs use the OBF1xx range reserved for generated business View code.
    /// </summary>
    internal static class CdeDiagnostics
    {
        private const string Category = "ObFramework.CDE";

        private static DiagnosticDescriptor Error(string id, string title, string messageFormat)
        {
            return new DiagnosticDescriptor(id, title, messageFormat, Category, DiagnosticSeverity.Error, isEnabledByDefault: true);
        }

        private static DiagnosticDescriptor Warning(string id, string title, string messageFormat)
        {
            return new DiagnosticDescriptor(id, title, messageFormat, Category, DiagnosticSeverity.Warning, isEnabledByDefault: true);
        }

        public static readonly DiagnosticDescriptor ViewNotPartial = Error(
            "OBF100",
            "View must be partial",
            "View '{0}' must be declared 'partial' so the CDE source generator can add generated properties and commands.");

        public static readonly DiagnosticDescriptor ViewNotAUIView = Error(
            "OBF101",
            "View must derive from AUIView",
            "'{0}' does not derive from ObFramework.Integrated.UI.AUIView; CDE attributes are only supported on engine-neutral business Views.");

        public static readonly DiagnosticDescriptor ViewNested = Error(
            "OBF102",
            "Nested View types are not supported",
            "View '{0}' is a nested type; the CDE source generator only supports top-level (non-nested) View classes.");

        public static readonly DiagnosticDescriptor ViewGeneric = Error(
            "OBF103",
            "Generic View types are not supported",
            "View '{0}' is generic; the CDE source generator only supports non-generic View classes.");

        public static readonly DiagnosticDescriptor ViewNotClass = Error(
            "OBF104",
            "Observable state requires a class View",
            "'{0}' is not a class; [ObservableProperty] and [RelayCommand] require a class deriving from AUIView.");

        public static readonly DiagnosticDescriptor ObservableFieldStatic = Error(
            "OBF110",
            "Observable field must be an instance field",
            "Field '{0}' is static; observable properties are per-View-instance state, so [ObservableProperty] requires an instance field.");

        public static readonly DiagnosticDescriptor ObservableFieldConst = Error(
            "OBF111",
            "Observable field must not be const",
            "Field '{0}' is const; [ObservableProperty] requires a mutable instance field.");

        public static readonly DiagnosticDescriptor ObservableFieldReadOnly = Error(
            "OBF112",
            "Observable field must not be readonly",
            "Field '{0}' is readonly; the generated property must assign the same backing field, so [ObservableProperty] requires a writable field.");

        public static readonly DiagnosticDescriptor ObservableFieldUnsupportedType = Error(
            "OBF113",
            "Observable field type is not supported",
            "Field '{0}' has type '{1}', which cannot be used for observable View state (pointer, ref-like and void types are not supported).");

        public static readonly DiagnosticDescriptor ObservableMemberCollision = Error(
            "OBF114",
            "Generated observable member collides with an existing View member",
            "Field '{0}' would generate member '{1}', but the View already contains a member with that name; rename the field.");

        public static readonly DiagnosticDescriptor DuplicateObservableProperty = Error(
            "OBF115",
            "Two observable fields generate the same property",
            "Fields '{0}' and '{1}' would both generate property '{2}'; rename one of the fields.");

        public static readonly DiagnosticDescriptor ObservableFieldNameInvalid = Error(
            "OBF116",
            "Cannot derive a property name from the field",
            "Cannot derive a valid PascalCase property name for observable field '{0}'; use a field name such as '_count' or 'count'.");

        public static readonly DiagnosticDescriptor NotifyTargetInvalid = Error(
            "OBF120",
            "Notification target name is invalid",
            "Field '{0}' declares [{1}] with an empty target name; use nameof(...) or a non-empty property/command name.");

        public static readonly DiagnosticDescriptor RelayCommandKindUnsupported = Error(
            "OBF130",
            "Relay command must be an ordinary method",
            "'{0}' is not an ordinary method; [RelayCommand] requires a normal instance method declaration.");

        public static readonly DiagnosticDescriptor RelayCommandStatic = Error(
            "OBF131",
            "Relay command must be an instance method",
            "Method '{0}' is static; a relay command captures one View instance, so [RelayCommand] requires an instance method.");

        public static readonly DiagnosticDescriptor RelayCommandGeneric = Error(
            "OBF132",
            "Relay command must not be generic",
            "Method '{0}' is generic; the CDE source generator cannot construct a typed command for generic relay methods.");

        public static readonly DiagnosticDescriptor RelayCommandParameterUnsupported = Error(
            "OBF133",
            "Relay command signature is not supported",
            "Method '{0}' has an unsupported command signature: {1}");

        public static readonly DiagnosticDescriptor RelayCommandReturnTypeUnsupported = Error(
            "OBF134",
            "Relay command return type is not supported",
            "Method '{0}' returns '{1}'; relay commands support void, Task and ValueTask only (Task<T>/ValueTask<T> are not supported, expose results as observable state instead).");

        public static readonly DiagnosticDescriptor RelayCommandNameCollision = Error(
            "OBF135",
            "Two relay methods generate the same command",
            "Methods '{0}' and '{1}' both generate command type '{2}'; rename one of the methods (the 'Async' suffix is dropped).");

        public static readonly DiagnosticDescriptor RelayCommandCanExecuteMissing = Error(
            "OBF136",
            "CanExecute method was not found",
            "Method '{0}' declares CanExecute '{1}', but View '{2}' has no accessible method with that name.");

        public static readonly DiagnosticDescriptor RelayCommandCanExecuteInvalid = Error(
            "OBF137",
            "CanExecute method signature is not supported",
            "Method '{0}' declares CanExecute '{1}', but its signature is not supported: {2}");

        public static readonly DiagnosticDescriptor RelayCommandOptionIgnored = Warning(
            "OBF138",
            "Relay command option is ignored",
            "Method '{0}' sets '{1}', but that option only applies to async (Task/ValueTask) relay commands and is ignored.");

        public static readonly DiagnosticDescriptor GeneratedCommandCollision = Error(
            "OBF139",
            "Generated command type collides with another View member",
            "Method '{0}' would generate command type '{1}', but the View already contains a member with that name (or another generated member uses it); rename the method or the member.");
    }
}
