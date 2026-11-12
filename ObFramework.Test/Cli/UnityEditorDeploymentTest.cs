using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using NUnit.Framework;
using ObFramework.Cli.Deployment;

namespace ObFramework.Test.Cli
{
    /// <summary>
    /// Pure .NET tests for installing/updating the Unity Editor payload. They assert file layout,
    /// owned-file bookkeeping and idempotency; they deliberately do not claim Unity Editor import
    /// verification.
    /// </summary>
    [TestFixture]
    internal sealed class UnityEditorDeploymentTest
    {
        private static DeployableFile EditorSource(string source, string target)
            => new(source, target, "editor-source");

        [Test]
        public void Deploy_InstallsPayloadAndOwnedManifest()
        {
            using var context = new DeploymentTestContext();
            context.AddPayload("Editor/ObFrameworkEditorWindow.cs", "// editor tool v1");
            context.AddPayload("Editor/ObFramework.Editor.asmdef", "{ \"name\": \"ObFramework.Editor\" }");
            context.AddPayload("Editor/ObFrameworkEditorWindow.cs.meta", "fileFormatVersion: 2\n");

            var deployer = context.CreateDeployer(
                EditorSource("Editor/ObFrameworkEditorWindow.cs", "Assets/ObFramework/Editor/ObFrameworkEditorWindow.cs"),
                new DeployableFile("Editor/ObFramework.Editor.asmdef", "Assets/ObFramework/Editor/ObFramework.Editor.asmdef", "asmdef"),
                new DeployableFile("Editor/ObFrameworkEditorWindow.cs.meta", "Assets/ObFramework/Editor/ObFrameworkEditorWindow.cs.meta", "meta"));

            var result = deployer.Deploy(context.ProjectDirectory);

            Assert.That(result.InstalledCount, Is.EqualTo(3), result.Summary);
            Assert.That(result.UpdatedCount, Is.EqualTo(0));
            Assert.That(result.HasChanges, Is.True);
            Assert.That(
                context.ReadProjectText("Assets/ObFramework/Editor/ObFrameworkEditorWindow.cs"),
                Is.EqualTo("// editor tool v1"));
            Assert.That(
                context.ReadProjectText("Assets/ObFramework/Editor/ObFramework.Editor.asmdef"),
                Does.Contain("ObFramework.Editor"));

            Assert.That(context.OwnedManifestExists, Is.True);
            var owned = context.LoadOwnedManifest();
            Assert.That(owned, Is.Not.Null);
            Assert.That(owned!.SchemaVersion, Is.EqualTo(OwnedDeploymentManifest.CurrentSchemaVersion));
            Assert.That(owned.Tool, Is.EqualTo("ob"));
            Assert.That(
                owned.Files.Select(static file => file.Path),
                Is.EqualTo(new[]
                {
                    "Assets/ObFramework/Editor/ObFramework.Editor.asmdef",
                    "Assets/ObFramework/Editor/ObFrameworkEditorWindow.cs",
                    "Assets/ObFramework/Editor/ObFrameworkEditorWindow.cs.meta",
                }));

            foreach (var file in owned.Files)
            {
                Assert.That(
                    file.Sha256,
                    Is.EqualTo(DeploymentTestHashes.Sha256Hex(context.ReadProjectBytes(file.Path))),
                    file.Path);
            }
        }

        [Test]
        public void Deploy_RepeatedInstall_IsIdempotent()
        {
            using var context = new DeploymentTestContext();
            context.AddPayload("Editor/Tool.cs", "// v1");
            context.AddPayload("Editor/Tool.cs.meta", "guid: 1");
            var deployer = context.CreateDeployer(
                EditorSource("Editor/Tool.cs", "Assets/ObFramework/Editor/Tool.cs"),
                new DeployableFile("Editor/Tool.cs.meta", "Assets/ObFramework/Editor/Tool.cs.meta", "meta"));

            var first = deployer.Deploy(context.ProjectDirectory);
            var manifestAfterFirst = context.ReadProjectText(context.OwnedManifestProjectPath);
            var toolAfterFirst = context.ReadProjectBytes("Assets/ObFramework/Editor/Tool.cs");

            var second = deployer.Deploy(context.ProjectDirectory);

            Assert.That(first.InstalledCount, Is.EqualTo(2));
            Assert.That(second.InstalledCount, Is.EqualTo(0));
            Assert.That(second.UpdatedCount, Is.EqualTo(0));
            Assert.That(second.RemovedCount, Is.EqualTo(0));
            Assert.That(second.UnchangedCount, Is.EqualTo(2));
            Assert.That(second.HasChanges, Is.False);
            Assert.That(context.ReadProjectText(context.OwnedManifestProjectPath), Is.EqualTo(manifestAfterFirst));
            Assert.That(context.ReadProjectBytes("Assets/ObFramework/Editor/Tool.cs"), Is.EqualTo(toolAfterFirst));

            var temporaryFiles = Directory.GetFiles(context.ProjectDirectory, "*.tmp", SearchOption.AllDirectories);
            Assert.That(temporaryFiles, Is.Empty);
        }

