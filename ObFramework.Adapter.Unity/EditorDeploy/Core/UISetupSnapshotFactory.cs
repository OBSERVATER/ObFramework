#nullable enable
using System;
using System.Collections.Generic;

namespace ObFramework.Editor.Core
{
    /// <summary>
    /// Validates and normalizes a raw <see cref="UISetupSection"/> into a <see cref="UISetupSnapshot"/>.
    /// Defaults mirror the Config CLI Unity module defaults; unknown/invalid paths yield structured
    /// diagnostics instead of exceptions so the Editor can show them in a dialog.
    /// </summary>
    public static class UISetupSnapshotFactory
    {
        /// <summary>Validates one setup section and returns the snapshot or error diagnostics.</summary>
        public static UISetupSnapshotResult Create(UISetupSection? section)
        {
            var diagnostics = new List<EditorDiagnostic>();
            string root;
            string? rootError;

            if (section == null)
            {
                diagnostics.Add(new EditorDiagnostic(
                    EditorDiagnosticSeverity.Warning,
                    EditorDiagnosticCodes.UnitySectionMissing,
                    "setup.json has no 'modules.unity' section; using the default script/asset root settings. " +
                    "Run 'ob config unity' to configure the module."));
                diagnostics.Add(new EditorDiagnostic(
                    EditorDiagnosticSeverity.Error,
                    EditorDiagnosticCodes.SetupRootRequired,
                    "setup.json 'root' is required and must be an absolute development path."));
                return new UISetupSnapshotResult(null, diagnostics);
            }

            if (!UISetupPaths.TryNormalizeRoot(section.Root, out root, out rootError))
            {
                diagnostics.Add(new EditorDiagnostic(
                    EditorDiagnosticSeverity.Error,
                    EditorDiagnosticCodes.SetupRootRequired,
                    rootError!));
                return new UISetupSnapshotResult(null, diagnostics);
            }

            var scriptOutputRoot = NormalizePath(
                section.ScriptOutputRoot,
                "scriptOutputRoot",
                UISetupSnapshot.DefaultScriptOutputRoot,
                diagnostics);
            var uiAssetRoot = NormalizePath(
                section.UIAssetRoot,
                "uiAssetRoot",
                UISetupSnapshot.DefaultUIAssetRoot,
                diagnostics);

            var resourceKeyFormat = string.IsNullOrWhiteSpace(section.ResourceKeyFormat)
                ? UISetupSnapshot.DefaultResourceKeyFormat
                : section.ResourceKeyFormat!.Trim();

            ValidateResourceKeyFormat(resourceKeyFormat, root, diagnostics);

            var overrides = new Dictionary<string, string>(StringComparer.Ordinal);
            if (section.ResourceKeyOverrides != null)
            {
                foreach (var pair in section.ResourceKeyOverrides)
                {
                    var viewName = (pair.Key ?? string.Empty).Trim();
                    var resourceKey = (pair.Value ?? string.Empty).Trim();
                    if (viewName.Length == 0)
                    {
                        diagnostics.Add(new EditorDiagnostic(
                            EditorDiagnosticSeverity.Error,
                            EditorDiagnosticCodes.SetupResourceKeyInvalid,
                            "A resourceKeyOverrides entry has an empty View type name."));
                        continue;
                    }

                    if (!EditorNaming.IsValidIdentifier(viewName))
                    {
                        diagnostics.Add(new EditorDiagnostic(
                            EditorDiagnosticSeverity.Warning,
                            EditorDiagnosticCodes.SetupResourceKeyInvalid,
                            "resourceKeyOverrides key '" + viewName + "' is not a C# type name; overrides are looked up by View type name.",
                            viewName));
                    }

                    if (resourceKey.Length == 0)
                    {
                        diagnostics.Add(new EditorDiagnostic(
                            EditorDiagnosticSeverity.Error,
                            EditorDiagnosticCodes.SetupResourceKeyInvalid,
                            "resourceKeyOverrides['" + viewName + "'] must not be empty.",
                            viewName));
                        continue;
                    }

                    if (UISetupPaths.LooksLikeFileSystemPath(resourceKey, root))
                    {
                        diagnostics.Add(new EditorDiagnostic(
                            EditorDiagnosticSeverity.Error,
                            EditorDiagnosticCodes.SetupResourceKeyInvalid,
                            "resourceKeyOverrides['" + viewName + "'] looks like a file system path ('" + resourceKey +
                            "'); resource keys must be engine asset addresses, never development-root paths.",
                            viewName));
                        continue;
                    }

                    overrides[viewName] = resourceKey;
                }
            }

            if (scriptOutputRoot == null || uiAssetRoot == null || HasErrors(diagnostics))
                return new UISetupSnapshotResult(null, diagnostics);

            var snapshot = new UISetupSnapshot(root, scriptOutputRoot, uiAssetRoot, resourceKeyFormat, overrides);
            return new UISetupSnapshotResult(snapshot, diagnostics);
        }

        private static bool HasErrors(IReadOnlyList<EditorDiagnostic> diagnostics)
        {
            for (var index = 0; index < diagnostics.Count; index++)
            {
                if (diagnostics[index].IsError)
                    return true;
            }

            return false;
        }

        private static string? NormalizePath(
            string? value,
            string propertyName,
            string defaultValue,
            ICollection<EditorDiagnostic> diagnostics)
        {
            if (string.IsNullOrWhiteSpace(value))
                return defaultValue;

            if (!UISetupPaths.TryNormalizeModuleRelative(value, "setup.json '" + propertyName + "'", out var normalized, out var error))
            {
                diagnostics.Add(new EditorDiagnostic(
                    EditorDiagnosticSeverity.Error,
                    EditorDiagnosticCodes.SetupPathInvalid,
                    error!,
                    propertyName));
                return null;
            }

            return normalized;
        }

        private static void ValidateResourceKeyFormat(
            string format,
            string root,
            ICollection<EditorDiagnostic> diagnostics)
        {
            if (format.Length == 0)
            {
                diagnostics.Add(new EditorDiagnostic(
                    EditorDiagnosticSeverity.Error,
                    EditorDiagnosticCodes.SetupResourceKeyInvalid,
                    "resourceKeyFormat must not be empty.",
                    "resourceKeyFormat"));
                return;
            }

            if (UISetupPaths.LooksLikeFileSystemPath(format, root))
            {
                diagnostics.Add(new EditorDiagnostic(
                    EditorDiagnosticSeverity.Error,
                    EditorDiagnosticCodes.SetupResourceKeyInvalid,
                    "resourceKeyFormat looks like a file system path ('" + format +
                    "'); resource keys must be engine asset addresses, never development-root paths.",
                    "resourceKeyFormat"));
                return;
            }

            if (format.IndexOf("{view}", StringComparison.Ordinal) < 0)
            {
                diagnostics.Add(new EditorDiagnostic(
                    EditorDiagnosticSeverity.Warning,
                    EditorDiagnosticCodes.SetupResourceKeyInvalid,
                    "resourceKeyFormat '" + format + "' has no '{view}' placeholder, so every View resolves to the same resource key. " +
                    "Use the default 'UI/{view}' or per-View resourceKeyOverrides.",
                    "resourceKeyFormat"));
            }
        }
    }
}
