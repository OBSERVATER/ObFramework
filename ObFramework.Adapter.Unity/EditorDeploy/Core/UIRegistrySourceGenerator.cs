#nullable enable
using System;
using System.Collections.Generic;
using System.Text;

namespace ObFramework.Editor.Core
{
    /// <summary>
    /// Emits the replaceable static Unity UI registration source from the setup.json snapshot and
    /// the explicit View/VC pairs found in the Unity project.
    /// <para>
    /// The emitted <c>RegisterAll(&lt;adapter&gt;)</c> only calls the Unity adapter's explicit
    /// <c>Register&lt;TView, TWindow&gt;(resourceKey)</c> API. Resource keys are engine asset
    /// addresses: a per-entry override wins, then a setup.json override, then
    /// <c>resourceKeyFormat</c> with <c>{view}</c> replaced by the View type name. The development
    /// root path is never emitted or used as an address, and the runtime never reads setup.json.
    /// </para>
    /// </summary>
    public static class UIRegistrySourceGenerator
    {
        /// <summary>Default generated registry file name.</summary>
        public const string DefaultFileName = "GeneratedUIRegistry.g.cs";

        /// <summary>Generates the registry source; errors suppress emission.</summary>
        public static UIRegistryGenerationOutput Generate(UIRegistryGenerationInput input)
        {
            if (input == null)
                throw new ArgumentNullException(nameof(input));

            var diagnostics = new List<EditorDiagnostic>();
            var registrations = new List<UIRegistryResolvedEntry>();
            var setup = input.Setup;
            if (setup == null)
            {
                diagnostics.Add(new EditorDiagnostic(
                    EditorDiagnosticSeverity.Error,
                    EditorDiagnosticCodes.InvalidRegistryEntry,
                    "A validated setup.json snapshot is required to generate the UI registry."));
                return new UIRegistryGenerationOutput(null, diagnostics, registrations);
            }

            var @namespace = (input.Namespace ?? string.Empty).Trim();
            if (!EditorNaming.IsValidNamespace(@namespace))
            {
                diagnostics.Add(new EditorDiagnostic(
                    EditorDiagnosticSeverity.Error,
                    EditorDiagnosticCodes.InvalidRegistryEntry,
                    "'" + @namespace + "' is not a valid C# namespace for the generated UI registry.",
                    "Namespace"));
            }

            var className = (input.ClassName ?? string.Empty).Trim();
            if (!EditorNaming.IsValidIdentifier(className))
            {
                diagnostics.Add(new EditorDiagnostic(
                    EditorDiagnosticSeverity.Error,
                    EditorDiagnosticCodes.InvalidRegistryEntry,
                    "'" + className + "' is not a valid C# class name for the generated UI registry.",
                    "ClassName"));
            }

            var methodName = (input.MethodName ?? string.Empty).Trim();
            if (!EditorNaming.IsValidIdentifier(methodName))
            {
                diagnostics.Add(new EditorDiagnostic(
                    EditorDiagnosticSeverity.Error,
                    EditorDiagnosticCodes.InvalidRegistryEntry,
                    "'" + methodName + "' is not a valid C# method name for the generated UI registry.",
                    "MethodName"));
            }

            var adapterTypeName = NormalizeQualifiedTypeName(input.AdapterTypeName);
            if (!EditorNaming.IsValidQualifiedName(adapterTypeName))
            {
                diagnostics.Add(new EditorDiagnostic(
                    EditorDiagnosticSeverity.Error,
                    EditorDiagnosticCodes.InvalidRegistryEntry,
                    "'" + input.AdapterTypeName + "' is not a valid fully qualified Unity UI adapter type name.",
                    "AdapterTypeName"));
            }

            var outputPath = ResolveOutputPath(input.OutputPath, setup, className, diagnostics);

            var entries = input.Entries ?? Array.Empty<UIRegistryEntry>();
            var seenViews = new HashSet<string>(StringComparer.Ordinal);
            var resourceKeys = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var entry in entries)
            {
                if (entry == null)
                {
                    diagnostics.Add(new EditorDiagnostic(
                        EditorDiagnosticSeverity.Error,
                        EditorDiagnosticCodes.InvalidRegistryEntry,
                        "The UI registry input contains a null entry."));
                    continue;
                }

                var viewType = (entry.ViewTypeFullName ?? string.Empty).Trim();
                var windowType = (entry.WindowTypeFullName ?? string.Empty).Trim();
                if (!EditorNaming.IsValidQualifiedName(viewType))
                {
                    diagnostics.Add(new EditorDiagnostic(
                        EditorDiagnosticSeverity.Error,
                        EditorDiagnosticCodes.InvalidRegistryEntry,
                        "'" + viewType + "' is not a valid fully qualified View type name.",
                        "ViewTypeFullName"));
                    continue;
                }

                if (!EditorNaming.IsValidQualifiedName(windowType))
                {
                    diagnostics.Add(new EditorDiagnostic(
                        EditorDiagnosticSeverity.Error,
                        EditorDiagnosticCodes.InvalidRegistryEntry,
                        "'" + windowType + "' is not a valid fully qualified window type name.",
                        "WindowTypeFullName"));
                    continue;
                }

                if (string.Equals(viewType, windowType, StringComparison.Ordinal))
                {
                    diagnostics.Add(new EditorDiagnostic(
                        EditorDiagnosticSeverity.Error,
                        EditorDiagnosticCodes.InvalidRegistryEntry,
                        "View type '" + viewType + "' must differ from its window controller type.",
                        viewType));
                    continue;
                }

                if (!seenViews.Add(viewType))
                {
                    diagnostics.Add(new EditorDiagnostic(
                        EditorDiagnosticSeverity.Error,
                        EditorDiagnosticCodes.DuplicateViewMapping,
                        "Duplicate View mapping: '" + viewType + "' is registered more than once. " +
                        "A View type may only map to one window/resource key.",
                        viewType));
                    continue;
                }

                var viewShortName = LastSegment(viewType);
                var resourceKey = !string.IsNullOrWhiteSpace(entry.ResourceKeyOverride)
                    ? entry.ResourceKeyOverride!.Trim()
                    : setup.ResolveResourceKey(viewShortName);

                if (resourceKey.Length == 0)
                {
                    diagnostics.Add(new EditorDiagnostic(
                        EditorDiagnosticSeverity.Error,
                        EditorDiagnosticCodes.SetupResourceKeyInvalid,
                        "View '" + viewType + "' resolves to an empty resource key.",
                        viewShortName));
                    continue;
                }

                if (UISetupPaths.LooksLikeFileSystemPath(resourceKey, setup.Root))
                {
                    diagnostics.Add(new EditorDiagnostic(
                        EditorDiagnosticSeverity.Error,
                        EditorDiagnosticCodes.SetupResourceKeyInvalid,
                        "View '" + viewType + "' resolves to '" + resourceKey +
                        "', which looks like a file system path. Resource keys must be engine asset addresses, never development-root paths.",
                        viewShortName));
                    continue;
                }

                if (resourceKey.IndexOf("{view}", StringComparison.Ordinal) >= 0)
                {
                    diagnostics.Add(new EditorDiagnostic(
                        EditorDiagnosticSeverity.Error,
                        EditorDiagnosticCodes.SetupResourceKeyInvalid,
                        "View '" + viewType + "' resource key '" + resourceKey + "' still contains the '{view}' placeholder.",
                        viewShortName));
                    continue;
                }

                if (resourceKeys.TryGetValue(resourceKey, out var existingView))
                {
                    diagnostics.Add(new EditorDiagnostic(
                        EditorDiagnosticSeverity.Warning,
                        EditorDiagnosticCodes.DuplicateResourceKey,
                        "Views '" + existingView + "' and '" + viewType + "' both resolve to resource key '" + resourceKey +
                        "'. Use setup.json resourceKeyOverrides when they are different assets.",
                        viewShortName));
                }
                else
                {
                    resourceKeys.Add(resourceKey, viewType);
                }

                registrations.Add(new UIRegistryResolvedEntry(viewType, windowType, viewShortName, resourceKey));
            }