        [Test]
        public void Deploy_Update_ReplacesOnlyOwnedFiles()
        {
            using var context = new DeploymentTestContext();
            context.AddPayload("Editor/Tool.cs", "// v1");
            context.AddPayload("Editor/Tool.cs.meta", "guid: 1");
            var deployer = context.CreateDeployer(
                EditorSource("Editor/Tool.cs", "Assets/ObFramework/Editor/Tool.cs"),
                new DeployableFile("Editor/Tool.cs.meta", "Assets/ObFramework/Editor/Tool.cs.meta", "meta"));
            deployer.Deploy(context.ProjectDirectory);

            context.AddPayload("Editor/Tool.cs", "// v2 updated");
            context.WriteProjectText("Assets/ObFramework/Editor/UserScript.cs", "// user owned file");

            var result = deployer.Deploy(context.ProjectDirectory);

            Assert.That(result.UpdatedCount, Is.EqualTo(1), result.Summary);
            Assert.That(result.InstalledCount, Is.EqualTo(0));
            Assert.That(result.UnchangedCount, Is.EqualTo(1));
            Assert.That(
                context.ReadProjectText("Assets/ObFramework/Editor/Tool.cs"),
                Is.EqualTo("// v2 updated"));
            Assert.That(
                context.ReadProjectText("Assets/ObFramework/Editor/UserScript.cs"),
                Is.EqualTo("// user owned file"));
        }

        [Test]
        public void Deploy_DoesNotOverwriteUnownedFile()
        {
            using var context = new DeploymentTestContext();
            context.AddPayload("Editor/Tool.cs", "// owned payload");
            context.AddPayload("Editor/Other.cs", "// other payload");
            context.WriteProjectText("Assets/ObFramework/Editor/Tool.cs", "// user content");

            var deployer = context.CreateDeployer(
                EditorSource("Editor/Tool.cs", "Assets/ObFramework/Editor/Tool.cs"),
                EditorSource("Editor/Other.cs", "Assets/ObFramework/Editor/Other.cs"));

            var result = deployer.Deploy(context.ProjectDirectory);

            Assert.That(result.InstalledCount, Is.EqualTo(1), result.Summary);
            Assert.That(result.SkippedCount, Is.EqualTo(1));
            Assert.That(result.Warnings, Has.Some.Contains("not owned"));
            Assert.That(
                context.ReadProjectText("Assets/ObFramework/Editor/Tool.cs"),
                Is.EqualTo("// user content"));
            Assert.That(context.OwnedManifestExists, Is.True);

            var owned = context.LoadOwnedManifest()!;
            Assert.That(
                owned.Files.Select(static file => file.Path),
                Is.EqualTo(new[] { "Assets/ObFramework/Editor/Other.cs" }));
        }

        [Test]
        public void Deploy_RemovesStaleOwnedFilesOnUpdate()
        {
            using var context = new DeploymentTestContext();
            context.AddPayload("Editor/A.cs", "// a");
            context.AddPayload("Editor/B.cs", "// b");
            context.AddPayload("Editor/C.cs", "// c");

            context.CreateDeployer(
                EditorSource("Editor/A.cs", "Assets/ObFramework/Editor/A.cs"),
                EditorSource("Editor/B.cs", "Assets/ObFramework/Editor/B.cs"))
                .Deploy(context.ProjectDirectory);

            var result = context.CreateDeployer(
                EditorSource("Editor/A.cs", "Assets/ObFramework/Editor/A.cs"),
                EditorSource("Editor/C.cs", "Assets/ObFramework/Editor/C.cs"))
                .Deploy(context.ProjectDirectory);

            Assert.That(result.RemovedCount, Is.EqualTo(1), result.Summary);
            Assert.That(result.InstalledCount, Is.EqualTo(1));
            Assert.That(result.UnchangedCount, Is.EqualTo(1));
            Assert.That(context.ProjectFileExists("Assets/ObFramework/Editor/B.cs"), Is.False);
            Assert.That(context.ProjectFileExists("Assets/ObFramework/Editor/A.cs"), Is.True);
            Assert.That(context.ProjectFileExists("Assets/ObFramework/Editor/C.cs"), Is.True);

            var owned = context.LoadOwnedManifest()!;
            Assert.That(
                owned.Files.Select(static file => file.Path),
                Is.EqualTo(new[]
                {
                    "Assets/ObFramework/Editor/A.cs",
                    "Assets/ObFramework/Editor/C.cs",
                }));
        }

