using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ObFramework.Test.Cli
{
    /// <summary>
    /// Temporary directory helper for Config CLI / deployment tests. Only ordinary file system
    /// operations are used; no Unity, no network and no environment variables.
    /// </summary>
    internal sealed class TempWorkspace : IDisposable
    {
        public TempWorkspace(string prefix = "obf-cli-test-")
        {
            Root = Path.Combine(Path.GetTempPath(), prefix + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        /// <summary>Absolute workspace root directory.</summary>
        public string Root { get; }

        /// <summary>Resolves a workspace-relative path (<c>/</c> or <c>\</c> separators) to an absolute path.</summary>
        public string FullPath(string relativePath)
            => Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));

        public string EnsureDirectory(string relativePath)
        {
            var fullPath = FullPath(relativePath);
            Directory.CreateDirectory(fullPath);
            return fullPath;
        }

        public string WriteText(string relativePath, string content)
        {
            var fullPath = FullPath(relativePath);
            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllText(fullPath, content);
            return fullPath;
        }

        public bool Exists(string relativePath) => File.Exists(FullPath(relativePath));

        public bool DirectoryExists(string relativePath) => Directory.Exists(FullPath(relativePath));

        public string ReadText(string relativePath) => File.ReadAllText(FullPath(relativePath));

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Root))
                    Directory.Delete(Root, recursive: true);
            }
            catch (IOException)
            {
                // Best effort cleanup only; a failed temp delete must not fail a test run.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
