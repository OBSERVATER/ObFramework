using System;
using System.IO;
using ObFramework.Cli.Commands.Config;

namespace ObFramework.Cli.Deployment
{
    /// <summary>
    /// Path rules for deploying package resources into a Unity project.
    /// <para>
    /// Deployment targets are project-relative paths and must stay inside the Unity project's
    /// <c>Assets</c> folder. <c>..</c> and <c>.</c> segments are rejected outright so a deployment
    /// plan can never escape the project or accidentally overwrite user content outside Assets.
    /// </para>
    /// </summary>
    internal static class DeploymentPaths
    {
        private static bool IsWindowsFileSystem => Path.DirectorySeparatorChar == '\\';

        /// <summary>Normalizes a relative path to forward-slash form and rejects rooted/escaping forms.</summary>
        public static string NormalizeRelative(string? relativePath, string description)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
                throw new DeploymentException($"{description} path must not be empty.");

            var trimmed = relativePath.Trim();
            if (Path.IsPathFullyQualified(trimmed) ||
                trimmed.StartsWith('/') ||
                trimmed.StartsWith('\\') ||
                (trimmed.Length >= 2 && char.IsLetter(trimmed[0]) && trimmed[1] == ':'))
            {
                throw new DeploymentException($"{description} path must be relative: '{relativePath}'.");
            }

            var segments = trimmed.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0)
                throw new DeploymentException($"{description} path must not be empty: '{relativePath}'.");

            var invalidChars = Path.GetInvalidFileNameChars();
            foreach (var segment in segments)
            {
                if (segment == "." || segment == "..")
                    throw new DeploymentException(
                        $"{description} path must not contain '.' or '..' segments: '{relativePath}'.");
                if (segment.IndexOfAny(invalidChars) >= 0)
                    throw new DeploymentException(
                        $"{description} path contains invalid characters: '{relativePath}'.");
            }

            return string.Join("/", segments);
        }

        /// <summary>Combines a project-relative path with a root and verifies containment.</summary>
        public static string CombineWithinRoot(string rootDirectory, string relativePath, string description)
        {
            var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootDirectory));
            var normalized = NormalizeRelative(relativePath, description);
            var combined = Path.GetFullPath(
                Path.Combine(root, normalized.Replace('/', Path.DirectorySeparatorChar)));

            if (!SetupConfigPaths.IsPathWithinRoot(root, combined))
                throw new DeploymentException($"{description} path '{relativePath}' escapes '{root}'.");

            return combined;
        }

        /// <summary>Unity requires the deployed layout to live below the project's Assets folder.</summary>
        public static bool IsUnderAssets(string projectRelativePath)
            => projectRelativePath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) &&
               projectRelativePath.Length > "Assets/".Length;

        /// <summary>User-owned project files that must never be written or deleted by the deployer.</summary>
        public static bool IsProtectedProjectPath(string projectRelativePath)
        {
            var fileName = Path.GetFileName(projectRelativePath.Replace('/', Path.DirectorySeparatorChar));
            return string.Equals(fileName, SetupJsonDocument.DefaultFileName, StringComparison.OrdinalIgnoreCase);
        }

        public static bool PathsEqual(string left, string right)
        {
            var comparison = IsWindowsFileSystem ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            return string.Equals(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
                comparison);
        }
    }
}