        [Test]
        public void Deploy_SkipsMissingSourceResource_ThenInstallsWhenItAppears()
        {
            using var context = new DeploymentTestContext();
            context.AddPayload("Editor/A.cs", "// a");

            var deployer = context.CreateDeployer(
                EditorSource("Editor/A.cs", "Assets/ObFramework/Editor/A.cs"),
                EditorSource("Editor/Missing.cs", "Assets/ObFramework/Editor/Missing.cs"));

            var first = deployer.Deploy(context.ProjectDirectory);

            Assert.That(first.InstalledCount, Is.EqualTo(1), first.Summary);
            Assert.That(first.SkippedCount, Is.EqualTo(1));
            Assert.That(first.Warnings, Has.Some.Contains("Missing.cs"));

            context.AddPayload("Editor/Missing.cs", "// appeared later");
            var second = deployer.Deploy(context.ProjectDirectory);

            Assert.That(second.InstalledCount, Is.EqualTo(1), second.Summary);
            Assert.That(second.UnchangedCount, Is.EqualTo(1));
            Assert.That(context.ProjectFileExists("Assets/ObFramework/Editor/Missing.cs"), Is.True);
        }

        [Test]
        public void Deploy_AutoDeployResources_MapsEveryResourceToTargetRoot()
        {
            using var context = new DeploymentTestContext();
            context.AddPayload("ObFrameworkEditorWindow.cs", "// window");
            context.AddPayload("ObFramework.Editor.asmdef", "{}");
            context.AddPayload("ObFrameworkEditorWindow.cs.meta", "guid: 1");
            context.AddPayload("UI/ObFrameworkEditorWindow.cs.meta", "guid: 2");
            context.AddPayload("Analyzers/ObFramework.Generator.Unity.dll", "placeholder analyzer bytes");
            context.AddPayload("README.md", "docs are not deployed");
            context.AddPayload("deploy-manifest.json", "{}");

            var manifest = new DeploymentManifest
            {
                AutoDeployResources = true,
                TargetRoot = "Assets/ObFramework/Editor",
                Exclude = new[] { "**/*.md" },
            };
            var deployer = context.CreateDeployer(manifest);

            var result = deployer.Deploy(context.ProjectDirectory);

            Assert.That(result.InstalledCount, Is.EqualTo(5), result.Summary);
            Assert.That(context.ProjectFileExists("Assets/ObFramework/Editor/ObFrameworkEditorWindow.cs"), Is.True);
            Assert.That(context.ProjectFileExists("Assets/ObFramework/Editor/ObFramework.Editor.asmdef"), Is.True);
            Assert.That(context.ProjectFileExists("Assets/ObFramework/Editor/UI/ObFrameworkEditorWindow.cs.meta"), Is.True);
            Assert.That(context.ProjectFileExists("Assets/ObFramework/Editor/Analyzers/ObFramework.Generator.Unity.dll"), Is.True);
            Assert.That(context.ProjectFileExists("Assets/ObFramework/Editor/README.md"), Is.False);
            Assert.That(context.ProjectFileExists("Assets/ObFramework/obf-deploy.json"), Is.True);

            var owned = context.LoadOwnedManifest()!;
            Assert.That(
                owned.Files.Single(static file => file.Path.EndsWith(".dll", StringComparison.Ordinal)).Kind,
                Is.EqualTo("roslyn-analyzer"));
            Assert.That(
                owned.Files.Single(static file => file.Path.EndsWith(".cs", StringComparison.Ordinal)).Kind,
                Is.EqualTo("editor-source"));
            Assert.That(
                owned.Files.Single(static file => file.Path.EndsWith(".asmdef", StringComparison.Ordinal)).Kind,
                Is.EqualTo("asmdef"));
        }

