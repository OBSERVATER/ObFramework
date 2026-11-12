#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ObFramework.Editor.Core;
using UnityEditor;
using UnityEngine;

namespace ObFramework.Adapter.Unity.EditorTools
{
    [Serializable]
    internal sealed class ObfUiTableRow
    {
        /// <summary>Transform path inside the Prefab (used to resolve the component reference).</summary>
        public string componentPath = string.Empty;

        /// <summary>Full Unity component type name, for example <c>UnityEngine.UI.Button</c>.</summary>
        public string componentType = string.Empty;

        /// <summary>Index among components of the same type on that GameObject.</summary>
        public int componentOrdinal;

        /// <summary>Unity <see cref="UIComponentKind"/> integer value.</summary>
        public int kind;

        public string fieldName = string.Empty;
        public string viewMember = string.Empty;

        /// <summary>Explicit binding mode integer, or -1 for the control kind default.</summary>
        public int mode = -1;

        public string format = string.Empty;
        public string prefabPath = string.Empty;
        public bool isDestructive;

        public string DisplayKind => ((UIComponentKind)kind).ToString();

        public ObfUiTableRow Clone() => (ObfUiTableRow)MemberwiseClone();
    }

    /// <summary>Serialized binding table + generation options for the current Prefab.</summary>
    [Serializable]
    internal sealed class ObfUiTableState
    {
        public string prefabPath = string.Empty;
        public string viewName = "ShopView";
        public string windowName = string.Empty;
        public string namespaceName = "Game.UI";
        public string architectureType = string.Empty;
        public string level = "Common";
        public string outputDirectory = string.Empty;
        public ObfUiTableRow[] rows = Array.Empty<ObfUiTableRow>();
    }

    /// <summary>
    /// Unity-side binding table state: component scanning from the current selection/active
    /// Prefab, default name derivation, persistence in EditorPrefs and conversion to the pure
    /// <see cref="BindingTableRow"/> model used by <see cref="EditorTemplateGenerator"/>.
    /// </summary>
    internal static class ObfUiBindingTable
    {
        public const string EditorPrefsKey = "ObFramework.UI.BindingTable.v1";
        public const string RegistryNamespacePrefsKey = "ObFramework.UI.RegistryNamespace";
        public const string RegistryPathPrefsKey = "ObFramework.UI.RegistryPath";

        /// <summary>Loads the persisted table, or a fresh default state.</summary>
        public static ObfUiTableState Load()
        {
            var json = EditorPrefs.GetString(EditorPrefsKey, string.Empty);
            if (string.IsNullOrEmpty(json))
                return new ObfUiTableState();

            try
            {
                var state = JsonUtility.FromJson<ObfUiTableState>(json);
                if (state == null)
                    return new ObfUiTableState();
                state.rows ??= Array.Empty<ObfUiTableRow>();
                return state;
            }
            catch (Exception)
            {
                return new ObfUiTableState();
            }
        }

        /// <summary>Persists the table for the next Editor session.</summary>
        public static void Save(ObfUiTableState state)
        {
            try
            {
                EditorPrefs.SetString(EditorPrefsKey, JsonUtility.ToJson(state));
            }
            catch (Exception error)
            {
                Debug.LogWarning("OBF UI: failed to persist the binding table: " + error.Message);
            }
        }

        /// <summary>Prefab asset path of the active selection (Prefab asset or Prefab instance).</summary>
        public static string? GetSelectedPrefabPath()
        {
            var active = Selection.activeGameObject;
            if (active == null)
                return null;

            var prefabPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(active);
            if (string.IsNullOrEmpty(prefabPath) && PrefabUtility.IsPartOfPrefabAsset(active))
                prefabPath = AssetDatabase.GetAssetPath(active);
            return string.IsNullOrEmpty(prefabPath) ? null : prefabPath;
        }

