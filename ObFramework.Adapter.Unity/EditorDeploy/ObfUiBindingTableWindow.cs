#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using ObFramework.Editor.Core;
using UnityEditor;
using UnityEngine;

namespace ObFramework.Adapter.Unity.EditorTools
{
    /// <summary>
    /// YIUI-like CDE binding table window: add/remove Prefab control references, choose the
    /// Architecture/UI level/output folder and trigger View+VC generation or validation.
    /// <para>
    /// The table (rows + generation options) persists in EditorPrefs; the heavy lifting is done by
    /// the pure <see cref="ObFramework.Editor.Core"/> generators and the thin
    /// <see cref="ObfUiGeneration"/> glue.
    /// </para>
    /// </summary>
    internal sealed class ObfUiBindingTableWindow : EditorWindow
    {
        private static readonly string[] Levels = { "Scene", "Common", "Overlay" };

        private ObfUiTableState _state = new ObfUiTableState();
        private Vector2 _scroll;
        private string _message = string.Empty;

        public static void Open()
        {
            var window = GetWindow<ObfUiBindingTableWindow>("OBF UI Binding Table");
            window.minSize = new Vector2(760, 460);
            window.Show();
        }

        /// <summary>Opens the window and adds every supported control from the current selection.</summary>
        public static void OpenAndAddSelection()
        {
            var window = GetWindow<ObfUiBindingTableWindow>("OBF UI Binding Table");
            window.minSize = new Vector2(760, 460);
            window.Show();
            window.AddSelectedControls();
        }

        /// <summary>Opens the window and adds the supplied components (Used by the component context menu).</summary>
        public static void OpenAndAddComponents(IReadOnlyList<Component>? components)
        {
            if (components != null && components.Count > 0)
            {
                var gameObjects = new List<GameObject>();
                foreach (var component in components)
                {
                    if (component != null && !gameObjects.Contains(component.gameObject))
                        gameObjects.Add(component.gameObject);
                }

                Selection.objects = gameObjects.ToArray();
            }

            OpenAndAddSelection();
        }

        private void OnEnable()
        {
            _state = ObfUiBindingTable.Load();
            if (string.IsNullOrEmpty(_state.outputDirectory))
            {
                var setup = ObfUiSetup.Load();
                if (setup.Snapshot != null)
                    _state.outputDirectory = setup.Snapshot.ScriptOutputRoot;
            }

            if (string.IsNullOrEmpty(_state.prefabPath))
            {
                var selectedPrefab = ObfUiBindingTable.GetSelectedPrefabPath();
                if (!string.IsNullOrEmpty(selectedPrefab))
                    _state.prefabPath = selectedPrefab!;
            }
        }

        private void AddSelectedControls()
        {
            var messages = new List<string>();
            var added = ObfUiBindingTable.AddSelectedComponents(_state, messages);
            ObfUiBindingTable.Save(_state);
            _message = added > 0
                ? "Added " + added + " control(s) to the binding table."
                : messages.Count > 0 ? messages[0] : "No supported controls in the current selection.";
            Repaint();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("OBF UI 2.0 - CDE binding table", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Select Prefab controls in the Hierarchy and click Add Selected Controls. " +
                "Each row becomes a serialized control field on the generated window plus a [UIBindData]/[UIBindCommand] " +
                "binding consumed by the Roslyn generator.",
                MessageType.Info);

            DrawPrefabAndOptions();
            DrawTable();
            DrawActions();

            if (GUI.changed)
                ObfUiBindingTable.Save(_state);

            if (!string.IsNullOrEmpty(_message))
                EditorGUILayout.HelpBox(_message, MessageType.None);
        }

