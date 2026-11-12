#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ObFramework.Editor.Core;
using UnityEditor;
using UnityEngine;

namespace ObFramework.Adapter.Unity.EditorTools
{
    /// <summary>
    /// T16: generates the replaceable static UI registry from setup.json and the generated
    /// <c>[BindView]</c> window controllers, and validates the project's generated UI bindings.
    /// <para>
    /// The View/VC pairs are discovered from the compiled Unity assemblies through
    /// <see cref="TypeCache"/> and <see cref="CustomAttributeData"/> (the binding attribute
    /// constructor arguments are read from metadata, so the Editor glue does not need a compile-time
    /// reference to the runtime adapter assembly). The generated <c>RegisterAll</c> only calls the
    /// Unity adapter registration API.
    /// </para>
    /// </summary>
    internal static class ObfUiRegistryCommands
    {
        public const string Title = "OBF UI Registry";
        public const string DefaultNamespace = "Game.Generated.UI";

        private const string BindViewAttributeFullName = "ObFramework.Adapter.Unity.ViewBinding.BindViewAttribute";
        // Reflection-based base-type checks (the Editor asmdef does not reference the runtime adapter
        // assemblies); these names are part of the deployed framework contract.
        private const string WindowBaseTypeFullName = "ObFramework.Integrated.UI.AUIWindow";
        private const string ViewBaseTypeFullName = "ObFramework.Integrated.UI.AUIView";
        private const string UIBindDataAttributeFullName = "ObFramework.Adapter.Unity.ViewBinding.UIBindDataAttribute";
        private const string UIBindCommandAttributeFullName = "ObFramework.Adapter.Unity.ViewBinding.UIBindCommandAttribute";

        /// <summary>Discovers generated View/VC pairs and writes the static registry source.</summary>
        public static void Generate()
        {
            var setup = ObfUiSetup.Load();
            var diagnostics = new List<EditorDiagnostic>(setup.Diagnostics);
            if (!setup.Success || setup.Snapshot == null)
            {
                ObfUiDiagnostics.Log(diagnostics);
                EditorUtility.DisplayDialog(Title,
                    "setup.json could not be loaded:" + Environment.NewLine + Environment.NewLine +
                    ObfUiDiagnostics.Describe(diagnostics, 12), "OK");
                return;
            }

            var entries = FindGeneratedViewControllers(diagnostics);
            var input = new UIRegistryGenerationInput
            {
                Setup = setup.Snapshot,
                Entries = entries,
                Namespace = EditorPrefs.GetString(ObfUiBindingTable.RegistryNamespacePrefsKey, DefaultNamespace),
                OutputPath = EditorPrefs.GetString(ObfUiBindingTable.RegistryPathPrefsKey, string.Empty),
            };

            var output = UIRegistrySourceGenerator.Generate(input);
            diagnostics.AddRange(output.Diagnostics);
            if (!output.Success || output.File == null)
            {
                ObfUiDiagnostics.Log(diagnostics);
                EditorUtility.DisplayDialog(Title,
                    "UI registry generation failed:" + Environment.NewLine + Environment.NewLine +
                    ObfUiDiagnostics.Describe(diagnostics, 12), "OK");
                return;
            }

            var written = ObfUiGeneration.WriteFiles(new[] { output.File }, out var writeDiagnostics);
            diagnostics.AddRange(writeDiagnostics);
            ObfUiDiagnostics.Log(diagnostics);
            if (ObfUiDiagnostics.HasErrors(diagnostics))
            {
                EditorUtility.DisplayDialog(Title, "UI registry could not be written:" + Environment.NewLine +
                    ObfUiDiagnostics.Describe(writeDiagnostics, 12), "OK");
                return;
            }

            AssetDatabase.SaveAssets();
            var message = new System.Text.StringBuilder();
            message.AppendLine("Registered " + output.Registrations.Count + " UI view(s) -> '" + output.File.Path + "'.");
            if (written == 0)
                message.AppendLine("The registry file was already up to date.");
            if (output.Diagnostics.Count > 0)
            {
                message.AppendLine();
                message.Append(ObfUiDiagnostics.Describe(output.Diagnostics, 10));
            }

            EditorUtility.DisplayDialog(Title, message.ToString(), "OK");
        }