        [Test]
        public void Deploy_DryRun_WritesNothing()
        {
            using var context = new DeploymentTestContext();
            context.AddPayload("Editor/Tool.cs", "// v1");
            var deployer = context.CreateDeployer(
                EditorSource("Editor/Tool.cs", "Assets/ObFramework/Editor/Tool.cs"));

            var result = deployer.Deploy(context.ProjectDirectory, new DeploymentOptions { DryRun = true });

            Assert.That(result.DryRun, Is.True);
            Assert.That(result.InstalledCount, Is.EqualTo(1), result.Summary);
            Assert.That(context.ProjectFileExists("Assets/ObFramework/Editor/Tool.cs"), Is.False);
            Assert.That(context.OwnedManifestExists, Is.False);
            Assert.That(Directory.GetFiles(context.ProjectDirectory, "*", SearchOption.AllDirectories), Is.Empty);
        }

        [Test]
        public void Deploy_AbsentPayload_ThrowsClearDiagnostic()
        {
            using var context = new DeploymentTestContext();
            var deployer = context.CreateDeployer(
                EditorSource("Editor/Tool.cs", "Assets/ObFramework/Editor/Tool.cs"));

            var error = Assert.Throws<DeploymentException>(() => deployer.Deploy(context.ProjectDirectory));

            Assert.That(error!.Message, Does.Contain("payload"));
            Assert.That(error.Message, Does.Contain("not present"));
            Assert.That(context.OwnedManifestExists, Is.False);
        }

        [Test]
        public void Deploy_EmptyAutoPlan_ThrowsClearDiagnostic()
        {
            using var context = new DeploymentTestContext();
            var manifest = new DeploymentManifest
            {
                AutoDeployResources = true,
                TargetRoot = "Assets/ObFramework/Editor",
            };
            var deployer = context.CreateDeployer(manifest);

            var error = Assert.Throws<DeploymentException>(() => deployer.Deploy(context.ProjectDirectory));

            Assert.That(error!.Message, Does.Contain("payload"));
        }

        [Test]
        public void Deploy_RejectsProjectWithoutAssetsFolder()
        {
            using var context = new DeploymentTestContext();
            context.AddPayload("Editor/Tool.cs", "// v1");
            var notAProject = context.Workspace.EnsureDirectory("NotAUnityProject");
            var deployer = context.CreateDeployer(
                EditorSource("Editor/Tool.cs", "Assets/ObFramework/Editor/Tool.cs"));

            var error = Assert.Throws<DeploymentException>(() => deployer.Deploy(notAProject));

            Assert.That(error!.Message, Does.Contain("Assets"));
        }

        [Test]
        public void Deploy_RejectsMissingProjectDirectory()
        {
            using var context = new DeploymentTestContext();
            context.AddPayload("Editor/Tool.cs", "// v1");
            var deployer = context.CreateDeployer(
                EditorSource("Editor/Tool.cs", "Assets/ObFramework/Editor/Tool.cs"));

            Assert.Throws<DeploymentException>(
                () => deployer.Deploy(context.Workspace.FullPath("does-not-exist")));
        }

        [Test]
        public void Deploy_RejectsTargetOutsideAssets()
        {
            using var context = new DeploymentTestContext();
            context.AddPayload("Editor/Tool.cs", "// v1");

            Assert.Throws<DeploymentException>(() => context.CreateDeployer(
                EditorSource("Editor/Tool.cs", "ProjectSettings/Tool.cs")));
        }

        [Test]
        public void Deploy_RejectsEscapingTarget()
        {
            using var context = new DeploymentTestContext();

            Assert.Throws<DeploymentException>(() => context.CreateDeployer(
                EditorSource("Editor/Tool.cs", "Assets/../evil.txt")));
        }

        [Test]
        public void Deploy_RejectsDuplicateTargets()
        {
            using var context = new DeploymentTestContext();

            var errors = Assert.Throws<DeploymentException>(() => context.CreateDeployer(
                EditorSource("Editor/A.cs", "Assets/ObFramework/Editor/Same.cs"),
                EditorSource("Editor/B.cs", "Assets/ObFramework/Editor/Same.cs")));

            Assert.That(errors!.Message, Does.Contain("Duplicate"));
        }