        /// <summary>
        /// Adds every supported control component from the current selection to the table.
        /// Returns the number of added rows; duplicates are skipped.
        /// </summary>
        public static int AddSelectedComponents(ObfUiTableState state, ICollection<string> messages)
        {
            var selected = Selection.gameObjects;
            if (selected == null || selected.Length == 0)
            {
                messages.Add("Select one or more Prefab controls (or the Prefab instance) in the Hierarchy first.");
                return 0;
            }

            var prefabPath = GetSelectedPrefabPath();
            if (!string.IsNullOrEmpty(prefabPath))
                state.prefabPath = prefabPath;

            var existing = new HashSet<string>(StringComparer.Ordinal);
            foreach (var t in state.rows)
                existing.Add(RowIdentity(t));

            var rows = new List<ObfUiTableRow>(state.rows);
            var added = 0;
            foreach (var gameObject in selected)
            {
                if (gameObject == null)
                    continue;

                var components = gameObject.GetComponents<Component>();
                var ordinalByType = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (var component in components)
                {
                    if (component == null)
                        continue;

                    var typeName = component.GetType().FullName ?? string.Empty;
                    var kind = UIComponentKindInfo.FromUnityTypeName(typeName);
                    if (kind == UIComponentKind.Unknown)
                        continue;

                    ordinalByType.TryGetValue(typeName, out var ordinal);
                    ordinalByType[typeName] = ordinal + 1;

                    var row = new ObfUiTableRow
                    {
                        componentPath = GetTransformPath(gameObject.transform, gameObject.transform.root),
                        componentType = typeName,
                        componentOrdinal = ordinal,
                        kind = (int)kind,
                        fieldName = EditorNaming.DefaultFieldName(gameObject.name),
                        viewMember = UIComponentKindInfo.IsCommand(kind)
                            ? EditorNaming.DefaultCommandName(gameObject.name)
                            : EditorNaming.DefaultViewMemberName(gameObject.name),
                        prefabPath = state.prefabPath,
                    };

                    if (!existing.Add(RowIdentity(row)))
                        continue;

                    rows.Add(row);
                    added++;
                }
            }

            state.rows = rows.ToArray();
            if (added == 0)
                messages.Add("No new supported controls were found in the current selection (Button/Text/TMP_Text/InputField/TMP_InputField/Toggle/Slider).");
            return added;
        }

        /// <summary>Converts the persisted table rows to the pure generation model.</summary>
        public static List<BindingTableRow> ToCoreRows(ObfUiTableState state)
        {
            var result = new List<BindingTableRow>(state.rows.Length);
            foreach (var row in state.rows)
            {
                if (row == null)
                    continue;

                result.Add(new BindingTableRow
                {
                    ComponentName = string.IsNullOrEmpty(row.componentPath)
                        ? row.componentType
                        : row.componentPath,
                    Kind = (UIComponentKind)row.kind,
                    FieldName = row.fieldName,
                    ViewMemberName = row.viewMember,
                    Mode = row.mode < 0 ? (UIBindMode?)null : (UIBindMode)row.mode,
                    Format = string.IsNullOrEmpty(row.format) ? null : row.format,
                    PrefabPath = string.IsNullOrEmpty(row.prefabPath) ? state.prefabPath : row.prefabPath,
                    IsDestructive = row.isDestructive,
                });
            }

            return result;
        }

        /// <summary>
        /// Relative path of a Transform below the Prefab root (empty for the root), using '/'
        /// separators. Transform.Find resolves paths relative to the root, so the root name must not
        /// be part of the stored path.
        /// </summary>
        public static string GetTransformPath(Transform transform, Transform? root = null)
        {
            root ??= transform.root;
            var names = new List<string>();
            for (var current = transform; current != null; current = current.parent)
            {
                if (current == root)
                    break;
                names.Insert(0, current.name);
            }

            return BindingTablePath.CombinePath(names);
        }

        /// <summary>Resolves the component for a persisted row inside a Prefab asset, or null.</summary>
        public static Component? ResolveComponent(GameObject prefabRoot, ObfUiTableRow row)
        {
            if (prefabRoot == null || row == null)
                return null;

            // Stored paths may be legacy absolute paths ("ShopRoot/Buy/Button"); Transform.Find is
            // relative to the prefab root, so normalize first. If the path is not found directly,
            // fall back to a hierarchy walk (handles renamed intermediate nodes and older tables)
            // before giving up with the caller's diagnostic.
            var relative = BindingTablePath.MakeRelative(row.componentPath, prefabRoot.name);
            Transform? target = relative.Length == 0 ? prefabRoot.transform : prefabRoot.transform.Find(relative);
            if (target == null && relative.Length > 0)
                target = FindByRelativePath(prefabRoot.transform, relative);
            if (target == null && relative.Length == 0)
                target = prefabRoot.transform;
            if (target == null)
                return null;

            var components = target.GetComponents<Component>();
            var ordinal = 0;
            Component? match = null;
            foreach (var component in components)
            {
                if (component == null)
                    continue;
                var typeName = component.GetType().FullName ?? string.Empty;
                if (!string.Equals(typeName, row.componentType, StringComparison.Ordinal))
                    continue;
                if (ordinal == row.componentOrdinal)
                {
                    match = component;
                    break;
                }

                ordinal++;
            }

            return match;
        }

        private static Transform? FindByRelativePath(Transform root, string relativePath)
        {
            foreach (var candidate in root.GetComponentsInChildren<Transform>(true))
            {
                if (candidate == null || candidate == root)
                    continue;
                if (string.Equals(GetTransformPath(candidate, root), relativePath, StringComparison.Ordinal))
                    return candidate;
            }

            return null;
        }

        private static string RowIdentity(ObfUiTableRow row)
            => row.componentPath + "|" + row.componentType + "|" + row.componentOrdinal;
    }
}
