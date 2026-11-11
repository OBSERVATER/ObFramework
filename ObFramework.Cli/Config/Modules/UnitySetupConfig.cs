using System.Collections.Generic;

namespace ObFramework.Cli.Config.Modules
{
    /// <summary>
    /// POCO configuration contributed by the Unity module for one Unity project's setup.json.
    /// <para>
    /// This type is registered explicitly in
    /// <see cref="ConfigCliModule.CreateDefaultRegistry"/>; it is not discovered by scanning.
    /// Module paths are validated against the setup.json <c>root</c> when written and never escape
    /// it. Runtime UI loading does not read setup.json: the Unity Editor tooling uses these values
    /// at edit/generate time, while the game loads the adapter's statically registered keys.
    /// </para>
    /// </summary>
    public sealed class UnitySetupConfig
    {
        /// <summary>
        /// Directory (module-relative) where the Unity Editor writes generated View/VC and the
        /// static UI registration source.
        /// </summary>
        [SetupPath(Description = "Generated View/VC and UI registry source directory")]
        public string ScriptOutputRoot { get; set; } = "Assets/ObFramework/Generated/UI";

        /// <summary>Directory (module-relative) containing the UI Prefabs referenced by generated registrations.</summary>
        [SetupPath(Description = "UI Prefab directory")]
        public string UiAssetRoot { get; set; } = "Assets/ObFramework/UI";

        /// <summary>
        /// Asset key rule used by the Unity Editor when generating View-to-resource registrations.
        /// <c>{view}</c> is replaced with the business View type name; this is an engine resource
        /// key rule, never a file system path.
        /// </summary>
        public string ResourceKeyFormat { get; set; } = "UI/{view}";

        /// <summary>Optional per-view resource key overrides (View type name to engine resource key).</summary>
        public Dictionary<string, string>? ResourceKeyOverrides { get; set; }
    }
}
