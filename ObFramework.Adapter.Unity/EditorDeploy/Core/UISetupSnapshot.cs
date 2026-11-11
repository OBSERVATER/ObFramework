#nullable enable
using System;
using System.Collections.Generic;

namespace ObFramework.Editor.Core
{
    /// <summary>
    /// Raw <c>modules.unity</c> section of setup.json as read by the Unity Editor glue (fixed
    /// string fields via Unity <c>JsonUtility</c> plus the raw section JSON for the override map).
    /// Mirrors the T13 <c>UnitySetupConfig</c> POCO; null/empty values fall back to the same
    /// defaults the Config CLI uses.
    /// </summary>
    public sealed class UISetupSection
    {
        /// <summary>Absolute development root (<c>root</c>), for example <c>D:/GameProject</c>.</summary>
        public string? Root { get; set; }

        /// <summary>Module-relative directory for generated View/VC/registry sources.</summary>
        public string? ScriptOutputRoot { get; set; }

        /// <summary>Module-relative directory containing the UI Prefabs.</summary>
        public string? UIAssetRoot { get; set; }

        /// <summary>Engine resource key rule; <c>{view}</c> is replaced by the View type name.</summary>
        public string? ResourceKeyFormat { get; set; }

        /// <summary>Optional View-name to resource-key overrides.</summary>
        public IDictionary<string, string>? ResourceKeyOverrides { get; set; }
    }

    /// <summary>
    /// Validated, normalized view of the <c>unity</c> module of setup.json.
    /// <para>
    /// The development root is an absolute file system path used only at edit/generate time.
    /// Module paths are normalized to forward-slash module-relative form and constrained below the
    /// root. Resource keys (format + overrides) are engine asset addresses and are never derived
    /// from the root file system path: the runtime does not read setup.json at all.
    /// </para>
    /// </summary>
    public sealed class UISetupSnapshot
    {
        /// <summary>Default <c>scriptOutputRoot</c> (mirrors the Config CLI module default).</summary>
        public const string DefaultScriptOutputRoot = "Assets/ObFramework/Generated/UI";

        /// <summary>Default <c>uiAssetRoot</c> (mirrors the Config CLI module default).</summary>
        public const string DefaultUIAssetRoot = "Assets/ObFramework/UI";

        /// <summary>Default <c>resourceKeyFormat</c> (mirrors the Config CLI module default).</summary>
        public const string DefaultResourceKeyFormat = "UI/{view}";

        private readonly Dictionary<string, string> _resourceKeyOverrides;

        internal UISetupSnapshot(
            string root,
            string scriptOutputRoot,
            string uiAssetRoot,
            string resourceKeyFormat,
            Dictionary<string, string> resourceKeyOverrides)
        {
            Root = root;
            ScriptOutputRoot = scriptOutputRoot;
            UIAssetRoot = uiAssetRoot;
            ResourceKeyFormat = resourceKeyFormat;
            _resourceKeyOverrides = resourceKeyOverrides;
        }

        /// <summary>Absolute, forward-slash, trimmed development root.</summary>
        public string Root { get; }

        /// <summary>Normalized module-relative generated-source directory.</summary>
        public string ScriptOutputRoot { get; }

        /// <summary>Normalized module-relative UI Prefab directory.</summary>
        public string UIAssetRoot { get; }

        /// <summary>Resource key rule containing <c>{view}</c> (unless misconfigured; see diagnostics).</summary>
        public string ResourceKeyFormat { get; }

        /// <summary>Per-View resource key overrides (ordinal lookup by View type name).</summary>
        public IReadOnlyDictionary<string, string> ResourceKeyOverrides => _resourceKeyOverrides;

        /// <summary>Absolute generated-source directory (edit-time only; never a runtime asset address).</summary>
        public string ScriptOutputDirectory => Combine(Root, ScriptOutputRoot);

        /// <summary>Absolute UI Prefab directory (edit-time only).</summary>
        public string UIAssetDirectory => Combine(Root, UIAssetRoot);

        /// <summary>
        /// Resolves the engine resource key for a View: an explicit override wins, otherwise the
        /// configured format is applied with <c>{view}</c> replaced by the View type name.
        /// </summary>
        public string ResolveResourceKey(string viewTypeName)
        {
            if (viewTypeName == null)
                throw new ArgumentNullException(nameof(viewTypeName));

            var name = viewTypeName.Trim();
            if (name.Length == 0)
                throw new ArgumentException("A View type name is required.", nameof(viewTypeName));

            if (_resourceKeyOverrides.TryGetValue(name, out var overridden) && !string.IsNullOrEmpty(overridden))
                return overridden;

            return ResourceKeyFormat.Replace("{view}", name);
        }

        /// <summary>Combines the development root with one normalized module-relative path.</summary>
        public string ResolvePath(string moduleRelativePath)
        {
            if (!UISetupPaths.TryNormalizeModuleRelative(moduleRelativePath, "path", out var normalized, out var error))
                throw new ArgumentException(error, nameof(moduleRelativePath));
            return Combine(Root, normalized);
        }

        private static string Combine(string root, string relative)
        {
            if (root.Length > 0 && root[root.Length - 1] == '/')
                return root + relative;
            return root + "/" + relative;
        }

