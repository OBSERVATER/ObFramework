#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ObFramework.Editor.Core;
using UnityEditor;
using UnityEngine;

namespace ObFramework.Adapter.Unity.EditorTools
{
    /// <summary>
    /// Unity Editor orchestration for CDE generation: reads the existing editable sources, calls the
    /// pure <see cref="EditorTemplateGenerator"/>, previews destructive changes in a dialog, writes
    /// the resulting <c>.cs</c> files through the AssetDatabase and assigns the serialized Prefab
    /// component references.
    /// <para>
    /// The same files can be regenerated safely: user-authored code is preserved, missing
    /// declarations are added and renames/deletes require explicit confirmation.
    /// </para>
    /// </summary>
    internal static class ObfUiGeneration
    {
        public const string Title = "OBF UI";

        /// <summary>Generates/updates View+VC+contract from the binding table. Returns true when files were written.</summary>
        public static bool Generate(ObfUiTableState state, bool showDialogs)
        {
            var setup = ObfUiSetup.Load();
            if (!setup.Success || setup.Snapshot == null)
            {
                ObfUiDiagnostics.Log(setup.Diagnostics);
                if (showDialogs)
                {
                    EditorUtility.DisplayDialog(Title,
                        "setup.json could not be loaded:" + Environment.NewLine + Environment.NewLine +
                        ObfUiDiagnostics.Describe(setup.Diagnostics, 12), "OK");
                }

                return false;
            }

            var request = BuildRequest(state, setup.Snapshot, out var paths);
            var result = EditorTemplateGenerator.Generate(request);
            if (!result.Success)
            {
                ObfUiDiagnostics.Log(result.Diagnostics);
                if (showDialogs)
                {
                    EditorUtility.DisplayDialog(Title,
                        "Generation failed:" + Environment.NewLine + Environment.NewLine +
                        ObfUiDiagnostics.Describe(result.Diagnostics, 12), "OK");
                }

                return false;
            }

            if (result.HasPendingDestructiveChanges)
            {
                var pending = result.DestructiveChanges.Where(change => !change.Applied).ToArray();
                var preview = new StringBuilder();
                foreach (var change in pending)
                    preview.AppendLine(change.ToString());
                preview.AppendLine();
                preview.AppendLine("Apply the destructive renames/deletes? Existing serialized fields are kept until you confirm.");
                ObfUiDiagnostics.Log(result.Diagnostics.Distinct().ToArray());

                if (showDialogs)
                {
                    var choice = EditorUtility.DisplayDialogComplex(
                        Title + " - destructive changes",
                        preview.ToString(),
                        "Apply changes",
                        "Cancel",
                        "Keep old declarations");
                    if (choice == 1)
                        return false;
                    if (choice == 0)
                    {
                        request.ConfirmedDestructiveChangeIds = pending.Select(change => change.Id).ToList();
                        result = EditorTemplateGenerator.Generate(request);
                        if (!result.Success)
                        {
                            ObfUiDiagnostics.Log(result.Diagnostics);
                            EditorUtility.DisplayDialog(Title, "Generation failed after confirming changes.", "OK");
                            return false;
                        }
                    }
                }
            }

            var written = WriteFiles(result.Files, out var writeErrors);
            if (writeErrors.Count > 0)
            {
                ObfUiDiagnostics.Log(writeErrors);
                if (showDialogs)
                {
                    EditorUtility.DisplayDialog(Title,
                        "Some generated files could not be written:" + Environment.NewLine +
                        ObfUiDiagnostics.Describe(writeErrors, 12), "OK");
                }

                return false;
            }

            AssignSerializedReferences(state, out var referenceDiagnostics);
            ObfUiDiagnostics.Log(referenceDiagnostics);

            ObfUiBindingTable.Save(state);
            AssetDatabase.SaveAssets();
            if (showDialogs)
            {
                var summary = new StringBuilder();
                summary.AppendLine("Generated " + written + " file(s) under '" + paths.OutputDirectory + "'.");
                summary.AppendLine();
                summary.AppendLine("View:     " + paths.ViewPath);
                summary.AppendLine("Window:   " + paths.WindowPath);
                summary.AppendLine("Contract: " + paths.ContractPath);
                if (referenceDiagnostics.Count > 0)
                {
                    summary.AppendLine();
                    summary.AppendLine("Serialized reference assignment:");
                    summary.Append(ObfUiDiagnostics.Describe(referenceDiagnostics, 8));
                }

                EditorUtility.DisplayDialog(Title, summary.ToString(), "OK");
            }

            return true;
        }

