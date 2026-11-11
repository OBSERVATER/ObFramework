#nullable enable
#pragma warning disable 0649 // JsonUtility assigns the serializable DTO fields through reflection.
using System;
using System.Collections.Generic;
using System.IO;
using ObFramework.Editor.Core;
using UnityEditor;
using UnityEngine;

namespace ObFramework.Adapter.Unity.EditorTools
{
    /// <summary>
    /// Reads only the <c>modules.unity</c> section of setup.json (fixed fields via Unity's
    /// <see cref="JsonUtility"/>, resource key overrides via the pure
    /// <see cref="UISetupOverridesJson"/> reader) and validates it with the pure
    /// <see cref="UISetupSnapshotFactory"/>.
    /// <para>
    /// The Unity Editor tooling only consumes this section at edit/generate time. The game runtime
    /// never reads setup.json; it loads the statically registered resource keys emitted by the
    /// registry generator.
    /// </para>
    /// </summary>
    internal static class ObfUiSetup
    {
        public const string SetupFileName = "setup.json";
        private const int MaxDirectoryDepth = 32;

        /// <summary>Unity project root folder (the parent of the Assets folder).</summary>
        public static string ProjectRoot
        {
            get
            {
                var dataPath = Application.dataPath;
                var parent = Directory.GetParent(dataPath);
                return NormalizeDirectory(parent != null ? parent.FullName : dataPath);
            }
        }

        /// <summary>Finds setup.json from the Unity project root upward (Config CLI discovery rule).</summary>
        public static string? FindSetupJson()
        {
            var directory = new DirectoryInfo(ProjectRoot);
            for (var depth = 0; directory != null && depth < MaxDirectoryDepth; depth++, directory = directory.Parent)
            {
                var candidate = Path.Combine(directory.FullName, SetupFileName);
                if (File.Exists(candidate))
                    return candidate;
            }

            return null;
        }

        /// <summary>Loads and validates the unity setup section; never throws.</summary>
        public static ObfUiSetupResult Load()
        {
            var setupPath = FindSetupJson();
            if (setupPath == null)
            {
                return ObfUiSetupResult.Failed(
                    null,
                    "setup.json was not found from the Unity project root '" + ProjectRoot +
                    "' upward. Run 'ob config unity' in the project to create it.");
            }

            string json;
            try
            {
                json = File.ReadAllText(setupPath);
            }
            catch (Exception error)
            {
                return ObfUiSetupResult.Failed(setupPath, "Failed to read '" + setupPath + "': " + error.Message);
            }

            ObfUiSetupJsonModel? model = null;
            try
            {
                model = JsonUtility.FromJson<ObfUiSetupJsonModel>(json);
            }
            catch (Exception error)
            {
                return ObfUiSetupResult.Failed(setupPath, "setup.json is not valid JSON: " + error.Message);
            }

            var diagnostics = new List<EditorDiagnostic>();
            var section = new UISetupSection
            {
                Root = model?.root,
                ScriptOutputRoot = model?.modules?.unity?.scriptOutputRoot,
                UIAssetRoot = model?.modules?.unity?.uiAssetRoot,
                ResourceKeyFormat = model?.modules?.unity?.resourceKeyFormat,
            };

            if (UISetupOverridesJson.TryRead(json, out var overrides, out var overridesError))
            {
                if (overrides.Count > 0)
                {
                    var map = new Dictionary<string, string>(StringComparer.Ordinal);
                    foreach (var pair in overrides)
                        map[pair.Key] = pair.Value;
                    section.ResourceKeyOverrides = map;
                }
            }
            else
            {
                diagnostics.Add(new EditorDiagnostic(
                    EditorDiagnosticSeverity.Error,
                    EditorDiagnosticCodes.OverridesParseFailed,
                    "setup.json resourceKeyOverrides could not be read: " + overridesError,
                    setupPath));
            }

            var result = UISetupSnapshotFactory.Create(section);
            diagnostics.AddRange(result.Diagnostics);

            var projectRoot = ProjectRoot;
            if (result.Snapshot != null &&
                !string.Equals(NormalizeDirectory(result.Snapshot.Root), projectRoot, StringComparison.OrdinalIgnoreCase))
            {
                diagnostics.Add(new EditorDiagnostic(
                    EditorDiagnosticSeverity.Error,
                    EditorDiagnosticCodes.SetupPathInvalid,
                    "setup.json 'root' (" + result.Snapshot.Root + ") does not point at this Unity project ('" + projectRoot +
                    "'). The Unity Editor tooling writes generated sources into the project Assets folder, so root must be the Unity project root.",
                    "root"));
            }

            return new ObfUiSetupResult(setupPath, result.Snapshot, diagnostics);
        }

        /// <summary>Absolute path for a project-relative forward-slash path.</summary>
        public static string ToAbsolutePath(string projectRelativePath)
        {
            var relative = projectRelativePath.Replace('\\', '/').TrimStart('/');
            return Path.Combine(ProjectRoot, relative.Replace('/', Path.DirectorySeparatorChar));
        }