            WarnAboutUnusedOverrides(setup, registrations, diagnostics);

            if (registrations.Count == 0)
            {
                diagnostics.Add(new EditorDiagnostic(
                    EditorDiagnosticSeverity.Warning,
                    EditorDiagnosticCodes.RegistryHasNoEntries,
                    "No generated View/window pairs were found; the UI registry will contain no registrations."));
            }

            var errors = false;
            foreach (var diagnostic in diagnostics)
            {
                if (!diagnostic.IsError)
                    continue;
                errors = true;
                break;
            }

            if (errors || outputPath == null)
                return new UIRegistryGenerationOutput(null, diagnostics, registrations);

            var content = BuildSource(@namespace, className, methodName, adapterTypeName, registrations);
            var isModified = !string.Equals(content, input.ExistingSource, StringComparison.Ordinal);
            var file = new GeneratedFile(outputPath, GeneratedFileKind.Registry, content, isModified);
            return new UIRegistryGenerationOutput(file, diagnostics, registrations);
        }

        /// <summary>Builds the registry source text (also used by tests to inspect exact emission).</summary>
        public static string BuildSource(
            string @namespace,
            string className,
            string methodName,
            string adapterTypeName,
            IReadOnlyList<UIRegistryResolvedEntry> registrations)
        {
            var hasNamespace = !string.IsNullOrEmpty(@namespace);
            var classIndent = hasNamespace ? "    " : string.Empty;
            var memberIndent = classIndent + "    ";
            var bodyIndent = memberIndent + "    ";

            var builder = new StringBuilder();
            builder.AppendLine("// ------------------------------------------------------------------------------");
            builder.AppendLine("// OBF UI 2.0 static UI registry generated from setup.json by the Unity Editor tooling.");
            builder.AppendLine("// Replaceable file: rerun Tools/OBF/UI/Generate UI Registry instead of editing it.");
            builder.AppendLine("// Registrations are explicit adapter calls; the game runtime never reads setup.json.");
            builder.AppendLine("// ------------------------------------------------------------------------------");
            if (hasNamespace)
            {
                builder.Append("namespace ").Append(@namespace).AppendLine();
                builder.AppendLine("{");
            }

            builder.Append(classIndent).AppendLine("/// <summary>Explicit Unity View-to-window-to-resource registrations generated from setup.json.</summary>");
            builder.Append(classIndent).Append("public static class ").Append(className).AppendLine();
            builder.Append(classIndent).AppendLine("{");
            builder.Append(memberIndent).AppendLine("/// <summary>Registers every generated UI View with the Unity UI adapter. Call once during application startup.</summary>");
            builder.Append(memberIndent).Append("public static void ").Append(methodName).Append("(global::")
                .Append(adapterTypeName).AppendLine(" adapter)");
            builder.Append(memberIndent).AppendLine("{");
            builder.Append(bodyIndent).AppendLine("if (adapter == null)");
            builder.Append(bodyIndent).Append("    throw new global::System.ArgumentNullException(").Append("nameof(adapter));");
            builder.AppendLine();
            builder.AppendLine();

            if (registrations.Count == 0)
            {
                builder.Append(bodyIndent).AppendLine("// No generated View/window pairs were found; add them and rerun Tools/OBF/UI/Generate UI Registry.");
            }
            else
            {
                for (var index = 0; index < registrations.Count; index++)
                {
                    var registration = registrations[index];
                    builder.Append(bodyIndent).Append("adapter.Register<global::").Append(registration.ViewTypeFullName)
                        .Append(", global::").Append(registration.WindowTypeFullName).Append(">(")
                        .Append(EditorNaming.ToCSharpStringLiteral(registration.ResourceKey)).Append(')');
                    builder.AppendLine(index + 1 < registrations.Count ? ";" : ";");
                }
            }

            builder.Append(memberIndent).AppendLine("}");
            builder.Append(classIndent).AppendLine("}");
            if (hasNamespace)
                builder.AppendLine("}");
            return builder.ToString();
        }