        private void DrawPrefabAndOptions()
        {
            EditorGUILayout.Space();
            EditorGUILayout.BeginHorizontal();
            _state.prefabPath = EditorGUILayout.TextField("Prefab", _state.prefabPath);
            if (GUILayout.Button("Use Selected Prefab", GUILayout.Width(160)))
            {
                var selected = ObfUiBindingTable.GetSelectedPrefabPath();
                if (string.IsNullOrEmpty(selected))
                    _message = "The current selection is not part of a Prefab asset.";
                else
                {
                    _state.prefabPath = selected!;
                    for (var index = 0; index < _state.rows.Length; index++)
                        _state.rows[index].prefabPath = _state.prefabPath;
                }
            }

            EditorGUILayout.EndHorizontal();

            _state.viewName = EditorGUILayout.TextField("View Name", _state.viewName);
            _state.windowName = EditorGUILayout.TextField(
                "Window Name",
                string.IsNullOrEmpty(_state.windowName) ? EditorNaming.DefaultWindowName(_state.viewName) : _state.windowName);
            _state.namespaceName = EditorGUILayout.TextField("Namespace", _state.namespaceName);
            _state.architectureType = EditorGUILayout.TextField("Architecture", _state.architectureType);
            EditorGUILayout.HelpBox(
                "Architecture is the fully qualified type, e.g. Game.GameArchitecture. The generated window returns Game.GameArchitecture.Interface.",
                MessageType.None);

            var levelIndex = Array.IndexOf(Levels, _state.level);
            if (levelIndex < 0)
                levelIndex = 1;
            levelIndex = EditorGUILayout.Popup("UI Level", levelIndex, Levels);
            _state.level = Levels[levelIndex];

            EditorGUILayout.BeginHorizontal();
            _state.outputDirectory = EditorGUILayout.TextField("Output Directory", _state.outputDirectory);
            if (GUILayout.Button("Use setup.json", GUILayout.Width(120)))
            {
                var setup = ObfUiSetup.Load();
                if (setup.Snapshot != null)
                {
                    _state.outputDirectory = setup.Snapshot.ScriptOutputRoot;
                    _message = "Using scriptOutputRoot from '" + setup.SetupPath + "'.";
                }
                else
                {
                    _message = "setup.json could not be loaded; check the Validate output.";
                }
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawTable()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Components (" + _state.rows.Length + ")", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Component / Kind", GUILayout.MinWidth(200));
            EditorGUILayout.LabelField("Field", GUILayout.Width(150));
            EditorGUILayout.LabelField("View Member / Command", GUILayout.Width(180));
            EditorGUILayout.LabelField("Format", GUILayout.Width(70));
            EditorGUILayout.LabelField("Destructive", GUILayout.Width(70));
            EditorGUILayout.EndHorizontal();

            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.MinHeight(180));
            var rows = new List<ObfUiTableRow>(_state.rows);
            var removeIndex = -1;
            for (var index = 0; index < rows.Count; index++)
            {
                var row = rows[index];
                if (row == null)
                    continue;

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(row.componentPath + "  (" + row.DisplayKind + ")", GUILayout.MinWidth(200));
                row.fieldName = EditorGUILayout.TextField(row.fieldName, GUILayout.Width(150));
                row.viewMember = EditorGUILayout.TextField(row.viewMember, GUILayout.Width(180));
                row.format = EditorGUILayout.TextField(row.format, GUILayout.Width(70));
                row.isDestructive = EditorGUILayout.Toggle(row.isDestructive, GUILayout.Width(70));
                if (GUILayout.Button("Remove", GUILayout.Width(70)))
                    removeIndex = index;
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndScrollView();
            if (removeIndex >= 0)
            {
                rows.RemoveAt(removeIndex);
                _state.rows = rows.ToArray();
            }
        }

        private void DrawActions()
        {
            EditorGUILayout.Space();
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Add Selected Controls", GUILayout.Height(24)))
                AddSelectedControls();
            if (GUILayout.Button("Clear Table", GUILayout.Height(24)))
            {
                if (EditorUtility.DisplayDialog("OBF UI", "Remove all binding table rows?", "Clear", "Cancel"))
                {
                    _state.rows = Array.Empty<ObfUiTableRow>();
                    ObfUiBindingTable.Save(_state);
                }
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Generate View + VC", GUILayout.Height(28)))
                ObfUiGeneration.Generate(_state, showDialogs: true);
            if (GUILayout.Button("Validate", GUILayout.Height(28)))
                ObfUiGeneration.Validate(_state);
            EditorGUILayout.EndHorizontal();
        }
    }
}
