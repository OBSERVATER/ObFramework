namespace ObFramework.Cli.Commands.Config
{
    /// <summary>
    /// Locates the <c>setup.json</c> used by the Config CLI.
    /// <para>
    /// Either an explicit file (the CLI <c>--file</c> option) or the nearest file found by walking
    /// from the working directory upward. The explicit option always wins. The legacy
    /// <c>obf.json</c> name is intentionally never read: the UI 2.0 spec standardizes on
    /// <c>setup.json</c>, so when both files exist setup.json is always preferred.
    /// </para>
    /// </summary>
    public static class SetupJsonDiscovery
    {
        /// <summary>
        /// Walks from <paramref name="startDirectory"/> (default: current directory) up to the file
        /// system root and returns the first existing setup.json, or <see langword="null"/>.
        /// </summary>
        public static string? FindSetupFile(string? startDirectory = null)
        {
            var start = ResolveStartDirectory(startDirectory);

            var directory = new DirectoryInfo(start);
            while (directory != null)
            {
                var candidate = Path.Combine(directory.FullName, SetupJsonDocument.DefaultFileName);
                if (File.Exists(candidate))
                    return Path.GetFullPath(candidate);

                directory = directory.Parent;
            }

            return null;
        }

        /// <summary>
        /// Resolves the configuration file to use.
        /// <list type="bullet">
        /// <item>An explicit relative file is resolved against <paramref name="startDirectory"/>.</item>
        /// <item>Without an explicit file the nearest setup.json from the start directory upward is returned.</item>
        /// <item>Returns <see langword="null"/> when nothing is found and no explicit file was given.</item>
        /// </list>
        /// Throws <see cref="SetupConfigException"/> when an explicit file is required but missing.
        /// </summary>
        public static string? ResolveSetupFile(
            string? explicitFile,
            string? startDirectory = null,
            bool requireExplicitFileExists = true)
        {
            if (!string.IsNullOrWhiteSpace(explicitFile))
            {
                var start = ResolveStartDirectory(startDirectory);
                var fullPath = ResolvePath(explicitFile, start);

                if (requireExplicitFileExists && !File.Exists(fullPath))
                    throw new SetupConfigException($"Configuration file not found: {fullPath}");

                return fullPath;
            }

            return FindSetupFile(startDirectory);
        }

        /// <summary>Resolves a possibly relative path against a base directory (default: current directory).</summary>
        public static string ResolvePath(string path, string? baseDirectory = null)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new SetupConfigException("Path must not be empty.");

            var baseDirectoryPath = ResolveStartDirectory(baseDirectory);
            var combined = Path.IsPathFullyQualified(path) ? path : Path.Combine(baseDirectoryPath, path);

            try
            {
                return Path.GetFullPath(combined);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
            {
                throw new SetupConfigException($"Path is invalid: '{path}'. {ex.Message}", ex);
            }
        }

        private static string ResolveStartDirectory(string? startDirectory)
        {
            var value = string.IsNullOrWhiteSpace(startDirectory)
                ? Directory.GetCurrentDirectory()
                : startDirectory;

            try
            {
                return Path.GetFullPath(value);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
            {
                throw new SetupConfigException($"Directory is invalid: '{value}'. {ex.Message}", ex);
            }
        }
    }
}
