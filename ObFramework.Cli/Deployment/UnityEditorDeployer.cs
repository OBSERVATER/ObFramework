using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ObFramework.Cli.Deployment
{
    /// <summary>
    /// Files-only Unity Editor deployment: installs, updates and removes the Editor tooling resources
    /// shipped with the NuGet package into a Unity project. It never launches Unity and never
    /// touches user content outside its owned-file manifest.
    /// <para>
    /// Guarantees:
    /// <list type="bullet">
    /// <item>Install/update writes only files inside <c>Assets/</c> that are either absent or owned
    /// by this deployer; existing unowned files are skipped with a warning.</item>
    /// <item>Repeated installs are idempotent (byte-identical files and owned manifest; unchanged
    /// files are not rewritten).</item>
    /// <item>Owned files that are no longer part of the (possibly changed) resource set are removed
    /// on the next install so upgrades reflect the current package.</item>
    /// <item>Remove deletes only owned files whose SHA-256 still matches the owned manifest;
    /// modified files, user scripts, Prefabs and setup.json are preserved.</item>
    /// <item>An absent editor payload produces a clear <see cref="DeploymentException"/> instead of a
    /// silent no-op.</item>
    /// </list>
    /// </para>
    /// </summary>
    public sealed class UnityEditorDeployer
    {
        private readonly IDeployableFileSource _source;
        private readonly DeploymentManifest _manifest;

        public UnityEditorDeployer(IDeployableFileSource source, DeploymentManifest manifest)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
            _manifest.Validate();
        }

        public DeploymentManifest Manifest => _manifest;

        /// <summary>Installs or updates the plan into a Unity project directory.</summary>
        public DeploymentResult Deploy(string unityProjectDirectory, DeploymentOptions? options = null)
        {
            var dryRun = options?.DryRun ?? false;
            var projectRoot = ResolveProjectRoot(unityProjectDirectory);

            var ownedRelativePath = DeploymentPaths.NormalizeRelative(_manifest.ManifestPath, "owned deployment manifest");
            EnsureDeployableTarget(ownedRelativePath, "owned deployment manifest");
            var ownedFullPath = DeploymentPaths.CombineWithinRoot(projectRoot, ownedRelativePath, "owned deployment manifest");

            var plan = _manifest.ResolveFiles(_source);
            if (plan.Count == 0)
            {
                throw new DeploymentException(
                    $"Unity Editor deployment payload is not present in {_source.Description}. " +
                    "Build ObFramework.Cli after ObFramework.Adapter.Unity/EditorDeploy contains the Editor tooling " +
                    "(or pass --source <DIR> with a local payload).");
            }

            // Read every resource up front: lets us fail with one clear diagnostic when the payload is
            // missing, and keeps the file system writes in a second, well-defined phase.
            var planned = new List<PlannedFile>(plan.Count);
            foreach (var entry in plan)
            {
                var sourcePath = DeploymentPaths.NormalizeRelative(entry.Source, "deployable source");
                var available = _source.TryRead(sourcePath, out var content);
                planned.Add(new PlannedFile(entry, sourcePath, available && content != null ? content : null));
            }

            if (planned.All(static file => file.Content == null))
            {
                throw new DeploymentException(
                    $"Unity Editor deployment payload is not present in {_source.Description}: " +
                    "none of the plan source files could be read. Rebuild ObFramework.Cli so the " +
                    "ObFramework.Adapter.Unity/EditorDeploy resources are embedded.");
            }

            var existingOwned = OwnedDeploymentManifest.TryLoad(ownedFullPath);
            var ownedFiles = existingOwned == null
                ? new Dictionary<string, OwnedDeploymentFile>(StringComparer.OrdinalIgnoreCase)
                : existingOwned.Files.ToDictionary(static file => file.Path, StringComparer.OrdinalIgnoreCase);

            var changes = new List<DeploymentFileChange>();
            var warnings = new List<string>();
            var deployedFiles = new List<OwnedDeploymentFile>();
            var deployedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var plannedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var file in planned)
            {
                var entry = file.Entry;
                var targetRelative = DeploymentPaths.NormalizeRelative(entry.Target, "deployment target");
                EnsureDeployableTarget(targetRelative, "deployment target");

                if (string.Equals(targetRelative, ownedRelativePath, StringComparison.OrdinalIgnoreCase))
                {
                    throw new DeploymentException(
                        $"Deployment target '{targetRelative}' collides with the owned deployment manifest.");
                }

                if (!plannedPaths.Add(targetRelative))
                    throw new DeploymentException($"Duplicate deployment target '{targetRelative}'.");

                var targetFullPath = DeploymentPaths.CombineWithinRoot(projectRoot, targetRelative, "deployment target");

                if (file.Content == null)
                {
                    var detail = $"source resource '{file.SourcePath}' is missing from {_source.Description}; skipped.";
                    changes.Add(new DeploymentFileChange(targetRelative, DeploymentChangeKind.Skipped, detail));
                    warnings.Add(detail);
                    continue;
                }

                var exists = File.Exists(targetFullPath);
                var isOwned = ownedFiles.ContainsKey(targetRelative);

                if (exists && !isOwned)
                {
                    var detail = $"'{targetRelative}' already exists and is not owned by ob; left untouched.";
                    changes.Add(new DeploymentFileChange(targetRelative, DeploymentChangeKind.Skipped, detail));
                    warnings.Add(detail);
                    continue;
                }

                if (exists)
                {
                    var existingContent = File.ReadAllBytes(targetFullPath);
                    if (existingContent.AsSpan().SequenceEqual(file.Content))
                    {
                        changes.Add(new DeploymentFileChange(targetRelative, DeploymentChangeKind.Unchanged));
                    }
                    else
                    {
                        if (!dryRun)
                            WriteFile(targetFullPath, file.Content);

                        changes.Add(new DeploymentFileChange(targetRelative, DeploymentChangeKind.Updated));
                    }
                }
                else
                {
                    if (!dryRun)
                        WriteFile(targetFullPath, file.Content);

                    changes.Add(new DeploymentFileChange(targetRelative, DeploymentChangeKind.Installed));
                }

                deployedPaths.Add(targetRelative);
                deployedFiles.Add(new OwnedDeploymentFile(
                    targetRelative,
                    DeploymentHash.ComputeSha256(file.Content),
                    string.IsNullOrWhiteSpace(entry.Kind) ? "file" : entry.Kind));
            }

            // Stale owned files: previously installed by ob but no longer part of this package's plan.
            foreach (var stale in ownedFiles.Values
                         .Where(file => !deployedPaths.Contains(file.Path))
                         .OrderBy(static file => file.Path, StringComparer.OrdinalIgnoreCase))
            {
                if (!IsSafeOwnedPath(stale.Path))
                {
                    warnings.Add($"'{stale.Path}' is not a deployable Assets path; kept and still owned.");
                    deployedFiles.Add(stale);
                    continue;
                }

                var staleFullPath = DeploymentPaths.CombineWithinRoot(projectRoot, stale.Path, "owned file");
                if (!File.Exists(staleFullPath))
                {
                    changes.Add(new DeploymentFileChange(stale.Path, DeploymentChangeKind.Missing, "already absent"));
                    continue;
                }

                // Only delete content this deployer actually wrote: a user-modified owned file is left
                // in place (and keeps its ownership record) exactly like Remove does.
                var staleHash = DeploymentHash.ComputeSha256(File.ReadAllBytes(staleFullPath));
                if (!string.Equals(staleHash, stale.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    var detail = "modified after deployment; left in place (not owned content anymore)";
                    warnings.Add($"'{stale.Path}' was {detail}; it is no longer part of the deployment plan.");
                    changes.Add(new DeploymentFileChange(stale.Path, DeploymentChangeKind.Skipped, detail));
                    deployedFiles.Add(stale);
                    continue;
                }

                if (!dryRun)
                {
                    File.Delete(staleFullPath);
                    DeleteEmptyDirectories(projectRoot, Path.GetDirectoryName(staleFullPath));
                }

                changes.Add(new DeploymentFileChange(stale.Path, DeploymentChangeKind.Removed));
            }

            if (!dryRun)
                WriteOwnedManifest(projectRoot, ownedRelativePath, ownedFullPath, deployedFiles);

            return new DeploymentResult(changes, warnings, dryRun);
        }

        /// <summary>Removes all files owned by this deployer from a Unity project directory.</summary>
        public DeploymentResult Remove(string unityProjectDirectory, DeploymentOptions? options = null)
        {
            var dryRun = options?.DryRun ?? false;
            var projectRoot = ResolveProjectRoot(unityProjectDirectory);

            var ownedRelativePath = DeploymentPaths.NormalizeRelative(_manifest.ManifestPath, "owned deployment manifest");
            var ownedFullPath = DeploymentPaths.CombineWithinRoot(projectRoot, ownedRelativePath, "owned deployment manifest");

            var changes = new List<DeploymentFileChange>();
            var warnings = new List<string>();

            var owned = OwnedDeploymentManifest.TryLoad(ownedFullPath);
            if (owned == null)
            {
                warnings.Add(
                    $"No owned deployment manifest found at '{ownedRelativePath}'; nothing to remove.");
                return new DeploymentResult(changes, warnings, dryRun);
            }

            var remaining = new List<OwnedDeploymentFile>();

            foreach (var ownedFile in owned.Files.OrderBy(static file => file.Path, StringComparer.OrdinalIgnoreCase))
            {
                if (!IsSafeOwnedPath(ownedFile.Path))
                {
                    warnings.Add($"'{ownedFile.Path}' is not a deployable Assets path; left in place.");
                    remaining.Add(ownedFile);
                    continue;
                }

                var fullPath = DeploymentPaths.CombineWithinRoot(projectRoot, ownedFile.Path, "owned file");
                if (!File.Exists(fullPath))
                {
                    changes.Add(new DeploymentFileChange(ownedFile.Path, DeploymentChangeKind.Missing, "already absent"));
                    continue;
                }

                var actualHash = DeploymentHash.ComputeSha256(File.ReadAllBytes(fullPath));
                if (!string.Equals(actualHash, ownedFile.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    var detail = "modified after deployment; left in place (not owned content anymore)";
                    warnings.Add($"'{ownedFile.Path}' was {detail}.");
                    changes.Add(new DeploymentFileChange(ownedFile.Path, DeploymentChangeKind.Skipped, detail));
                    remaining.Add(ownedFile);
                    continue;
                }

                if (!dryRun)
                {
                    File.Delete(fullPath);
                    DeleteEmptyDirectories(projectRoot, Path.GetDirectoryName(fullPath));
                }

                changes.Add(new DeploymentFileChange(ownedFile.Path, DeploymentChangeKind.Removed));
            }

            if (!dryRun)
            {
                if (remaining.Count == 0)
                {
                    if (File.Exists(ownedFullPath))
                    {
                        File.Delete(ownedFullPath);
                        DeleteEmptyDirectories(projectRoot, Path.GetDirectoryName(ownedFullPath));
                    }
                }
                else
                {
                    // Keep ownership records for files left in place so a later remove can retry.
                    WriteFile(
                        ownedFullPath,
                        DeploymentText.Encode(
                            OwnedDeploymentManifest.ForFiles(remaining).ToJson() + Environment.NewLine));
                }
            }

            return new DeploymentResult(changes, warnings, dryRun);
        }

        private static string ResolveProjectRoot(string? projectDirectory)
        {
            if (string.IsNullOrWhiteSpace(projectDirectory))
                throw new DeploymentException("Unity project directory must not be empty.");

            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(projectDirectory);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
            {
                throw new DeploymentException($"Unity project directory is invalid: '{projectDirectory}'. {ex.Message}", ex);
            }

            if (!Directory.Exists(fullPath))
                throw new DeploymentException($"Unity project directory not found: {fullPath}");

            fullPath = Path.TrimEndingDirectorySeparator(fullPath);
            if (!Directory.Exists(Path.Combine(fullPath, "Assets")))
                throw new DeploymentException(
                    $"'{fullPath}' does not look like a Unity project: the Assets folder was not found.");

            return fullPath;
        }

        private static void EnsureDeployableTarget(string projectRelativePath, string description)
        {
            if (!DeploymentPaths.IsUnderAssets(projectRelativePath))
                throw new DeploymentException(
                    $"{description} '{projectRelativePath}' must be inside the Unity project's Assets folder.");
            if (DeploymentPaths.IsProtectedProjectPath(projectRelativePath))
                throw new DeploymentException($"{description} '{projectRelativePath}' is protected.");
        }

        private static bool IsSafeOwnedPath(string projectRelativePath)
        {
            try
            {
                var normalized = DeploymentPaths.NormalizeRelative(projectRelativePath, "owned file");
                return DeploymentPaths.IsUnderAssets(normalized) &&
                       !DeploymentPaths.IsProtectedProjectPath(normalized);
            }
            catch (DeploymentException)
            {
                return false;
            }
        }

        private static void WriteOwnedManifest(
            string projectRoot,
            string ownedRelativePath,
            string ownedFullPath,
            IReadOnlyList<OwnedDeploymentFile> files)
        {
            if (files.Count == 0)
            {
                if (File.Exists(ownedFullPath))
                {
                    File.Delete(ownedFullPath);
                    DeleteEmptyDirectories(projectRoot, Path.GetDirectoryName(ownedFullPath));
                }

                return;
            }

            var json = OwnedDeploymentManifest.ForFiles(files).ToJson() + Environment.NewLine;

            if (File.Exists(ownedFullPath))
            {
                string existing;
                try
                {
                    existing = File.ReadAllText(ownedFullPath);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    existing = string.Empty;
                }

                if (string.Equals(existing, json, StringComparison.Ordinal))
                    return; // byte-identical owned manifest: keep the file untouched (idempotent install)
            }

            WriteFile(ownedFullPath, DeploymentText.Encode(json));
        }

        private static void WriteFile(string fullPath, byte[] content)
        {
            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            var tempPath = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllBytes(tempPath, content);
                File.Move(tempPath, fullPath, overwrite: true);
            }
            finally
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
        }

        private static void DeleteEmptyDirectories(string projectRoot, string? startDirectory)
        {
            var assetsRoot = Path.Combine(projectRoot, "Assets");
            var directory = startDirectory;

            while (!string.IsNullOrEmpty(directory))
            {
                var fullPath = Path.TrimEndingDirectorySeparator(directory!);

                if (DeploymentPaths.PathsEqual(fullPath, projectRoot) ||
                    DeploymentPaths.PathsEqual(fullPath, assetsRoot))
                    break;

                if (!Directory.Exists(fullPath))
                {
                    directory = Path.GetDirectoryName(fullPath);
                    continue;
                }

                if (Directory.EnumerateFileSystemEntries(fullPath).Any())
                    break;

                try
                {
                    Directory.Delete(fullPath);
                }
                catch (IOException)
                {
                    break; // another process is using the directory; keep it
                }

                directory = Path.GetDirectoryName(fullPath);
            }
        }

        private sealed class PlannedFile
        {
            public PlannedFile(DeployableFile entry, string sourcePath, byte[]? content)
            {
                Entry = entry;
                SourcePath = sourcePath;
                Content = content;
            }

            public DeployableFile Entry { get; }

            public string SourcePath { get; }

            /// <summary>Deployed content, or <see langword="null"/> when the resource is absent.</summary>
            public byte[]? Content { get; }
        }
    }
}