        public override string ToString() => Root + " (scriptOutputRoot=" + ScriptOutputRoot + ", uiAssetRoot=" + UIAssetRoot + ")";
    }

    /// <summary>Result of <see cref="UISetupSnapshotFactory.Create"/>: a snapshot plus diagnostics.</summary>
    public sealed class UISetupSnapshotResult
    {
        public UISetupSnapshotResult(UISetupSnapshot? snapshot, IReadOnlyList<EditorDiagnostic> diagnostics)
        {
            Snapshot = snapshot;
            Diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        }

        /// <summary>The validated snapshot, or <see langword="null"/> when a fatal setup error was reported.</summary>
        public UISetupSnapshot? Snapshot { get; }

        /// <summary>Setup validation diagnostics.</summary>
        public IReadOnlyList<EditorDiagnostic> Diagnostics { get; }

        /// <summary>True when a snapshot was produced.</summary>
        public bool Success => Snapshot != null;
    }

    /// <summary>Pure path normalization rules shared by setup validation, generation and the Editor glue.</summary>
    public static class UISetupPaths
    {
        /// <summary>True for rooted paths on Windows or Unix (drive, UNC, leading slash).</summary>
        public static bool IsAbsolutePath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            var value = path!.Trim();
            if (value[0] == '/' || value[0] == '\\')
                return true;
            if (value.Length >= 3 && char.IsLetter(value[0]) && value[1] == ':' &&
                (value[2] == '/' || value[2] == '\\'))
                return true;
            if (value.Length >= 2 && char.IsLetter(value[0]) && value[1] == ':')
                return true;
            return false;
        }

        /// <summary>Normalizes an absolute development root to forward slashes without a trailing separator.</summary>
        public static bool TryNormalizeRoot(string? root, out string normalized, out string? error)
        {
            normalized = string.Empty;
            error = null;
            if (string.IsNullOrWhiteSpace(root))
            {
                error = "setup.json 'root' is required and must be an absolute development path.";
                return false;
            }

            var value = root!.Trim();
            if (!IsAbsolutePath(value))
            {
                error = "setup.json 'root' must be an absolute path: '" + value + "'.";
                return false;
            }

            var unified = value.Replace('\\', '/');
            while (unified.Length > 1 && unified[unified.Length - 1] == '/')
                unified = unified.Substring(0, unified.Length - 1);
            if (unified.Length > 2 && unified[unified.Length - 1] == ':' && char.IsLetter(unified[0]))
                unified += "/";

            normalized = unified;
            return true;
        }

        /// <summary>
        /// Normalizes one module-relative path (forward slashes, no <c>.</c>/<c>..</c> segments).
        /// Absolute paths and escaping segments are rejected so a setup path can never leave the root.
        /// </summary>
        public static bool TryNormalizeModuleRelative(
            string? path,
            string description,
            out string normalized,
            out string? error)
        {
            normalized = string.Empty;
            error = null;
            if (string.IsNullOrWhiteSpace(path))
            {
                error = "The " + description + " path must not be empty.";
                return false;
            }

            var value = path!.Trim();
            if (IsAbsolutePath(value))
            {
                error = "The " + description + " path must be module-relative, not absolute: '" + value + "'.";
                return false;
            }

            var segments = value.Replace('\\', '/').Split('/');
            var kept = new List<string>(segments.Length);
            foreach (var segment in segments)
            {
                if (segment.Length == 0 || segment == ".")
                    continue;
                if (segment == "..")
                {
                    error = "The " + description + " path must not contain '..' segments: '" + value + "'.";
                    return false;
                }
                if (segment.IndexOf(':') >= 0)
                {
                    error = "The " + description + " path contains an invalid segment: '" + value + "'.";
                    return false;
                }

                kept.Add(segment);
            }

            if (kept.Count == 0)
            {
                error = "The " + description + " path must not be empty: '" + value + "'.";
                return false;
            }

            normalized = string.Join("/", kept);
            return true;
        }

        /// <summary>
        /// True when a resource key looks like a file system path (absolute, UNC, backslash form, or
        /// below the development root). Such values must never become runtime asset addresses.
        /// </summary>
        public static bool LooksLikeFileSystemPath(string? resourceKey, string? developmentRoot)
        {
            if (string.IsNullOrWhiteSpace(resourceKey))
                return false;

            var key = resourceKey!.Trim();
            if (key.IndexOf('\\') >= 0)
                return true;
            if (IsAbsolutePath(key))
                return true;

            if (!string.IsNullOrEmpty(developmentRoot))
            {
                var root = developmentRoot!.TrimEnd('/');
                if (key.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(key, root, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        /// <summary>Normalizes a project-relative output file path used for generated sources.</summary>
        public static bool TryNormalizeProjectFile(
            string? path,
            string description,
            out string normalized,
            out string? error)
        {
            normalized = string.Empty;
            error = null;
            if (string.IsNullOrWhiteSpace(path))
            {
                error = "The " + description + " file path must not be empty.";
                return false;
            }

            if (!TryNormalizeModuleRelative(path, description, out var relative, out error))
                return false;

            if (!relative.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            {
                error = "The " + description + " file path must end with '.cs': '" + path + "'.";
                return false;
            }

            normalized = relative;
            return true;
        }
    }
}
