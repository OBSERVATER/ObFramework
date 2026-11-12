#nullable enable
using System;

namespace ObFramework.Editor.Core
{
    /// <summary>
    /// Pure string rules for the persisted binding-table component paths. The Editor glue records a
    /// control by its hierarchy path inside the Prefab, but Unity's <c>Transform.Find</c> resolves
    /// paths relative to the Prefab root (without the root name), so the stored value has to be
    /// normalized before use. Keeping the rule dependency-free makes it testable without Unity.
    /// </summary>
    public static class BindingTablePath
    {
        /// <summary>
        /// Normalizes a stored component path to a path relative to the Prefab root.
        /// <para>
        /// Accepts both the legacy absolute form (<c>ShopRoot/Buy/Button</c>) and an already
        /// relative form (<c>Buy/Button</c>). Returns an empty string for the root itself.
        /// </para>
        /// </summary>
        public static string MakeRelative(string? storedPath, string? prefabRootName)
        {
            var path = NormalizeSeparators(storedPath);
            if (path.Length == 0)
                return string.Empty;

            var root = NormalizeSeparators(prefabRootName);
            if (root.Length == 0)
                return path;

            if (string.Equals(path, root, StringComparison.Ordinal))
                return string.Empty;

            if (path.StartsWith(root + "/", StringComparison.Ordinal))
                return path.Substring(root.Length + 1);

            return path;
        }

        /// <summary>
        /// Builds a hierarchy path of <paramref name="transform"/> below <paramref name="root"/>
        /// (the root itself yields an empty string). Unity-free: callers pass names in order.
        /// </summary>
        public static string CombinePath(System.Collections.Generic.IReadOnlyList<string> namesInRootOrder)
        {
            if (namesInRootOrder == null)
                throw new ArgumentNullException(nameof(namesInRootOrder));
            if (namesInRootOrder.Count == 0)
                return string.Empty;

            return string.Join("/", namesInRootOrder);
        }

        private static string NormalizeSeparators(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;

            return path!.Trim().Replace('\\', '/').Trim('/');
        }
    }
}
