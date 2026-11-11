#nullable enable
using System;
using System.Collections.Generic;

namespace ObFramework.Editor.Core
{
    /// <summary>
    /// Input for <see cref="EditorTemplateGenerator"/>: the selected Architecture/Level/names, the
    /// component binding table scanned from a Prefab and the current contents of the editable
    /// files being regenerated. All paths are project-relative (for example
    /// <c>Assets/ObFramework/Generated/UI/ShopWindow.cs</c>); no Unity types are involved.
    /// </summary>
    public sealed class EditorGenerationRequest
    {
        /// <summary>Business View class name, for example <c>ShopView</c> (required).</summary>
        public string ViewName { get; set; } = string.Empty;

        /// <summary>Unity window controller class name; defaults to <c>&lt;View minus "View"&gt;Window</c>.</summary>
        public string WindowName { get; set; } = string.Empty;

        /// <summary>Namespace of the generated View/Window/contract classes; empty means the global namespace.</summary>
        public string Namespace { get; set; } = string.Empty;

        /// <summary>View contract interface name; defaults to <c>I&lt;View minus "View"&gt;Contract</c>.</summary>
        public string ContractName { get; set; } = string.Empty;

        /// <summary>
        /// When true (default) the contract interface is generated as an editable partial interface
        /// file. Disable when the contract is authored elsewhere.
        /// </summary>
        public bool GenerateContractInterface { get; set; } = true;

        /// <summary>
        /// Fully qualified architecture type selected during generation, for example
        /// <c>Game.GameArchitecture</c>. The window returns <c>Game.GameArchitecture.Interface</c>.
        /// </summary>
        public string ArchitectureTypeFullName { get; set; } = string.Empty;

        /// <summary><c>EUILevel</c> selection: <c>Scene</c>, <c>Common</c> (default) or <c>Overlay</c>.</summary>
        public string Level { get; set; } = "Common";

        /// <summary>Default output directory used when an explicit file path is not supplied.</summary>
        public string OutputDirectory { get; set; } = string.Empty;

        /// <summary>Explicit project-relative path for the generated View file.</summary>
        public string ViewOutputPath { get; set; } = string.Empty;

        /// <summary>Explicit project-relative path for the generated window (VC) file.</summary>
        public string WindowOutputPath { get; set; } = string.Empty;

        /// <summary>Explicit project-relative path for the generated contract interface file.</summary>
        public string ContractOutputPath { get; set; } = string.Empty;

        /// <summary>Current View source read from disk, or <see langword="null"/> for first generation.</summary>
        public string? ExistingViewSource { get; set; }

        /// <summary>Current window source read from disk, or <see langword="null"/> for first generation.</summary>
        public string? ExistingWindowSource { get; set; }

        /// <summary>Current contract source read from disk, or <see langword="null"/> for first generation.</summary>
        public string? ExistingContractSource { get; set; }

        /// <summary>Project-relative Prefab path the rows were scanned from (for diagnostics).</summary>
        public string? PrefabPath { get; set; }

        /// <summary>When true (default) the View template receives empty <c>OnInit</c>/<c>OnClose</c> hooks.</summary>
        public bool EmitLifecycleHooks { get; set; } = true;

        /// <summary>Component reference binding table; rows are processed in order.</summary>
        public IList<BindingTableRow> Rows { get; set; } = new List<BindingTableRow>();

        /// <summary>
        /// Destructive change ids confirmed by the user in the preview dialog. Only confirmed
        /// renames/deletes of generated serialized fields are applied; everything else is reported
        /// as a pending <see cref="DestructiveChange"/>.
        /// </summary>
        public IList<string> ConfirmedDestructiveChangeIds { get; set; } = new List<string>();

        /// <summary>
        /// Optional project-specific Unity control type names per kind (for example a
        /// <c>TextMeshProUGUI</c> subclass). Values must be fully qualified or <c>global::</c> qualified.
        /// </summary>
        public IDictionary<UIComponentKind, string>? ControlTypeNameOverrides { get; set; }
    }
}
