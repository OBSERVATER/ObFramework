using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ObFramework.Cli.Deployment
{
    /// <summary>
    /// The deployment plan shipped with the NuGet package: how Editor-only deployable resources are
    /// mapped into a Unity project.
    /// <para>
    /// The plan is data, never a hard coded file list. The default embedded plan
    /// (<c>Resources/UnityDeployPlan/deploy-manifest.json</c>) uses
    /// <see cref="AutoDeployResources"/> to deploy every resource found under the embedded payload
    /// prefix to <c>targetRoot/&lt;resource relative path&gt;</c>, so the Unity Editor payload can
    /// change without changing code or the plan. Explicit <see cref="Files"/> entries override or
    /// add individual mappings. A missing resource is reported as a skipped file instead of
    /// crashing; a completely absent payload produces a clear diagnostic from the deployer.
    /// </para>
    /// </summary>
    public sealed class DeploymentManifest
    {
        /// <summary>File name of a deployment plan when it is supplied by a local source directory.</summary>
        public const string DefaultSourceFileName = "deploy-manifest.json";

        /// <summary>Default owned-file manifest path inside the Unity project.</summary>
        public const string DefaultOwnedManifestPath = "Assets/ObFramework/obf-deploy.json";

        /// <summary>Default manifest resource name of the embedded plan produced by the csproj.</summary>
        public const string EmbeddedPlanResourceName = "ObFramework.Cli.UnityDeployPlan/deploy-manifest.json";

        /// <summary>Manifest schema version understood by this CLI.</summary>
        public const int CurrentSchemaVersion = 1;

        private static readonly JsonSerializerOptions ManifestJsonOptions = CreateJsonOptions();

        /// <summary>Plan schema version.</summary>
        public int SchemaVersion { get; init; } = CurrentSchemaVersion;

        /// <summary>Producing tool name (<c>ob</c>).</summary>
        public string Tool { get; init; } = "ob";

        /// <summary>Deployment target identifier (<c>unity-editor</c>).</summary>
        public string Deployment { get; init; } = "unity-editor";

        /// <summary>Project-relative path of the owned-file manifest written by this deployer.</summary>
        public string ManifestPath { get; init; } = DefaultOwnedManifestPath;

        /// <summary>
        /// Project-relative root used for automatically mapped resources. Only used when
        /// <see cref="AutoDeployResources"/> is enabled.
        /// </summary>
        public string TargetRoot { get; init; } = "Assets/ObFramework/Editor";

        /// <summary>
        /// When enabled, every resource under the deployable source is mapped to
        /// <c>TargetRoot/&lt;relative resource path&gt;</c> (minus explicit entries and excludes).
        /// This is what makes deployment tolerate a changing Editor resource set.
        /// </summary>
        public bool AutoDeployResources { get; init; }

        /// <summary>Optional glob patterns of resource paths that must not be deployed.</summary>
        public IReadOnlyList<string> Exclude { get; init; } = Array.Empty<string>();

        /// <summary>Explicit deployable files (in plan order); auto mapping is applied after these.</summary>
        public IReadOnlyList<DeployableFile> Files { get; init; } = Array.Empty<DeployableFile>();

        /// <summary>Creates an explicit plan for the supplied files using the default manifest settings.</summary>
        public static DeploymentManifest ForFiles(IEnumerable<DeployableFile> files)
        {
            if (files == null)
                throw new ArgumentNullException(nameof(files));

            return new DeploymentManifest { Files = new List<DeployableFile>(files) };
        }

        /// <summary>Parses and validates a deployment plan JSON document.</summary>
        public static DeploymentManifest Load(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new DeploymentException("Deployment manifest is empty.");

            DeploymentManifest manifest;
            try
            {
                var parsed = JsonSerializer.Deserialize<DeploymentManifest>(json, ManifestJsonOptions);
                manifest = parsed ?? throw new DeploymentException("Deployment manifest must be a JSON object.");
            }
            catch (JsonException ex)
            {
                throw new DeploymentException($"Deployment manifest is not valid JSON: {ex.Message}", ex);
            }

            manifest.Validate();
            return manifest;
        }

        /// <summary>Reads <c>deploy-manifest.json</c> from a deployable resource source.</summary>
        public static DeploymentManifest LoadFromSource(IDeployableFileSource source)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));

            if (!source.TryRead(DefaultSourceFileName, out var content))
                throw new DeploymentException(
                    $"Deployment manifest '{DefaultSourceFileName}' was not found in {source.Description}.");

            return Load(DeploymentText.Decode(content));
        }

        /// <summary>Loads the plan embedded in the CLI assembly (the default <c>ob init</c> plan).</summary>
        public static DeploymentManifest LoadEmbeddedPlan(Assembly? assembly = null)
        {
            assembly ??= typeof(DeploymentManifest).Assembly;

            using var stream = assembly.GetManifestResourceStream(EmbeddedPlanResourceName);
            if (stream == null)
                throw new DeploymentException(
                    $"Embedded deployment plan '{EmbeddedPlanResourceName}' is missing from " +
                    $"'{assembly.GetName().Name}'. The CLI package is incomplete; rebuild ObFramework.Cli.");

            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            return Load(DeploymentText.Decode(buffer.ToArray()));
        }

        /// <summary>
        /// Materializes the plan against a deployable resource source: explicit entries first, then
        /// automatically mapped resources (when enabled). Deterministic order.
        /// </summary>
        public IReadOnlyList<DeployableFile> ResolveFiles(IDeployableFileSource source)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));

            var resolved = new List<DeployableFile>();
            var mappedSources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var file in Files)
            {
                var sourcePath = DeploymentPaths.NormalizeRelative(file.Source, "deployable source");
                mappedSources.Add(sourcePath);
                resolved.Add(file);
            }

            if (!AutoDeployResources)
                return resolved;

            var targetRoot = DeploymentPaths.NormalizeRelative(TargetRoot, "deployment target root");
            foreach (var sourcePath in source.ListFiles())
            {
                if (string.Equals(sourcePath, DefaultSourceFileName, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (mappedSources.Contains(sourcePath) || IsExcluded(sourcePath))
                    continue;

                var normalized = DeploymentPaths.NormalizeRelative(sourcePath, "deployable source");
                resolved.Add(new DeployableFile(
                    normalized,
                    targetRoot + "/" + normalized,
                    InferKind(normalized)));
                mappedSources.Add(normalized);
            }

            return resolved;
        }

        /// <summary>
        /// Structural validation: relative paths, Assets-only targets and duplicate target
        /// detection. Resource existence is deliberately not required here (the resource set may
        /// legitimately change between builds).
        /// </summary>
        public void Validate()
        {
            if (SchemaVersion != CurrentSchemaVersion)
                throw new DeploymentException(
                    $"Unsupported deployment manifest schema version {SchemaVersion}; expected {CurrentSchemaVersion}.");

            var ownedManifestPath = DeploymentPaths.NormalizeRelative(ManifestPath, "owned deployment manifest");
            if (!DeploymentPaths.IsUnderAssets(ownedManifestPath))
                throw new DeploymentException(
                    $"Owned deployment manifest path '{ownedManifestPath}' must be inside the Unity project's Assets folder.");
            if (DeploymentPaths.IsProtectedProjectPath(ownedManifestPath))
                throw new DeploymentException($"Owned deployment manifest path '{ownedManifestPath}' is protected.");

            if (Files == null)
                throw new DeploymentException("Deployment manifest 'files' must be an array.");

            if (AutoDeployResources)
            {
                var targetRoot = DeploymentPaths.NormalizeRelative(TargetRoot, "deployment target root");
                if (!DeploymentPaths.IsUnderAssets(targetRoot))
                    throw new DeploymentException(
                        $"Deployment target root '{targetRoot}' must be inside the Unity project's Assets folder.");
                if (DeploymentPaths.IsProtectedProjectPath(targetRoot + "/file"))
                    throw new DeploymentException($"Deployment target root '{targetRoot}' is protected.");
            }

            var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var sources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in Files)
            {
                if (file == null)
                    throw new DeploymentException("Deployment manifest contains a null file entry.");

                var source = DeploymentPaths.NormalizeRelative(file.Source, "deployable source");
                if (!sources.Add(source))
                    throw new DeploymentException($"Duplicate deployable source '{source}' in deployment manifest.");

                var target = DeploymentPaths.NormalizeRelative(file.Target, "deployment target");
                if (!DeploymentPaths.IsUnderAssets(target))
                    throw new DeploymentException(
                        $"Deployment target '{target}' must be inside the Unity project's Assets folder.");
                if (DeploymentPaths.IsProtectedProjectPath(target))
                    throw new DeploymentException($"Deployment target '{target}' is protected (setup.json is never deployed).");
                if (string.Equals(target, ownedManifestPath, StringComparison.OrdinalIgnoreCase))
                    throw new DeploymentException(
                        $"Deployment target '{target}' collides with the owned deployment manifest.");
                if (!targets.Add(target))
                    throw new DeploymentException($"Duplicate deployment target '{target}' in deployment manifest.");
            }
        }

        private bool IsExcluded(string sourcePath)
        {
            var patterns = Exclude ?? Array.Empty<string>();
            foreach (var pattern in patterns)
            {
                if (!string.IsNullOrWhiteSpace(pattern) && DeploymentGlobs.IsMatch(pattern.Trim(), sourcePath))
                    return true;
            }

            return false;
        }

        private static string InferKind(string sourcePath)
        {
            var extension = Path.GetExtension(sourcePath).ToLowerInvariant();
            return extension switch
            {
                ".cs" => "editor-source",
                ".asmdef" => "asmdef",
                ".asmref" => "asmref",
                ".meta" => "meta",
                ".dll" => "roslyn-analyzer",
                ".json" => "metadata",
                _ => "file",
            };
        }

        private static JsonSerializerOptions CreateJsonOptions()
        {
            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                PropertyNameCaseInsensitive = true,
                WriteIndented = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            };
            options.Converters.Add(new JsonStringEnumConverter());
            return options;
        }
    }

    /// <summary>One deployable resource and its Unity project destination.</summary>
    public sealed class DeployableFile
    {
        public DeployableFile()
        {
        }

        public DeployableFile(string source, string target, string? kind = null, string? description = null)
        {
            Source = source;
            Target = target;
            Kind = string.IsNullOrWhiteSpace(kind) ? "file" : kind;
            Description = description;
        }

        /// <summary>Resource-relative source path (forward slashes).</summary>
        public string Source { get; init; } = string.Empty;

        /// <summary>Unity project-relative target path (forward slashes, must start with <c>Assets/</c>).</summary>
        public string Target { get; init; } = string.Empty;

        /// <summary>Resource kind used for diagnostics: editor-source, asmdef, meta, roslyn-analyzer, ...</summary>
        public string Kind { get; init; } = "file";

        /// <summary>Optional human readable purpose.</summary>
        public string? Description { get; init; }
    }

    /// <summary>Simple glob matcher for plan excludes (<c>*</c>, <c>?</c>, <c>**</c>).</summary>
    internal static class DeploymentGlobs
    {
        public static bool IsMatch(string pattern, string path)
        {
            if (string.IsNullOrWhiteSpace(pattern))
                return false;

            var normalizedPattern = pattern.Replace('\\', '/').TrimStart('/');
            var normalizedPath = path.Replace('\\', '/');

            // A trailing slash means "everything below this folder".
            if (normalizedPattern.EndsWith('/'))
                normalizedPattern += "**";

            var regex = "^" + ToRegex(normalizedPattern) + "$";
            return System.Text.RegularExpressions.Regex.IsMatch(
                normalizedPath,
                regex,
                System.Text.RegularExpressions.RegexOptions.IgnoreCase |
                System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        }

        private static string ToRegex(string pattern)
        {
            var builder = new StringBuilder(pattern.Length * 2);
            for (var index = 0; index < pattern.Length; index++)
            {
                var character = pattern[index];
                if (character == '*')
                {
                    if (index + 1 < pattern.Length && pattern[index + 1] == '*')
                    {
                        if (index + 2 < pattern.Length && pattern[index + 2] == '/')
                        {
                            // "**/" also matches the current directory (zero segments).
                            builder.Append("(?:.*/)?");
                            index += 2;
                        }
                        else
                        {
                            builder.Append(".*");
                            index++;
                        }
                    }
                    else
                    {
                        builder.Append("[^/]*");
                    }

                    continue;
                }

                if (character == '?')
                {
                    builder.Append("[^/]");
                    continue;
                }

                builder.Append(System.Text.RegularExpressions.Regex.Escape(character.ToString()));
            }

            return builder.ToString();
        }
    }

    /// <summary>UTF-8 decoding shared by the deployment manifests (tolerates a BOM).</summary>
    internal static class DeploymentText
    {
        private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        public static string Decode(byte[] content)
        {
            if (content == null || content.Length == 0)
                return string.Empty;

            var offset = content.Length >= 3 && content[0] == 0xEF && content[1] == 0xBB && content[2] == 0xBF
                ? 3
                : 0;

            return Encoding.UTF8.GetString(content, offset, content.Length - offset);
        }

        public static byte[] Encode(string text) => Utf8NoBom.GetBytes(text);
    }
}
