#nullable enable
namespace ObFramework.Editor.Core
{
    /// <summary>
    /// Runtime type and attribute names emitted by the Unity Editor tooling. The Editor never
    /// references these types at compile time: it emits fully qualified source text that the Unity
    /// project compiles together with the runtime assemblies and the Roslyn view-binding generator.
    /// </summary>
    public static class EditorFrameworkNames
    {
        /// <summary>Engine-independent business View base class.</summary>
        public const string AUIView = "ObFramework.Integrated.UI.AUIView";

        /// <summary>Unity window controller (VC) base class.</summary>
        public const string AUIWindow = "ObFramework.Adapter.Unity.UI.AUIWindow";

        /// <summary>Architecture interface returned by the generated window.</summary>
        public const string IArchitecture = "ObFramework.Core.IArchitecture";

        /// <summary>Unity UI level enum.</summary>
        public const string EUILevel = "ObFramework.Integrated.UI.EUILevel";

        /// <summary>Engine-independent UI data interface.</summary>
        public const string IUIData = "ObFramework.Integrated.UI.IUIData";

        /// <summary>Observable View field attribute consumed by the engine-neutral generator.</summary>
        public const string ObservablePropertyAttribute = "ObFramework.Integrated.UI.ObservableProperty";

        /// <summary>Relay command View method attribute consumed by the engine-neutral generator.</summary>
        public const string RelayCommandAttribute = "ObFramework.Integrated.UI.RelayCommand";

        /// <summary>Window-to-View/contract attribute consumed by the Unity binding generator.</summary>
        public const string BindViewAttribute = "ObFramework.Adapter.Unity.ViewBinding.BindView";

        /// <summary>Serialized window field attribute for data bindings.</summary>
        public const string UIBindDataAttribute = "ObFramework.Adapter.Unity.ViewBinding.UIBindData";

        /// <summary>Serialized window field attribute for command bindings.</summary>
        public const string UIBindCommandAttribute = "ObFramework.Adapter.Unity.ViewBinding.UIBindCommand";

        /// <summary>Binding direction enum used by data bindings.</summary>
        public const string UIBindMode = "ObFramework.Adapter.Unity.ViewBinding.UIBindMode";

        /// <summary>Unity serialized field attribute.</summary>
        public const string UnitySerializeField = "UnityEngine.SerializeField";

        /// <summary>Prefixes a full type name with <c>global::</c> so emitted source never depends on usings.</summary>
        public static string Global(string fullName) => "global::" + fullName;
    }
}