        private static void WarnAboutUnusedOverrides(
            UISetupSnapshot setup,
            IReadOnlyList<UIRegistryResolvedEntry> registrations,
            ICollection<EditorDiagnostic> diagnostics)
        {
            if (setup.ResourceKeyOverrides.Count == 0)
                return;

            var views = new HashSet<string>(StringComparer.Ordinal);
            foreach (var registration in registrations)
                views.Add(registration.ViewTypeShortName);

            foreach (var pair in setup.ResourceKeyOverrides)
            {
                if (views.Contains(pair.Key))
                    continue;
                diagnostics.Add(new EditorDiagnostic(
                    EditorDiagnosticSeverity.Warning,
                    EditorDiagnosticCodes.UnusedResourceKeyOverride,
                    "Resource key override for '" + pair.Key + "' does not match any generated View type name.",
                    pair.Key));
            }
        }

        private static string? ResolveOutputPath(
            string? explicitPath,
            UISetupSnapshot setup,
            string className,
            ICollection<EditorDiagnostic> diagnostics)
        {
            var candidate = string.IsNullOrWhiteSpace(explicitPath)
                ? setup.ScriptOutputRoot + "/" + (string.IsNullOrEmpty(className) ? DefaultFileName : className + ".g.cs")
                : explicitPath!.Trim();

            if (!UISetupPaths.TryNormalizeProjectFile(candidate, "UI registry output", out var normalized, out var error))
            {
                diagnostics.Add(new EditorDiagnostic(
                    EditorDiagnosticSeverity.Error,
                    EditorDiagnosticCodes.InvalidOutputPath,
                    error!,
                    "OutputPath"));
                return null;
            }

            return normalized;
        }

        private static string NormalizeQualifiedTypeName(string? value)
        {
            var trimmed = (value ?? string.Empty).Trim();
            if (trimmed.StartsWith("global::", StringComparison.Ordinal))
                trimmed = trimmed.Substring("global::".Length);
            return trimmed;
        }

        private static string LastSegment(string qualifiedName)
        {
            var split = qualifiedName.LastIndexOf('.');
            return split < 0 ? qualifiedName : qualifiedName.Substring(split + 1);
        }
    }
}
