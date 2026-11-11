#nullable enable
using System;

namespace ObFramework.Editor.Core
{
    /// <summary>Severity of one editor generation/validation diagnostic.</summary>
    public enum EditorDiagnosticSeverity
    {
        /// <summary>Informational message; generation can continue.</summary>
        Info = 0,

        /// <summary>Something looks wrong but generation can continue safely.</summary>
        Warning = 1,

        /// <summary>Generation cannot produce usable code for this input.</summary>
        Error = 2,
    }

    /// <summary>
    /// Stable diagnostic codes reported by <see cref="EditorTemplateGenerator"/> and
    /// <see cref="UIRegistrySourceGenerator"/>. Codes are part of the tooling contract: tests and
    /// documentation refer to them.
    /// </summary>
    public static class EditorDiagnosticCodes
    {
        // setup.json snapshot (OBFE0xx)
        /// <summary>setup.json has no absolute <c>root</c> (or the root is not absolute).</summary>
        public const string SetupRootRequired = "OBFE001";

        /// <summary>A module-relative setup path is absolute, contains <c>..</c> or is otherwise invalid.</summary>
        public const string SetupPathInvalid = "OBFE002";

        /// <summary>A resource key rule/override is empty or looks like a file system path.</summary>
        public const string SetupResourceKeyInvalid = "OBFE003";

        /// <summary>The <c>modules.unity</c> section was absent; CLI defaults were used.</summary>
        public const string UnitySectionMissing = "OBFE004";

        /// <summary><c>resourceKeyOverrides</c> could not be read from the raw section JSON.</summary>
        public const string OverridesParseFailed = "OBFE005";

        // registry source (OBFE1xx)
        /// <summary>Two registry entries map the same business View type.</summary>
        public const string DuplicateViewMapping = "OBFE101";

        /// <summary>A registry entry has an invalid View/Window type name.</summary>
        public const string InvalidRegistryEntry = "OBFE102";

        /// <summary>Two different Views resolve to the same resource key.</summary>
        public const string DuplicateResourceKey = "OBFE103";

        /// <summary>The registry would be empty because no View/VC pairs were found.</summary>
        public const string RegistryHasNoEntries = "OBFE104";

        /// <summary>A setup resource-key override targets a View that is not registered.</summary>
        public const string UnusedResourceKeyOverride = "OBFE105";

        // generation request (OBFE2xx)
        /// <summary>A class, namespace, field, property or architecture name is not a valid C# identifier.</summary>
        public const string InvalidIdentifier = "OBFE201";

        /// <summary>A generated file output path is absolute, escapes its directory or is not a .cs file.</summary>
        public const string InvalidOutputPath = "OBFE202";

        /// <summary>The selected Unity UI level is not a known <c>EUILevel</c> value.</summary>
        public const string InvalidLevel = "OBFE203";

        /// <summary>No architecture was selected for the generated window.</summary>
        public const string ArchitectureNotSelected = "OBFE204";

        // binding table (OBFE3xx)
        /// <summary>A binding table row is incomplete (missing component reference/name).</summary>
        public const string InvalidBindingRow = "OBFE301";

        /// <summary>A binding table row uses a control kind the engine generator does not support.</summary>
        public const string UnsupportedControlKind = "OBFE302";

        /// <summary>Two rows generate the same serialized field or the same View declaration.</summary>
        public const string DuplicateBinding = "OBFE303";

        /// <summary>The prefab path of a row is absolute or escapes the project.</summary>
        public const string InvalidPrefabPath = "OBFE304";

        /// <summary>A row has no prefab reference, so the editor cannot assign the serialized field.</summary>
        public const string MissingPrefabReference = "OBFE305";

        /// <summary>The binding table has no usable rows; empty editable templates are generated.</summary>
        public const string EmptyBindingTable = "OBFE306";

        // incremental merge (OBFE4xx)
        /// <summary>A destructive rename/delete is pending user confirmation and was not applied.</summary>
        public const string DestructiveChangePending = "OBFE401";

        /// <summary>A confirmed destructive rename/delete was applied to the generated window.</summary>
        public const string DestructiveChangeApplied = "OBFE402";

        /// <summary>The existing editable source cannot be edited safely (missing class or not partial).</summary>
        public const string ExistingSourceNotEditable = "OBFE403";
    }

    /// <summary>
    /// One structured diagnostic returned by editor generation. Pure data: no Unity or file system
    /// references, so it can be asserted from ordinary .NET tests and surfaced in an Editor dialog.
    /// </summary>
    public sealed class EditorDiagnostic
    {
        public EditorDiagnostic(EditorDiagnosticSeverity severity, string code, string message, string? target = null)
        {
            if (string.IsNullOrEmpty(code))
                throw new ArgumentException("A diagnostic code is required.", nameof(code));
            if (message == null)
                throw new ArgumentNullException(nameof(message));

            Severity = severity;
            Code = code;
            Message = message;
            Target = target;
        }

        /// <summary>Diagnostic severity.</summary>
        public EditorDiagnosticSeverity Severity { get; }

        /// <summary>Stable <see cref="EditorDiagnosticCodes"/> value.</summary>
        public string Code { get; }

        /// <summary>Human readable explanation.</summary>
        public string Message { get; }

        /// <summary>Optional file/field/member the diagnostic applies to.</summary>
        public string? Target { get; }

        /// <summary>True when <see cref="Severity"/> is <see cref="EditorDiagnosticSeverity.Error"/>.</summary>
        public bool IsError => Severity == EditorDiagnosticSeverity.Error;

        public override string ToString()
        {
            var suffix = string.IsNullOrEmpty(Target) ? string.Empty : " [" + Target + "]";
            return Code + " " + Severity + ": " + Message + suffix;
        }
    }
}
