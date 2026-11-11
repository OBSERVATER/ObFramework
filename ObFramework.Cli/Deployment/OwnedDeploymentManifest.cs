using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ObFramework.Cli.Deployment
{
    /// <summary>
    /// The owned-file manifest written into the Unity project
    /// (<c>Assets/ObFramework/obf-deploy.json</c> by default).
    /// <para>
    /// It records exactly which files the deployer installed and their content hash. Deploy/update
    /// only ever replaces files in this list; remove only deletes files from this list whose hash
    /// still matches, so user scripts, Prefabs and setup.json can never be removed by accident.
    /// The manifest is deterministic (sorted, no timestamps), so repeating an install produces
    /// byte-identical output.
    /// </para>
    /// </summary>
    public sealed class OwnedDeploymentManifest
    {
        public const int CurrentSchemaVersion = 1;

        private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

        /// <summary>Manifest schema version.</summary>
        public int SchemaVersion { get; init; } = CurrentSchemaVersion;

        /// <summary>Producing tool name.</summary>
        public string Tool { get; init; } = "ob";

        /// <summary>Deployment target identifier.</summary>
        public string Deployment { get; init; } = "unity-editor";

        /// <summary>Owned files, sorted by path.</summary>
        public IReadOnlyList<OwnedDeploymentFile> Files { get; init; } = Array.Empty<OwnedDeploymentFile>();

        /// <summary>Creates a deterministic owned manifest from the supplied files.</summary>
        public static OwnedDeploymentManifest ForFiles(IEnumerable<OwnedDeploymentFile> files)
        {
            if (files == null)
                throw new ArgumentNullException(nameof(files));

            return new OwnedDeploymentManifest
            {
                Files = files
                    .OrderBy(static file => file.Path, StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
            };
        }

        /// <summary>Loads an existing owned manifest; returns <see langword="null"/> when absent.</summary>
        public static OwnedDeploymentManifest? TryLoad(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                return null;

            string text;
            try
            {
                text = File.ReadAllText(filePath);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                throw new DeploymentException(
                    $"Failed to read owned deployment manifest '{filePath}': {ex.Message}", ex);
            }

            OwnedDeploymentManifest manifest;
            try
            {
                var parsed = JsonSerializer.Deserialize<OwnedDeploymentManifest>(text, JsonOptions);
                manifest = parsed
                    ?? throw new DeploymentException($"Owned deployment manifest '{filePath}' must be a JSON object.");
            }
            catch (JsonException ex)
            {
                throw new DeploymentException(
                    $"Owned deployment manifest '{filePath}' is not valid JSON: {ex.Message}", ex);
            }

            manifest.Validate(filePath);
            return manifest;
        }

        /// <summary>Serializes the manifest deterministically (indented, camelCase, sorted files).</summary>
        public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

        private void Validate(string filePath)
        {
            if (SchemaVersion != CurrentSchemaVersion)
                throw new DeploymentException(
                    $"Owned deployment manifest '{filePath}' uses schema version {SchemaVersion}; " +
                    $"expected {CurrentSchemaVersion}. Run a matching 'ob' version.");

            if (Files == null)
                throw new DeploymentException($"Owned deployment manifest '{filePath}' has no 'files' array.");

            foreach (var file in Files)
            {
                if (file == null || string.IsNullOrWhiteSpace(file.Path))
                    throw new DeploymentException(
                        $"Owned deployment manifest '{filePath}' contains an entry without a path.");

                // Rejects rooted or escaping paths; a tampered manifest must never reach the file system.
                DeploymentPaths.NormalizeRelative(file.Path, "owned file");
            }
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

    /// <summary>One file owned by the deployer.</summary>
    public sealed class OwnedDeploymentFile
    {
        public OwnedDeploymentFile()
        {
        }

        public OwnedDeploymentFile(string path, string sha256, string? kind = null)
        {
            Path = path;
            Sha256 = sha256;
            Kind = string.IsNullOrWhiteSpace(kind) ? "file" : kind;
        }

        /// <summary>Unity project-relative path of the deployed file.</summary>
        public string Path { get; init; } = string.Empty;

        /// <summary>Lower-case SHA-256 of the deployed content; used to detect user modifications.</summary>
        public string Sha256 { get; init; } = string.Empty;

        /// <summary>Deployable resource kind.</summary>
        public string Kind { get; init; } = "file";
    }

    /// <summary>SHA-256 helpers for ownership tracking.</summary>
    internal static class DeploymentHash
    {
        public static string ComputeSha256(byte[] content)
        {
            if (content == null)
                throw new ArgumentNullException(nameof(content));

            return Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
        }

        public static string ComputeSha256(string text) => ComputeSha256(DeploymentText.Encode(text));
    }
}
