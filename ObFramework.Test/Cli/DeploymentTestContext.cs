using System;
using System.IO;
using System.Security.Cryptography;
using ObFramework.Cli.Deployment;

namespace ObFramework.Test.Cli
{
    /// <summary>
    /// Arranges a temporary "Unity project" (a directory with an Assets folder) and a temporary
    /// deployable payload directory. Everything is pure .NET; Unity is never invoked.
    /// </summary>
    internal sealed class DeploymentTestContext : IDisposable
    {
        public DeploymentTestContext()
        {
            Workspace = new TempWorkspace("obf-deploy-test-");
            PayloadDirectory = Workspace.EnsureDirectory("payload");
            ProjectDirectory = Workspace.EnsureDirectory("UnityProject");
            Workspace.EnsureDirectory("UnityProject/Assets");
        }

        public TempWorkspace Workspace { get; }

        public string PayloadDirectory { get; }

        public string ProjectDirectory { get; }

        public string OwnedManifestProjectPath => "Assets/ObFramework/obf-deploy.json";

        public IDeployableFileSource Source() => new DirectoryDeployableFileSource(PayloadDirectory);

        public void AddPayload(string relativePath, string content)
            => Workspace.WriteText("payload/" + relativePath, content);

        public UnityEditorDeployer CreateDeployer(params DeployableFile[] files)
            => new(Source(), DeploymentManifest.ForFiles(files));

        public UnityEditorDeployer CreateDeployer(DeploymentManifest manifest)
            => new(Source(), manifest);

        public string ProjectPath(string projectRelativePath)
            => Path.Combine(ProjectDirectory, projectRelativePath.Replace('/', Path.DirectorySeparatorChar));

        public bool ProjectFileExists(string projectRelativePath) => File.Exists(ProjectPath(projectRelativePath));

        public string ReadProjectText(string projectRelativePath) => File.ReadAllText(ProjectPath(projectRelativePath));

        public byte[] ReadProjectBytes(string projectRelativePath) => File.ReadAllBytes(ProjectPath(projectRelativePath));

        public string WriteProjectText(string projectRelativePath, string content)
            => Workspace.WriteText("UnityProject/" + projectRelativePath, content);

        public bool OwnedManifestExists => ProjectFileExists(OwnedManifestProjectPath);

        public OwnedDeploymentManifest? LoadOwnedManifest()
            => OwnedDeploymentManifest.TryLoad(ProjectPath(OwnedManifestProjectPath));

        /// <summary>Rewrites the installed owned manifest (used to simulate a tampered manifest).</summary>
        public void RewriteOwnedManifest(Func<string, string> transform)
            => WriteProjectText(OwnedManifestProjectPath, transform(ReadProjectText(OwnedManifestProjectPath)));

        public void Dispose() => Workspace.Dispose();
    }

    internal static class DeploymentTestHashes
    {
        public static string Sha256Hex(string text) => Sha256Hex(System.Text.Encoding.UTF8.GetBytes(text));

        public static string Sha256Hex(byte[] content)
            => Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
    }
}
