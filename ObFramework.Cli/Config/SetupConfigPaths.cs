using System;
using System.Collections.Generic;
using System.IO;

namespace ObFramework.Cli.Config
{
    /// <summary>
    /// Path rules shared by the setup.json document and the Config CLI.
    /// <para>
    /// setup.json contains one absolute development <c>root</c>; every module path is relative to
    /// that root. Normalization produces stable forward-slash relative paths and rejects values
    /// that escape the root (for example <c>../outside</c> or an absolute path outside the root).
    /// </para>
    /// </summary>
    public static class SetupConfigPaths
    {
        private static bool IsWindowsFileSystem => Path.DirectorySeparatorChar == '\\';

        /// <summary>
        /// Validates that <paramref name="root"/> is a fully qualified absolute path and returns a
        /// normalized form with a trailing directory separator removed (a drive root keeps its
        /// separator). Throws <see cref="SetupConfigException"/> for missing, empty or relative
        /// roots. Existence of the directory is intentionally not required here: Root is a
        /// development path declaration and may be created later.
        /// </summary>
        public static string ValidateAbsoluteRoot(string? root)
        {
            if (string.IsNullOrWhiteSpace(root))
                throw new SetupConfigException(
                    "Root is not configured. Set an absolute root in setup.json or pass --root <DIR>.");

            var candidate = root.Trim();
            if (!Path.IsPathFullyQualified(candidate))
                throw new SetupConfigException($"Root must be an absolute path: '{root}'.");

            try
            {
                return Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidate));
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
            {
                throw new SetupConfigException($"Root is not a valid path: '{root}'. {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Returns <see langword="true"/> when <paramref name="fullPath"/> is the root itself or a
        /// descendant of it. Comparison follows the host file system case rules.
        /// </summary>
        public static bool IsPathWithinRoot(string absoluteRoot, string fullPath)
        {
            if (string.IsNullOrWhiteSpace(absoluteRoot))
                throw new ArgumentException("Root must not be empty.", nameof(absoluteRoot));
            if (string.IsNullOrWhiteSpace(fullPath))
                throw new ArgumentException("Path must not be empty.", nameof(fullPath));

            var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(absoluteRoot));
            var candidate = Path.GetFullPath(fullPath);
            var comparison = IsWindowsFileSystem ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

            if (string.Equals(candidate, root, comparison))
                return true;

            var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar) ||
                                    root.EndsWith(Path.AltDirectorySeparatorChar)
                ? root
                : root + Path.DirectorySeparatorChar;

            return candidate.StartsWith(rootWithSeparator, comparison);
        }

        /// <summary>
        /// Normalizes a module path relative to <paramref name="absoluteRoot"/>.
        /// <list type="bullet">
        /// <item>Relative values are resolved against the root.</item>
        /// <item>Absolute values are accepted only when they point inside the root.</item>
        /// <item>The returned value uses forward slashes and never contains <c>.</c>/<c>..</c> segments.</item>
        /// <item>The root itself normalizes to <c>.</c>.</item>
        /// </list>
        /// Throws <see cref="SetupConfigException"/> when the value escapes the root or is empty.
        /// </summary>
        public static string NormalizeModulePath(string absoluteRoot, string modulePath)
        {
            if (string.IsNullOrWhiteSpace(modulePath))
                throw new SetupConfigException("Module path must not be empty.");

            var root = ValidateAbsoluteRoot(absoluteRoot);

            string fullPath;
            try
            {
                fullPath = Path.IsPathFullyQualified(modulePath)
                    ? Path.GetFullPath(modulePath)
                    : Path.GetFullPath(Path.Combine(root, modulePath));
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
            {
                throw new SetupConfigException($"Module path is not a valid path: '{modulePath}'. {ex.Message}", ex);
            }

            if (!IsPathWithinRoot(root, fullPath))
                throw new SetupConfigException($"Module path '{modulePath}' escapes Root '{root}'.");

            var relative = Path.GetRelativePath(root, fullPath);
            relative = relative.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (relative.Length == 0)
                return ".";

            return relative.Replace('\\', '/');
        }

        /// <summary>
        /// Combines the root with an already normalized module path and returns the absolute path.
        /// </summary>
        public static string GetFullModulePath(string absoluteRoot, string modulePath)
        {
            var root = ValidateAbsoluteRoot(absoluteRoot);
            var normalized = NormalizeModulePath(root, modulePath);
            return normalized == "."
                ? root
                : Path.GetFullPath(Path.Combine(root, normalized.Replace('/', Path.DirectorySeparatorChar)));
        }
    }
}