        /// <summary>Dry-run validation: reports setup/generation diagnostics and pending destructive changes without writing files.</summary>
        public static void Validate(ObfUiTableState state)
        {
            var setup = ObfUiSetup.Load();
            var diagnostics = new List<EditorDiagnostic>(setup.Diagnostics);
            var message = new StringBuilder();

            if (setup.Snapshot != null)
            {
                var request = BuildRequest(state, setup.Snapshot, out var paths);
                var result = EditorTemplateGenerator.Generate(request);
                diagnostics.AddRange(result.Diagnostics);
                message.AppendLine("Output directory: " + paths.OutputDirectory);
                message.AppendLine("View:     " + paths.ViewPath);
                message.AppendLine("Window:   " + paths.WindowPath);
                message.AppendLine("Contract: " + paths.ContractPath);
                if (result.HasPendingDestructiveChanges)
                {
                    message.AppendLine();
                    message.AppendLine("Pending destructive changes (not applied):");
                    foreach (var change in result.DestructiveChanges)
                        message.AppendLine(change.ToString());
                }
            }

            ObfUiDiagnostics.Log(diagnostics);
            message.AppendLine();
            message.Append(ObfUiDiagnostics.HasErrors(diagnostics)
                ? "Validation found errors:"
                : "Validation succeeded.");
            message.AppendLine();
            message.Append(ObfUiDiagnostics.Describe(diagnostics, 16));
            EditorUtility.DisplayDialog(Title + " - validate", message.ToString(), "OK");
        }

        /// <summary>Builds the pure generation request and resolves output paths from setup/binding table.</summary>
        public static EditorGenerationRequest BuildRequest(
            ObfUiTableState state,
            UISetupSnapshot snapshot,
            out ObfUiGenerationPaths paths)
        {
            var viewName = string.IsNullOrWhiteSpace(state.viewName) ? "ShopView" : state.viewName.Trim();
            var windowName = string.IsNullOrWhiteSpace(state.windowName)
                ? EditorNaming.DefaultWindowName(viewName)
                : state.windowName.Trim();
            var contractName = EditorNaming.DefaultContractName(viewName);
            var outputDirectory = string.IsNullOrWhiteSpace(state.outputDirectory)
                ? snapshot.ScriptOutputRoot
                : state.outputDirectory.Trim().Replace('\\', '/').TrimEnd('/');

            paths = new ObfUiGenerationPaths(
                outputDirectory,
                Combine(outputDirectory, viewName + ".cs"),
                Combine(outputDirectory, windowName + ".cs"),
                Combine(outputDirectory, contractName + ".cs"));

            var request = new EditorGenerationRequest
            {
                ViewName = viewName,
                WindowName = windowName,
                Namespace = state.namespaceName,
                ContractName = contractName,
                GenerateContractInterface = true,
                ArchitectureTypeFullName = state.architectureType,
                Level = state.level,
                OutputDirectory = outputDirectory,
                ViewOutputPath = paths.ViewPath,
                WindowOutputPath = paths.WindowPath,
                ContractOutputPath = paths.ContractPath,
                PrefabPath = string.IsNullOrEmpty(state.prefabPath) ? null : state.prefabPath,
                ExistingViewSource = ReadExistingFile(paths.ViewPath),
                ExistingWindowSource = ReadExistingFile(paths.WindowPath),
                ExistingContractSource = ReadExistingFile(paths.ContractPath),
                Rows = ObfUiBindingTable.ToCoreRows(state),
            };
            return request;
        }

        /// <summary>Writes generated files through the AssetDatabase; skips unchanged files.</summary>
        public static int WriteFiles(IReadOnlyList<GeneratedFile> files, out List<EditorDiagnostic> diagnostics)
        {
            diagnostics = new List<EditorDiagnostic>();
            var written = 0;
            foreach (var file in files)
            {
                var projectRelative = file.Path.Replace('\\', '/');
                if (!projectRelative.StartsWith("Assets/", StringComparison.Ordinal))
                {
                    diagnostics.Add(new EditorDiagnostic(
                        EditorDiagnosticSeverity.Error,
                        EditorDiagnosticCodes.InvalidOutputPath,
                        "Generated file '" + projectRelative + "' is outside the Unity project Assets folder.",
                        projectRelative));
                    continue;
                }

                var absolute = ObfUiSetup.ToAbsolutePath(projectRelative);
                var directory = Path.GetDirectoryName(absolute);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                if (!file.IsModified && File.Exists(absolute))
                    continue;

                try
                {
                    File.WriteAllText(absolute, file.Content, new UTF8Encoding(false));
                    AssetDatabase.ImportAsset(projectRelative, ImportAssetOptions.ForceUpdate);
                    written++;
                }
                catch (Exception error)
                {
                    diagnostics.Add(new EditorDiagnostic(
                        EditorDiagnosticSeverity.Error,
                        EditorDiagnosticCodes.InvalidOutputPath,
                        "Failed to write '" + projectRelative + "': " + error.Message,
                        projectRelative));
                }
            }

            return written;
        }