        [Test]
        public void Deploy_AcceptsProjectRelativePathsWithDirectoryDepth()
        {
            using var context = new DeploymentTestContext();
            context.AddPayload("Deep/Nested/Tool.cs", "// deep");
            var deployer = context.CreateDeployer(
                EditorSource("Deep/Nested/Tool.cs", "Assets/ObFramework/Editor/Deep/Nested/Tool.cs"));

            var result = deployer.Deploy(context.ProjectDirectory);

            Assert.That(result.InstalledCount, Is.EqualTo(1), result.Summary);
            Assert.That(
                context.ProjectFileExists("Assets/ObFramework/Editor/Deep/Nested/Tool.cs"),
                Is.True);
        }

        [Test]
        public void AutoDeploy_ManifestJson_IsParsedAndValidated()
        {
            const string planJson = """
                {
                  "schemaVersion": 1,
                  "tool": "ob",
                  "deployment": "unity-editor",
                  "manifestPath": "Assets/ObFramework/obf-deploy.json",
                  "targetRoot": "Assets/ObFramework/Editor",
                  "autoDeployResources": true,
                  "exclude": ["deploy-manifest.json"],
                  "files": []
                }
                """;

            var manifest = DeploymentManifest.Load(planJson);

            Assert.That(manifest.SchemaVersion, Is.EqualTo(1));
            Assert.That(manifest.AutoDeployResources, Is.True);
            Assert.That(manifest.TargetRoot, Is.EqualTo("Assets/ObFramework/Editor"));
            Assert.That(manifest.ManifestPath, Is.EqualTo(DeploymentManifest.DefaultOwnedManifestPath));
        }

        [Test]
        public void EmbeddedPayload_DeployUpdateRemoveRoundTripsWithAnalyzers()
        {
            // This is the end-to-end lock for the real CLI payload: deploy, idempotent re-deploy and
            // remove must all operate on the embedded EditorDeploy + analyzer resources, while user
            // content is preserved. (Unity import itself remains sample-project validation.)
            var source = EmbeddedDeployableFileSource.CreateDefault();
            var payload = source.ListFiles();
            Assert.That(payload, Is.Not.Empty, "The CLI must embed the Unity tooling payload.");
            Assert.That(payload, Has.Member("Analyzers/ObFramework.Generator.dll"));
            Assert.That(payload, Has.Member("Analyzers/ObFramework.Generator.Unity.dll"));

            using var context = new DeploymentTestContext();
            const string userFile = "Assets/Scripts/Game.cs";
            context.WriteProjectText(userFile, "// user content");
            var deployer = new UnityEditorDeployer(source, DeploymentManifest.LoadEmbeddedPlan());

            var first = deployer.Deploy(context.ProjectDirectory);
            Assert.That(first.InstalledCount, Is.EqualTo(payload.Count), first.Summary);
            Assert.That(first.Warnings, Is.Empty, string.Join("; ", first.Warnings));
            Assert.That(
                context.ProjectFileExists("Assets/ObFramework/Analyzers/ObFramework.Generator.dll"),
                Is.True);
            Assert.That(
                context.ProjectFileExists("Assets/ObFramework/Analyzers/ObFramework.Generator.Unity.dll"),
                Is.True);
            Assert.That(
                context.ProjectFileExists("Assets/ObFramework/Analyzers/ObFramework.Generator.dll.meta"),
                Is.True);
            Assert.That(
                context.ProjectFileExists("Assets/ObFramework/Editor/ObFramework.Adapter.Unity.Editor.asmdef"),
                Is.True);

            var owned = context.LoadOwnedManifest();
            Assert.That(owned, Is.Not.Null);
            Assert.That(owned!.Files.Count, Is.EqualTo(payload.Count));

            var second = deployer.Deploy(context.ProjectDirectory);
            Assert.That(second.InstalledCount, Is.Zero);
            Assert.That(second.UpdatedCount, Is.Zero);
            Assert.That(second.UnchangedCount, Is.EqualTo(payload.Count));
            Assert.That(second.HasChanges, Is.False);

            var removed = deployer.Remove(context.ProjectDirectory);
            Assert.That(removed.RemovedCount, Is.EqualTo(payload.Count), removed.Summary);
            Assert.That(context.ProjectFileExists("Assets/ObFramework/Analyzers/ObFramework.Generator.dll"), Is.False);
            Assert.That(context.ProjectFileExists("Assets/ObFramework/Editor/ObFramework.Adapter.Unity.Editor.asmdef"), Is.False);
            Assert.That(context.OwnedManifestExists, Is.False);
            Assert.That(context.ReadProjectText(userFile), Is.EqualTo("// user content"));
        }
    }
}
