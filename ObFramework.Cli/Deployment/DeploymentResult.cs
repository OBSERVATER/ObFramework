using System;
using System.Collections.Generic;
using System.Linq;

namespace ObFramework.Cli.Deployment
{
    /// <summary>Kind of change applied to one deployed file.</summary>
    public enum DeploymentChangeKind
    {
        /// <summary>File was created.</summary>
        Installed,

        /// <summary>Owned file content was replaced.</summary>
        Updated,

        /// <summary>Owned file already had the expected content; nothing was written.</summary>
        Unchanged,

        /// <summary>Owned file was deleted because it is no longer part of the deployment plan.</summary>
        Removed,

        /// <summary>File was deliberately left untouched (not owned, modified by the user or unavailable).</summary>
        Skipped,

        /// <summary>The file was already absent.</summary>
        Missing,
    }

    /// <summary>One file change produced by a deploy/remove operation.</summary>
    public sealed class DeploymentFileChange
    {
        public DeploymentFileChange(string path, DeploymentChangeKind change, string? detail = null)
        {
            Path = path;
            Change = change;
            Detail = detail;
        }

        /// <summary>Unity project-relative path.</summary>
        public string Path { get; }

        /// <summary>Applied change kind.</summary>
        public DeploymentChangeKind Change { get; }

        /// <summary>Optional human readable context.</summary>
        public string? Detail { get; }
    }

    /// <summary>Options for one deploy/remove operation.</summary>
    public sealed class DeploymentOptions
    {
        /// <summary>Computes changes without writing or deleting anything.</summary>
        public bool DryRun { get; init; }
    }

    /// <summary>Result of one deploy/remove operation.</summary>
    public sealed class DeploymentResult
    {
        public DeploymentResult(
            IReadOnlyList<DeploymentFileChange> changes,
            IReadOnlyList<string> warnings,
            bool dryRun)
        {
            Changes = changes ?? throw new ArgumentNullException(nameof(changes));
            Warnings = warnings ?? throw new ArgumentNullException(nameof(warnings));
            DryRun = dryRun;
        }

        /// <summary>Per-file changes in processing order.</summary>
        public IReadOnlyList<DeploymentFileChange> Changes { get; }

        /// <summary>Non-fatal diagnostics (missing resources, unowned collisions, modified files).</summary>
        public IReadOnlyList<string> Warnings { get; }

        /// <summary>Whether this was a dry run (no file system changes were made).</summary>
        public bool DryRun { get; }

        public int InstalledCount => Count(DeploymentChangeKind.Installed);

        public int UpdatedCount => Count(DeploymentChangeKind.Updated);

        public int UnchangedCount => Count(DeploymentChangeKind.Unchanged);

        public int RemovedCount => Count(DeploymentChangeKind.Removed);

        public int SkippedCount => Count(DeploymentChangeKind.Skipped);

        public int MissingCount => Count(DeploymentChangeKind.Missing);

        /// <summary>Whether any content was (or would be, for a dry run) changed.</summary>
        public bool HasChanges => InstalledCount + UpdatedCount + RemovedCount > 0;

        public int Count(DeploymentChangeKind kind) => Changes.Count(change => change.Change == kind);

        /// <summary>One-line summary used by the CLI.</summary>
        public string Summary
        {
            get
            {
                var parts = new List<string>
                {
                    $"installed {InstalledCount}",
                    $"updated {UpdatedCount}",
                    $"unchanged {UnchangedCount}",
                    $"removed {RemovedCount}",
                };

                if (SkippedCount > 0)
                    parts.Add($"skipped {SkippedCount}");
                if (MissingCount > 0)
                    parts.Add($"missing {MissingCount}");

                return string.Join(", ", parts);
            }
        }
    }
}
