#nullable enable
using System;
using UnityEditor;
using UnityEngine;

namespace ObFramework.Adapter.Unity.EditorTools
{
    /// <summary>
    /// Unity Editor menu entries for the OBF UI 2.0 CDE workflow:
    /// <c>Tools/OBF/UI/Bind Selected Controls</c>, <c>Generate View+VC</c>,
    /// <c>Generate UI Registry</c> and <c>Validate</c>.
    /// </summary>
    internal static class ObfUiMenus
    {
        [MenuItem("Tools/OBF/UI/Bind Selected Controls", false, 100)]
        public static void BindSelectedControls()
        {
            ObfUiBindingTableWindow.OpenAndAddSelection();
        }

        [MenuItem("Tools/OBF/UI/Binding Table", false, 101)]
        public static void OpenBindingTable()
        {
            ObfUiBindingTableWindow.Open();
        }

        [MenuItem("Tools/OBF/UI/Generate View+VC", false, 120)]
        public static void GenerateViewAndWindow()
        {
            var state = ObfUiBindingTable.Load();
            if (state.rows.Length == 0 &&
                !EditorUtility.DisplayDialog(
                    "OBF UI",
                    "The binding table is empty. Generate empty editable View/VC templates anyway?",
                    "Generate",
                    "Open Binding Table"))
            {
                ObfUiBindingTableWindow.Open();
                return;
            }

            ObfUiGeneration.Generate(state, showDialogs: true);
        }

        [MenuItem("Tools/OBF/UI/Generate UI Registry", false, 121)]
        public static void GenerateUiRegistry()
        {
            ObfUiRegistryCommands.Generate();
        }

        [MenuItem("Tools/OBF/UI/Validate", false, 140)]
        public static void ValidateUiSetup()
        {
            ObfUiGeneration.Validate(ObfUiBindingTable.Load());
            ObfUiRegistryCommands.Validate();
        }

        [MenuItem("CONTEXT/Component/OBF: Add to UI Binding Table", false, 40)]
        public static void AddComponentToBindingTable(MenuCommand command)
        {
            var component = command.context as Component;
            ObfUiBindingTableWindow.OpenAndAddComponents(
                component == null ? Array.Empty<Component>() : new[] { component });
        }
    }
}