        /// <summary>Validates setup.json, the generated registry mapping and generated Prefab bindings.</summary>
        public static void Validate()
        {
            var diagnostics = new List<EditorDiagnostic>();
            var setup = ObfUiSetup.Load();
            diagnostics.AddRange(setup.Diagnostics);

            if (setup.Snapshot != null)
            {
                var entries = FindGeneratedViewControllers(diagnostics);
                var input = new UIRegistryGenerationInput
                {
                    Setup = setup.Snapshot,
                    Entries = entries,
                    Namespace = EditorPrefs.GetString(ObfUiBindingTable.RegistryNamespacePrefsKey, DefaultNamespace),
                    OutputPath = EditorPrefs.GetString(ObfUiBindingTable.RegistryPathPrefsKey, string.Empty),
                };
                var output = UIRegistrySourceGenerator.Generate(input);
                diagnostics.AddRange(output.Diagnostics);
                if (output.File != null)
                {
                    var absolute = ObfUiSetup.ToAbsolutePath(output.File.Path);
                    if (!System.IO.File.Exists(absolute))
                    {
                        diagnostics.Add(new EditorDiagnostic(
                            EditorDiagnosticSeverity.Warning,
                            EditorDiagnosticCodes.RegistryHasNoEntries,
                            "The generated UI registry file '" + output.File.Path + "' does not exist yet. Run Tools/OBF/UI/Generate UI Registry.",
                            output.File.Path));
                    }
                }

                diagnostics.AddRange(ValidatePrefabBindings(setup.Snapshot, entries));
            }

            ObfUiDiagnostics.Log(diagnostics);
            var message = new System.Text.StringBuilder();
            message.AppendLine("Generated UI views found: " +
                               (setup.Snapshot == null ? 0 : FindGeneratedViewControllers(new List<EditorDiagnostic>()).Count));
            message.AppendLine();
            message.Append(ObfUiDiagnostics.HasErrors(diagnostics) ? "Validation found errors:" : "Validation succeeded.");
            message.AppendLine();
            message.Append(ObfUiDiagnostics.Describe(diagnostics, 20));
            EditorUtility.DisplayDialog(Title + " - validate", message.ToString(), "OK");
        }

        /// <summary>Discovers non-abstract <c>MonoBehaviour</c> types carrying <c>[BindView(view, contract)]</c>.</summary>
        public static List<UIRegistryEntry> FindGeneratedViewControllers(ICollection<EditorDiagnostic> diagnostics)
        {
            var entries = new List<UIRegistryEntry>();
            TypeCache.TypeCollection? candidates = null;
            try
            {
                candidates = TypeCache.GetTypesDerivedFrom<MonoBehaviour>();
            }
            catch (Exception error)
            {
                diagnostics.Add(new EditorDiagnostic(
                    EditorDiagnosticSeverity.Warning,
                    EditorDiagnosticCodes.InvalidRegistryEntry,
                    "Unity type cache could not be read: " + error.Message));
                return entries;
            }

            foreach (var type in candidates)
            {
                if (type == null || type.IsAbstract)
                    continue;

                CustomAttributeData? bindView = null;
                foreach (var attribute in type.GetCustomAttributesData())
                {
                    if (string.Equals(attribute.AttributeType.FullName, BindViewAttributeFullName, StringComparison.Ordinal))
                    {
                        bindView = attribute;
                        break;
                    }
                }

                if (bindView == null)
                    continue;

                // Only window VCs belong in the static registry: child/reusable components also carry
                // [BindView] for their own business View, but they are not opened by UIPlugin and must
                // not be registered as windows.
                if (!DerivesFromNamed(type, WindowBaseTypeFullName))
                    continue;

                if (bindView.ConstructorArguments.Count != 2 ||
                    !(bindView.ConstructorArguments[0].Value is Type viewType) ||
                    !(bindView.ConstructorArguments[1].Value is Type contractType))
                {
                    diagnostics.Add(new EditorDiagnostic(
                        EditorDiagnosticSeverity.Error,
                        EditorDiagnosticCodes.InvalidRegistryEntry,
                        "Unable to read the [BindView] View/contract types from '" + type.FullName + "'.",
                        type.FullName ?? type.Name));
                    continue;
                }

                if (!viewType.IsClass || viewType.IsAbstract || !DerivesFromNamed(viewType, ViewBaseTypeFullName))
                {
                    diagnostics.Add(new EditorDiagnostic(
                        EditorDiagnosticSeverity.Error,
                        EditorDiagnosticCodes.InvalidRegistryEntry,
                        "'" + type.FullName + "' binds invalid business View type '" + viewType.FullName +
                        "' (a window VC must bind an AUIView-derived business View).",
                        type.FullName ?? type.Name));
                    continue;
                }

                if (!contractType.IsInterface)
                {
                    diagnostics.Add(new EditorDiagnostic(
                        EditorDiagnosticSeverity.Error,
                        EditorDiagnosticCodes.InvalidRegistryEntry,
                        "'" + type.FullName + "' binds '" + contractType.FullName + "' which is not an interface.",
                        type.FullName ?? type.Name));
                    continue;
                }

                entries.Add(new UIRegistryEntry
                {
                    ViewTypeFullName = viewType.FullName ?? viewType.Name,
                    WindowTypeFullName = type.FullName ?? type.Name,
                });
            }

            entries.Sort((left, right) => string.CompareOrdinal(left.ViewTypeFullName, right.ViewTypeFullName));
            return entries;
        }