        /// <summary>
        /// Assigns the scanned Prefab component references to the generated serialized fields.
        /// Safe no-op when the Prefab or the generated window component is not available yet (for
        /// example first generation before the script is imported/compiled).
        /// </summary>
        public static void AssignSerializedReferences(ObfUiTableState state, out List<EditorDiagnostic> diagnostics)
        {
            diagnostics = new List<EditorDiagnostic>();
            if (string.IsNullOrWhiteSpace(state.prefabPath))
            {
                diagnostics.Add(new EditorDiagnostic(
                    EditorDiagnosticSeverity.Info,
                    EditorDiagnosticCodes.MissingPrefabReference,
                    "No Prefab is selected for the binding table; serialized references were not assigned."));
                return;
            }

            var prefabPath = state.prefabPath.Replace('\\', '/');
            var prefabRoot = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefabRoot == null)
            {
                diagnostics.Add(new EditorDiagnostic(
                    EditorDiagnosticSeverity.Warning,
                    EditorDiagnosticCodes.InvalidPrefabPath,
                    "Prefab '" + prefabPath + "' could not be loaded; serialized references were not assigned.",
                    prefabPath));
                return;
            }

            var contents = PrefabUtility.LoadPrefabContents(prefabPath);
            if (contents == null)
            {
                diagnostics.Add(new EditorDiagnostic(
                    EditorDiagnosticSeverity.Warning,
                    EditorDiagnosticCodes.InvalidPrefabPath,
                    "Prefab contents for '" + prefabPath + "' could not be loaded.",
                    prefabPath));
                return;
            }

            try
            {
                var windowComponent = FindWindowComponent(contents, state.windowName);
                if (windowComponent == null)
                {
                    diagnostics.Add(new EditorDiagnostic(
                        EditorDiagnosticSeverity.Warning,
                        EditorDiagnosticCodes.InvalidBindingRow,
                        "The generated window component '" + state.windowName + "' is not attached to Prefab '" +
                        prefabPath + "'. Attach the generated script to the Prefab root and run generation again to assign serialized references.",
                        prefabPath));
                    return;
                }

                var serialized = new SerializedObject(windowComponent);
                var assigned = 0;
                foreach (var row in state.rows)
                {
                    var component = ObfUiBindingTable.ResolveComponent(contents, row);
                    if (component == null)
                    {
                        diagnostics.Add(new EditorDiagnostic(
                            EditorDiagnosticSeverity.Warning,
                            EditorDiagnosticCodes.InvalidBindingRow,
                            "Component '" + row.componentPath + "' (" + row.componentType + ") was not found in the Prefab.",
                            row.componentPath));
                        continue;
                    }

                    var property = serialized.FindProperty(row.fieldName);
                    if (property == null)
                    {
                        diagnostics.Add(new EditorDiagnostic(
                            EditorDiagnosticSeverity.Warning,
                            EditorDiagnosticCodes.InvalidBindingRow,
                            "Serialized field '" + row.fieldName + "' was not found on '" + state.windowName +
                            "' yet. Re-run generation after the generated window script compiles.",
                            row.fieldName));
                        continue;
                    }

                    property.objectReferenceValue = component;
                    assigned++;
                }

                if (assigned > 0)
                {
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                    PrefabUtility.SaveAsPrefabAsset(contents, prefabPath);
                    diagnostics.Add(new EditorDiagnostic(
                        EditorDiagnosticSeverity.Info,
                        EditorDiagnosticCodes.DestructiveChangeApplied,
                        "Assigned " + assigned + " serialized component reference(s) on Prefab '" + prefabPath + "'."));
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        private static Component? FindWindowComponent(GameObject root, string windowName)
        {
            if (root == null || string.IsNullOrWhiteSpace(windowName))
                return null;

            var components = root.GetComponentsInChildren<Component>(true);
            foreach (var component in components)
            {
                if (component == null)
                    continue;
                if (string.Equals(component.GetType().Name, windowName, StringComparison.Ordinal))
                    return component;
            }

            return null;
        }

        private static string? ReadExistingFile(string projectRelativePath)
        {
            var absolute = ObfUiSetup.ToAbsolutePath(projectRelativePath);
            if (!File.Exists(absolute))
                return null;
            try
            {
                return File.ReadAllText(absolute);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static string Combine(string directory, string fileName)
        {
            var normalized = (directory ?? string.Empty).Replace('\\', '/').TrimEnd('/');
            return normalized.Length == 0 ? fileName : normalized + "/" + fileName;
        }
    }

    /// <summary>Resolved output paths for one View/window/contract generation.</summary>
    internal sealed class ObfUiGenerationPaths
    {
        public ObfUiGenerationPaths(string outputDirectory, string viewPath, string windowPath, string contractPath)
        {
            OutputDirectory = outputDirectory;
            ViewPath = viewPath;
            WindowPath = windowPath;
            ContractPath = contractPath;
        }

        public string OutputDirectory { get; }
        public string ViewPath { get; }
        public string WindowPath { get; }
        public string ContractPath { get; }
    }
}
