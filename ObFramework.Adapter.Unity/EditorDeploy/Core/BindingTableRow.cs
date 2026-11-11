#nullable enable
using System;

namespace ObFramework.Editor.Core
{
    /// <summary>Engine control kinds supported by the Unity CDE binding tooling.</summary>
    public enum UIComponentKind
    {
        /// <summary>Not a supported control; generation reports a diagnostic.</summary>
        Unknown = 0,

        /// <summary><c>UnityEngine.UI.Button</c>; emits a command binding.</summary>
        Button = 1,

        /// <summary><c>UnityEngine.UI.Text</c>; one-way display binding.</summary>
        Text = 2,

        /// <summary><c>TMPro.TMP_Text</c> (including <c>TextMeshProUGUI</c>); one-way display binding.</summary>
        TmpText = 3,

        /// <summary><c>UnityEngine.UI.InputField</c>; two-way string binding.</summary>
        InputField = 4,

        /// <summary><c>TMPro.TMP_InputField</c>; two-way string binding.</summary>
        TmpInputField = 5,

        /// <summary><c>UnityEngine.UI.Toggle</c>; two-way boolean binding.</summary>
        Toggle = 6,

        /// <summary><c>UnityEngine.UI.Slider</c>; two-way float binding.</summary>
        Slider = 7,
    }

    /// <summary>Binding update direction used by generated <c>UIBindData</c> attributes.</summary>
    public enum UIBindMode
    {
        /// <summary>View state drives the control (default).</summary>
        OneWay = 0,

        /// <summary>View state drives the control and control input writes View state.</summary>
        TwoWay = 1,
    }

    /// <summary>
    /// Metadata about <see cref="UIComponentKind"/> values: control type mapping, default binding
    /// mode, View state type and Unity component type-name recognition used by the Editor glue.
    /// </summary>
    public static class UIComponentKindInfo
    {
        /// <summary>True when the kind is one of the supported control shapes.</summary>
        public static bool IsSupported(UIComponentKind kind)
            => kind != UIComponentKind.Unknown && Enum.IsDefined(typeof(UIComponentKind), kind);

        /// <summary>True when the kind generates a command binding instead of an observable state binding.</summary>
        public static bool IsCommand(UIComponentKind kind) => kind == UIComponentKind.Button;

        /// <summary>
        /// Default binding direction: display controls are one-way, input controls are two-way.
        /// Returns <see cref="UIBindMode.OneWay"/> for command/unknown kinds.
        /// </summary>
        public static UIBindMode DefaultMode(UIComponentKind kind)
        {
            switch (kind)
            {
                case UIComponentKind.InputField:
                case UIComponentKind.TmpInputField:
                case UIComponentKind.Toggle:
                case UIComponentKind.Slider:
                    return UIBindMode.TwoWay;
                default:
                    return UIBindMode.OneWay;
            }
        }

        /// <summary>Fully qualified Unity control type emitted into generated window fields.</summary>
        public static string? UnityControlTypeName(UIComponentKind kind)
        {
            switch (kind)
            {
                case UIComponentKind.Button: return "global::UnityEngine.UI.Button";
                case UIComponentKind.Text: return "global::UnityEngine.UI.Text";
                case UIComponentKind.TmpText: return "global::TMPro.TMP_Text";
                case UIComponentKind.InputField: return "global::UnityEngine.UI.InputField";
                case UIComponentKind.TmpInputField: return "global::TMPro.TMP_InputField";
                case UIComponentKind.Toggle: return "global::UnityEngine.UI.Toggle";
                case UIComponentKind.Slider: return "global::UnityEngine.UI.Slider";
                default: return null;
            }
        }

        /// <summary>C# type of the generated View observable property for a data binding.</summary>
        public static string? ViewStateTypeName(UIComponentKind kind)
        {
            switch (kind)
            {
                case UIComponentKind.Text:
                case UIComponentKind.TmpText:
                case UIComponentKind.InputField:
                case UIComponentKind.TmpInputField:
                    return "string";
                case UIComponentKind.Toggle:
                    return "bool";
                case UIComponentKind.Slider:
                    return "float";
                default:
                    return null;
            }
        }

        /// <summary>
        /// Maps a Unity component full type name (as reported by <c>GetType().FullName</c> in the
        /// Editor) to a supported control kind. Reflection based: the Editor glue uses this so it
        /// does not need compile-time references to UGUI or TextMeshPro assemblies.
        /// </summary>
        public static UIComponentKind FromUnityTypeName(string? unityTypeName)
        {
            if (string.IsNullOrWhiteSpace(unityTypeName))
                return UIComponentKind.Unknown;

            switch (unityTypeName!.Trim())
            {
                case "UnityEngine.UI.Button": return UIComponentKind.Button;
                case "UnityEngine.UI.Text": return UIComponentKind.Text;
                case "UnityEngine.UI.InputField": return UIComponentKind.InputField;
                case "UnityEngine.UI.Toggle": return UIComponentKind.Toggle;
                case "UnityEngine.UI.Slider": return UIComponentKind.Slider;
                case "TMPro.TMP_Text": return UIComponentKind.TmpText;
                case "TMPro.TextMeshProUGUI": return UIComponentKind.TmpText;
                case "TMPro.TMP_InputField": return UIComponentKind.TmpInputField;
                default: return UIComponentKind.Unknown;
            }
        }
    }

    /// <summary>
    /// One row of the YIUI-like component reference table recorded from a Prefab: which component
    /// was selected, which serialized field should reference it, and which business View member it
    /// binds to. This is an Editor/edit-time model; the runtime never reflects over it.
    /// </summary>
    public sealed class BindingTableRow
    {
        /// <summary>Component/GameObject name shown in the editor binding table (required).</summary>
        public string ComponentName { get; set; } = string.Empty;

        /// <summary>Supported control kind (required; <see cref="UIComponentKind.Unknown"/> is a diagnostic).</summary>
        public UIComponentKind Kind { get; set; }

        /// <summary>Serialized window field name generated for this component (required for generation).</summary>
        public string FieldName { get; set; } = string.Empty;

        /// <summary>
        /// Business View target: an observable property name for display/input controls, or the
        /// command name (<c>Submit</c>, <c>SubmitAsync</c> or <c>SubmitCommand</c>) for buttons.
        /// </summary>
        public string ViewMemberName { get; set; } = string.Empty;

        /// <summary>
        /// Explicit binding mode; <see langword="null"/> uses the control kind default (one-way for
        /// displays, two-way for inputs).
        /// </summary>
        public UIBindMode? Mode { get; set; }

        /// <summary>Optional <c>string.Format</c> pattern emitted as <c>UIBindData.Format</c>.</summary>
        public string? Format { get; set; }

        /// <summary>Project-relative path of the Prefab the component was scanned from.</summary>
        public string? PrefabPath { get; set; }

        /// <summary>
        /// Editor hint that this row renames/removes an existing generated binding. Such rows are
        /// only applied after the user confirms the previewed destructive change.
        /// </summary>
        public bool IsDestructive { get; set; }

        /// <summary>Effective binding mode (explicit value or the control kind default).</summary>
        public UIBindMode EffectiveMode => Mode ?? UIComponentKindInfo.DefaultMode(Kind);
    }
}