        /// <summary>Project-relative forward-slash path for an absolute path under the Unity project.</summary>
        public static string ToProjectRelativePath(string absolutePath)
        {
            var root = ProjectRoot.Replace('\\', '/').TrimEnd('/');
            var full = Path.GetFullPath(absolutePath).Replace('\\', '/');
            if (full.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase))
                return full.Substring(root.Length + 1);
            return full;
        }

        /// <summary>Normalizes a directory path (forward slashes, no trailing separator).</summary>
        public static string NormalizeDirectory(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;
            return path!.Replace('\\', '/').TrimEnd('/');
        }
    }

    /// <summary>Result of loading setup.json for the Unity Editor tooling.</summary>
    internal sealed class ObfUiSetupResult
    {
        public ObfUiSetupResult(string? setupPath, UISetupSnapshot? snapshot, IReadOnlyList<EditorDiagnostic> diagnostics)
        {
            SetupPath = setupPath;
            Snapshot = snapshot;
            Diagnostics = diagnostics;
        }

        public string? SetupPath { get; }

        public UISetupSnapshot? Snapshot { get; }

        public IReadOnlyList<EditorDiagnostic> Diagnostics { get; }

        public bool Success => Snapshot != null && !HasErrors(Diagnostics);

        public static ObfUiSetupResult Failed(string? setupPath, string message)
            => new ObfUiSetupResult(
                setupPath,
                null,
                new[] { new EditorDiagnostic(EditorDiagnosticSeverity.Error, EditorDiagnosticCodes.InvalidOutputPath, message) });

        private static bool HasErrors(IReadOnlyList<EditorDiagnostic> diagnostics)
        {
            for (var index = 0; index < diagnostics.Count; index++)
            {
                if (diagnostics[index].IsError)
                    return true;
            }

            return false;
        }
    }

    [Serializable]
    internal sealed class ObfUiSetupJsonModel
    {
        public string? root;
        public ObfUiSetupModulesJsonModel? modules;
    }

    [Serializable]
    internal sealed class ObfUiSetupModulesJsonModel
    {
        public ObfUiSetupUnityJsonModel? unity;
    }

    [Serializable]
    internal sealed class ObfUiSetupUnityJsonModel
    {
        public string? scriptOutputRoot;
        public string? uiAssetRoot;
        public string? resourceKeyFormat;
    }

    /// <summary>Small diagnostic presentation helpers for the Editor tooling.</summary>
    internal static class ObfUiDiagnostics
    {
        public static bool HasErrors(IReadOnlyList<EditorDiagnostic> diagnostics)
        {
            for (var index = 0; index < diagnostics.Count; index++)
            {
                if (diagnostics[index].IsError)
                    return true;
            }

            return false;
        }

        public static void Log(IReadOnlyList<EditorDiagnostic> diagnostics)
        {
            for (var index = 0; index < diagnostics.Count; index++)
            {
                var diagnostic = diagnostics[index];
                switch (diagnostic.Severity)
                {
                    case EditorDiagnosticSeverity.Error:
                        Debug.LogError(diagnostic.ToString());
                        break;
                    case EditorDiagnosticSeverity.Warning:
                        Debug.LogWarning(diagnostic.ToString());
                        break;
                    default:
                        Debug.Log(diagnostic.ToString());
                        break;
                }
            }
        }

        public static string Describe(IReadOnlyList<EditorDiagnostic> diagnostics, int maxLines)
        {
            if (diagnostics.Count == 0)
                return "No diagnostics.";

            var builder = new System.Text.StringBuilder();
            var count = 0;
            for (var index = 0; index < diagnostics.Count && count < maxLines; index++, count++)
            {
                builder.AppendLine(diagnostics[index].ToString());
            }

            if (diagnostics.Count > maxLines)
                builder.AppendLine("... " + (diagnostics.Count - maxLines) + " more.");
            return builder.ToString();
        }

        public static void ShowResult(string title, IReadOnlyList<EditorDiagnostic> diagnostics, string successMessage)
        {
            Log(diagnostics);
            if (HasErrors(diagnostics))
            {
                EditorUtility.DisplayDialog(title, "Generation failed:" + Environment.NewLine + Environment.NewLine +
                    Describe(diagnostics, 12), "OK");
                return;
            }

            var warnings = 0;
            for (var index = 0; index < diagnostics.Count; index++)
            {
                if (diagnostics[index].Severity == EditorDiagnosticSeverity.Warning)
                    warnings++;
            }

            var message = successMessage;
            if (warnings > 0)
                message += Environment.NewLine + Environment.NewLine + "Warnings:" + Environment.NewLine +
                           Describe(diagnostics, 8);
            EditorUtility.DisplayDialog(title, message, "OK");
        }
    }
}