        private static List<EditorDiagnostic> ValidatePrefabBindings(
            UISetupSnapshot snapshot,
            IReadOnlyList<UIRegistryEntry> entries)
        {
            var diagnostics = new List<EditorDiagnostic>();
            foreach (var entry in entries)
            {
                var viewShortName = LastSegment(entry.ViewTypeFullName);
                var prefabPath = snapshot.UIAssetRoot + "/" + viewShortName + ".prefab";
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (prefab == null)
                {
                    diagnostics.Add(new EditorDiagnostic(
                        EditorDiagnosticSeverity.Warning,
                        EditorDiagnosticCodes.MissingPrefabReference,
                        "No Prefab found for View '" + viewShortName + "' at '" + prefabPath + "'. " +
                        "Rename the Prefab to match the View or adjust setup.json uiAssetRoot.",
                        viewShortName));
                    continue;
                }

                var windowName = LastSegment(entry.WindowTypeFullName);
                var windowComponent = FindComponent(prefab, windowName);
                if (windowComponent == null)
                {
                    diagnostics.Add(new EditorDiagnostic(
                        EditorDiagnosticSeverity.Warning,
                        EditorDiagnosticCodes.InvalidBindingRow,
                        "Prefab '" + prefabPath + "' has no component of the generated window type '" + windowName + "'.",
                        viewShortName));
                    continue;
                }

                var serialized = new SerializedObject(windowComponent);
                foreach (var field in windowComponent.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (!HasBindingAttribute(field))
                        continue;

                    var property = serialized.FindProperty(field.Name);
                    if (property != null && property.objectReferenceValue == null)
                    {
                        diagnostics.Add(new EditorDiagnostic(
                            EditorDiagnosticSeverity.Warning,
                            EditorDiagnosticCodes.MissingPrefabReference,
                            "Serialized control reference '" + field.Name + "' on '" + windowName + "' is not assigned in '" + prefabPath + "'.",
                            field.Name));
                    }
                }
            }

            return diagnostics;
        }

        private static bool HasBindingAttribute(FieldInfo field)
        {
            foreach (var attribute in field.GetCustomAttributesData())
            {
                var name = attribute.AttributeType.FullName;
                if (string.Equals(name, UIBindDataAttributeFullName, StringComparison.Ordinal) ||
                    string.Equals(name, UIBindCommandAttributeFullName, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static Component? FindComponent(GameObject root, string typeName)
        {
            foreach (var component in root.GetComponentsInChildren<Component>(true))
            {
                if (component != null && string.Equals(component.GetType().Name, typeName, StringComparison.Ordinal))
                    return component;
            }

            return null;
        }

        private static string LastSegment(string qualifiedName)
        {
            var split = qualifiedName.LastIndexOf('.');
            return split < 0 ? qualifiedName : qualifiedName.Substring(split + 1);
        }

        /// <summary>True when <paramref name="type"/> or a base type has the fully qualified name.</summary>
        private static bool DerivesFromNamed(Type? type, string fullName)
        {
            for (var current = type; current != null; current = current.BaseType)
            {
                if (string.Equals(current.FullName, fullName, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }
    }
}
