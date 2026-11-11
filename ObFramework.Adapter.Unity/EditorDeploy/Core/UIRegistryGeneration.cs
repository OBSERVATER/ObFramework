#nullable enable
using System;
using System.Collections.Generic;

namespace ObFramework.Editor.Core
{
    /// <summary>One View-to-VC mapping supplied to the static UI registry generator.</summary>
    public sealed class UIRegistryEntry
    {
        /// <summary>Fully qualified business View type, for example <c>Game.UI.ShopView</c>.</summary>
        public string ViewTypeFullName { get; set; } = string.Empty;

        /// <summary>Fully qualified Unity window controller type, for example <c>Game.UI.ShopWindow</c>.</summary>
        public string WindowTypeFullName { get; set; } = string.Empty;

        /// <summary>Explicit resource key override; when null the setup.json rule/overrides are used.</summary>
        public string? ResourceKeyOverride { get; set; }
    }

    /// <summary>
    /// Input for <see cref="UIRegistrySourceGenerator"/>: the validated setup.json snapshot, the
    /// explicit View/VC pairs found by the Editor and where the replaceable registry source goes.
    /// The generated <c>RegisterAll</c> only calls the Unity adapter's explicit
    /// <c>Register&lt;TView, TWindow&gt;(resourceKey)</c> API; the runtime never reads setup.json.
    /// </summary>
    public sealed class UIRegistryGenerationInput
    {
        /// <summary>Validated setup.json unity module snapshot (required).</summary>
        public UISetupSnapshot? Setup { get; set; }

        /// <summary>Generated View/VC pairs to register (order is preserved).</summary>
        public IList<UIRegistryEntry> Entries { get; set; } = new List<UIRegistryEntry>();

        /// <summary>Namespace of the generated static registry class; empty means the global namespace.</summary>
        public string Namespace { get; set; } = "Game.Generated.UI";

        /// <summary>Generated static class name.</summary>
        public string ClassName { get; set; } = "GeneratedUIRegistry";

        /// <summary>Generated static method name.</summary>
        public string MethodName { get; set; } = "RegisterAll";

        /// <summary>
        /// Fully qualified Unity adapter type used as the <c>RegisterAll</c> parameter, for example
        /// <c>ObFramework.Adapter.Unity.UI.UnityUIAdapter</c>. Emitted as a <c>global::</c> name.
        /// </summary>
        public string AdapterTypeName { get; set; } = "ObFramework.Adapter.Unity.UI.UnityUIAdapter";

        /// <summary>
        /// Project-relative registry file path. Empty uses
        /// <c>&lt;setup.scriptOutputRoot&gt;/GeneratedUIRegistry.g.cs</c>.
        /// </summary>
        public string OutputPath { get; set; } = string.Empty;

        /// <summary>Current registry source (the file is replaceable; used only to report no-op regeneration).</summary>
        public string? ExistingSource { get; set; }
    }

    /// <summary>One View/VC/resource-key registration after validation and resource key resolution.</summary>
    public sealed class UIRegistryResolvedEntry
    {
        public UIRegistryResolvedEntry(
            string viewTypeFullName,
            string windowTypeFullName,
            string viewTypeShortName,
            string resourceKey)
        {
            ViewTypeFullName = viewTypeFullName ?? throw new ArgumentNullException(nameof(viewTypeFullName));
            WindowTypeFullName = windowTypeFullName ?? throw new ArgumentNullException(nameof(windowTypeFullName));
            ViewTypeShortName = viewTypeShortName ?? throw new ArgumentNullException(nameof(viewTypeShortName));
            ResourceKey = resourceKey ?? throw new ArgumentNullException(nameof(resourceKey));
        }

        /// <summary>Fully qualified View type.</summary>
        public string ViewTypeFullName { get; }

        /// <summary>Fully qualified window controller type.</summary>
        public string WindowTypeFullName { get; }

        /// <summary>View type name used for resource key rule/override lookup.</summary>
        public string ViewTypeShortName { get; }

        /// <summary>Resolved engine asset key (override, else <c>resourceKeyFormat</c> with <c>{view}</c> replaced).</summary>
        public string ResourceKey { get; }
    }

    /// <summary>Result of <see cref="UIRegistrySourceGenerator.Generate"/>.</summary>
    public sealed class UIRegistryGenerationOutput
    {
        public UIRegistryGenerationOutput(
            GeneratedFile? file,
            IReadOnlyList<EditorDiagnostic> diagnostics,
            IReadOnlyList<UIRegistryResolvedEntry> registrations)
        {
            File = file;
            Diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
            Registrations = registrations ?? throw new ArgumentNullException(nameof(registrations));
        }

        /// <summary>Generated registry source, or <see langword="null"/> when an error prevented emission.</summary>
        public GeneratedFile? File { get; }

        /// <summary>Structured diagnostics.</summary>
        public IReadOnlyList<EditorDiagnostic> Diagnostics { get; }

        /// <summary>Resolved registrations actually emitted.</summary>
        public IReadOnlyList<UIRegistryResolvedEntry> Registrations { get; }

        /// <summary>True when no error diagnostic was reported.</summary>
        public bool Success
        {
            get
            {
                foreach (var diagnostic in Diagnostics)
                {
                    if (diagnostic.IsError)
                        return false;
                }

                return true;
            }
        }
    }
}
