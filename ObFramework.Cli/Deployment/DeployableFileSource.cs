using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace ObFramework.Cli.Deployment
{
    /// <summary>
    /// Abstraction over the packaged deployable resource set. Production uses the CLI assembly's
    /// embedded resources; tests and local development can point at a directory instead.
    /// </summary>
    public interface IDeployableFileSource
    {
        /// <summary>Human readable source description used in diagnostics.</summary>
        string Description { get; }

        /// <summary>All available file paths in forward-slash relative form.</summary>
        IReadOnlyList<string> ListFiles();

        /// <summary>Reads one relative file; returns <see langword="false"/> when it does not exist.</summary>
        bool TryRead(string relativePath, out byte[] content);
    }

    /// <summary>
    /// Deployable Unity Editor resources embedded into the CLI assembly.
    /// <para>
    /// Packaging choice: the payload is authored once under
    /// <c>ObFramework.Adapter.Unity/EditorDeploy/**</c> and embedded by ObFramework.Cli.csproj as
    /// <c>ObFramework.Cli.UnityEditorDeploy/**</c> resources (plus contentFiles in the packed
    /// CLI package). A NuGet dotnet tool package only guarantees the tool assembly and its
    /// dependencies are restored, so embedding is what makes <c>ob unity deploy</c> work from an
    /// installed package with no build-machine copy of the adapter folder.
    /// </para>
    /// </summary>
    public sealed class EmbeddedDeployableFileSource : IDeployableFileSource
    {
        /// <summary>Resource name prefix produced by the csproj <c>LogicalName</c> mapping.</summary>
        public const string DefaultResourcePrefix = "ObFramework.Cli.UnityEditorDeploy";

        private readonly Assembly _assembly;
        private readonly string _resourcePrefix;
        private readonly Lazy<IReadOnlyDictionary<string, string>> _resourceNames;

        public EmbeddedDeployableFileSource(Assembly? assembly = null, string resourcePrefix = DefaultResourcePrefix)
        {
            _assembly = assembly ?? typeof(EmbeddedDeployableFileSource).Assembly;
            _resourcePrefix = NormalizePrefix(resourcePrefix);
            _resourceNames = new Lazy<IReadOnlyDictionary<string, string>>(LoadResourceNames);
        }

        /// <summary>Source backed by the CLI assembly that contains this type.</summary>
        public static EmbeddedDeployableFileSource CreateDefault() => new();

        public string Description => $"{_assembly.GetName().Name}:{_resourcePrefix}/";

        public IReadOnlyList<string> ListFiles()
            => _resourceNames.Value.Keys.OrderBy(static path => path, StringComparer.Ordinal).ToArray();

        public bool TryRead(string relativePath, out byte[] content)
        {
            content = Array.Empty<byte>();
            if (string.IsNullOrWhiteSpace(relativePath))
                return false;

            var normalized = DeploymentPaths.NormalizeRelative(relativePath, "embedded resource");
            if (!_resourceNames.Value.TryGetValue(normalized, out var resourceName))
                return false;

            using var stream = _assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
                return false;

            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            content = buffer.ToArray();
            return true;
        }

        private IReadOnlyDictionary<string, string> LoadResourceNames()
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            var prefixWithSeparator = _resourcePrefix + "/";

            foreach (var resourceName in _assembly.GetManifestResourceNames())
            {
                var normalizedName = resourceName.Replace('\\', '/');
                if (!normalizedName.StartsWith(prefixWithSeparator, StringComparison.Ordinal))
                    continue;

                var relativePath = normalizedName.Substring(prefixWithSeparator.Length);
                if (relativePath.Length == 0)
                    continue;

                map[relativePath] = resourceName;
            }

            return map;
        }

        private static string NormalizePrefix(string resourcePrefix)
        {
            if (string.IsNullOrWhiteSpace(resourcePrefix))
                throw new ArgumentException("Resource prefix must not be empty.", nameof(resourcePrefix));

            return resourcePrefix.Trim().Trim('/').Replace('\\', '/');
        }
    }

    /// <summary>
    /// Deployable files stored in a local directory tree. Used by tests and by local development
    /// when the Editor tool payload lives in the repository instead of being embedded.
    /// </summary>
    public sealed class DirectoryDeployableFileSource : IDeployableFileSource
    {
        public DirectoryDeployableFileSource(string rootDirectory)
        {
            if (string.IsNullOrWhiteSpace(rootDirectory))
                throw new ArgumentException("Source directory must not be empty.", nameof(rootDirectory));

            RootDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootDirectory));
            if (!Directory.Exists(RootDirectory))
                throw new DeploymentException($"Deployable source directory not found: {RootDirectory}");
        }

        public string RootDirectory { get; }

        public string Description => RootDirectory;

        public IReadOnlyList<string> ListFiles()
            => Directory
                .EnumerateFiles(RootDirectory, "*", SearchOption.AllDirectories)
                .Select(file => Path.GetRelativePath(RootDirectory, file).Replace('\\', '/'))
                .OrderBy(static file => file, StringComparer.Ordinal)
                .ToArray();

        public bool TryRead(string relativePath, out byte[] content)
        {
            content = Array.Empty<byte>();
            if (string.IsNullOrWhiteSpace(relativePath))
                return false;

            string fullPath;
            try
            {
                fullPath = DeploymentPaths.CombineWithinRoot(RootDirectory, relativePath, "deployable source");
            }
            catch (DeploymentException)
            {
                return false;
            }

            if (!File.Exists(fullPath))
                return false;

            content = File.ReadAllBytes(fullPath);
            return true;
        }
    }
}
