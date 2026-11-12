using System;
using System.Linq;
using NUnit.Framework;
using ObFramework.Cli.Deployment;

namespace ObFramework.Test.Cli
{
    [TestFixture]
    internal sealed class UnityDeploymentManifestTest
    {
        [Test]
        public void EmbeddedPlan_IsPresentAndUsesGenericAutoMapping()
        {
            var manifest = DeploymentManifest.LoadEmbeddedPlan();

            Assert.That(manifest.SchemaVersion, Is.EqualTo(DeploymentManifest.CurrentSchemaVersion));
            Assert.That(manifest.Tool, Is.EqualTo("ob"));
            Assert.That(manifest.Deployment, Is.EqualTo("unity-editor"));
            Assert.That(manifest.AutoDeployResources, Is.True);
            Assert.That(manifest.TargetRoot, Is.EqualTo("Assets/ObFramework/Editor"));
            Assert.That(manifest.ManifestPath, Is.EqualTo(DeploymentManifest.DefaultOwnedManifestPath));
        }

        [Test]
        public void EmbeddedDeployableFileSource_ReadsEmbeddedPlanResource()
        {
            // The embedded deployment plan lives under its own resource prefix, which makes this a
            // stable check that the csproj EmbeddedResource wiring (prefix + LogicalName) works.
            var source = new EmbeddedDeployableFileSource(
                typeof(EmbeddedDeployableFileSource).Assembly,
                "ObFramework.Cli.UnityDeployPlan");

            Assert.That(source.ListFiles(), Has.Member("deploy-manifest.json"));
            Assert.That(source.TryRead("deploy-manifest.json", out var content), Is.True);
            Assert.That(content.Length, Is.GreaterThan(0));

            var manifest = DeploymentManifest.LoadFromSource(source);
            Assert.That(manifest.AutoDeployResources, Is.True);
        }

        [Test]
        public void EmbeddedDeployableFileSource_UnknownResource_ReturnsFalse()
        {
            var source = new EmbeddedDeployableFileSource(
                typeof(EmbeddedDeployableFileSource).Assembly,
                "ObFramework.Cli.UnityDeployPlan");

            Assert.That(source.TryRead("does/not/exist.cs", out var content), Is.False);
            Assert.That(content, Is.Empty);
        }

        [Test]
        public void LoadFromSource_ReadsPlanFromPayloadDirectory_AndDeploysAutoMappedFiles()
        {
            using var context = new DeploymentTestContext();
            context.AddPayload(
                "deploy-manifest.json",
                """
                {
                  "schemaVersion": 1,
                  "tool": "ob",
                  "deployment": "unity-editor",
                  "manifestPath": "Assets/ObFramework/obf-deploy.json",
                  "targetRoot": "Assets/ObFramework/Editor",
                  "autoDeployResources": true,
                  "files": []
                }
                """);
            context.AddPayload("Editor/Tool.cs", "// tool");

            var manifest = DeploymentManifest.LoadFromSource(context.Source());
            var deployer = context.CreateDeployer(manifest);

            var result = deployer.Deploy(context.ProjectDirectory);

            Assert.That(result.InstalledCount, Is.EqualTo(1), result.Summary);
            Assert.That(context.ProjectFileExists("Assets/ObFramework/Editor/Editor/Tool.cs"), Is.True);
            Assert.That(context.ProjectFileExists("Assets/ObFramework/Editor/deploy-manifest.json"), Is.False);
        }

        [Test]
        public void LoadFromSource_MissingManifest_Throws()
        {
            using var context = new DeploymentTestContext();

            var error = Assert.Throws<DeploymentException>(
                () => DeploymentManifest.LoadFromSource(context.Source()));

            Assert.That(error!.Message, Does.Contain(DeploymentManifest.DefaultSourceFileName));
        }

        [Test]
        public void Load_RejectsUnsupportedSchemaVersion()
        {
            Assert.Throws<DeploymentException>(() => DeploymentManifest.Load("""{ "schemaVersion": 99 }"""));
        }

        [Test]
        public void Load_RejectsInvalidJson()
        {
            Assert.Throws<DeploymentException>(() => DeploymentManifest.Load("{ nope"));
        }

        [Test]
        public void Validate_RejectsAutoTargetRootOutsideAssets()
        {
            using var context = new DeploymentTestContext();
            var manifest = new DeploymentManifest
            {
                AutoDeployResources = true,
                TargetRoot = "ProjectSettings/Editor",
            };

            Assert.Throws<DeploymentException>(() => context.CreateDeployer(manifest));
        }

        [Test]
        public void Validate_RejectsOwnedManifestOutsideAssets()
        {
            using var context = new DeploymentTestContext();
            var manifest = new DeploymentManifest
            {
                ManifestPath = "obf-deploy.json",
                Files = new[] { new DeployableFile("a.cs", "Assets/ObFramework/Editor/a.cs") },
            };

            Assert.Throws<DeploymentException>(() => context.CreateDeployer(manifest));
        }

        [Test]
        public void Validate_RejectsDuplicateSources()
        {
            using var context = new DeploymentTestContext();

            Assert.Throws<DeploymentException>(() => context.CreateDeployer(
                new DeployableFile("Editor/Tool.cs", "Assets/ObFramework/Editor/A.cs"),
                new DeployableFile("Editor/Tool.cs", "Assets/ObFramework/Editor/B.cs")));
        }

        [Test]
        public void OwnedManifest_IsDeterministicAndSorted()
        {
            var first = OwnedDeploymentManifest.ForFiles(new[]
            {
                new OwnedDeploymentFile("Assets/B.cs", "hash-b", "editor-source"),
                new OwnedDeploymentFile("Assets/A.cs", "hash-a", "meta"),
            });
            var second = OwnedDeploymentManifest.ForFiles(new[]
            {
                new OwnedDeploymentFile("Assets/A.cs", "hash-a", "meta"),
                new OwnedDeploymentFile("Assets/B.cs", "hash-b", "editor-source"),
            });

            Assert.That(first.ToJson(), Is.EqualTo(second.ToJson()));
            Assert.That(
                first.Files.Select(static file => file.Path),
                Is.EqualTo(new[] { "Assets/A.cs", "Assets/B.cs" }));
        }

        [Test]
        public void OwnedManifest_RejectsEscapingPaths()
        {
            using var context = new DeploymentTestContext();
            context.WriteProjectText(
                "Assets/ObFramework/obf-deploy.json",
                """{ "schemaVersion": 1, "tool": "ob", "deployment": "unity-editor", "files": [ { "path": "../escape.cs", "sha256": "x" } ] }""");

            Assert.Throws<DeploymentException>(
                () => context.LoadOwnedManifest());
        }

        [Test]
        public void EmbeddedPayload_KeepsDotCsMetaFilesInMainResourceSet()
        {
            // Regression guard for MSBuild culture inference: a payload file named
            // "Foo.cs.meta" is parsed as resource "Foo" + culture "cs" (Czech) + extension "meta"
            // unless WithCulture="false" is set on the payload EmbeddedResource item in
            // ObFramework.Cli.csproj. Without it, MSBuild moves every *.cs.meta into a satellite
            // assembly and the main payload silently loses them (ob unity deploy then installs an
            // incomplete Editor metadata set).
            var source = EmbeddedDeployableFileSource.CreateDefault();
            var files = source.ListFiles();
            if (files.Count == 0)
                return; // payload not embedded in this build configuration (EditorDeploy absent)

            var metaFiles = files
                .Where(static file => file.EndsWith(".cs.meta", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            Assert.That(
                metaFiles,
                Is.Not.Empty,
                "The Unity Editor deployable payload must keep its *.cs.meta files in the main resource set.");
            foreach (var metaFile in metaFiles)
                Assert.That(source.TryRead(metaFile, out var content), Is.True, metaFile);
        }

        [Test]
        public void EmbeddedPayload_ContainsUnityRoslynAnalyzerAndImportMeta()
        {
            // The Unity Roslyn source generator is part of the deployable Editor tooling: the CLI
            // embeds the built ObFramework.Generator.Unity.dll plus a .meta carrying the
            // RoslynAnalyzer label, so `ob unity deploy` installs it without a manual copy step.
            var source = EmbeddedDeployableFileSource.CreateDefault();
            var files = source.ListFiles();
            if (files.Count == 0)
                return; // payload not embedded in this build configuration (EditorDeploy absent)

            Assert.That(files, Does.Contain("ObFramework.Generator.Unity.dll"));
            Assert.That(files, Does.Contain("ObFramework.Generator.Unity.dll.meta"));

            Assert.That(source.TryRead("ObFramework.Generator.Unity.dll", out var assemblyBytes), Is.True);
            Assert.That(assemblyBytes.Length, Is.GreaterThan(0));

            Assert.That(source.TryRead("ObFramework.Generator.Unity.dll.meta", out var metaBytes), Is.True);
            var meta = System.Text.Encoding.UTF8.GetString(metaBytes);
            Assert.That(meta, Does.Contain("RoslynAnalyzer"));
        }

        [Test]
        public void AutoDeploy_RespectsExcludePatterns()
        {
            using var context = new DeploymentTestContext();
            context.AddPayload("Tool.cs", "// tool");
            context.AddPayload("Editor/Notes.txt", "not excluded");
            context.AddPayload("README.md", "root docs");
            context.AddPayload("docs/Guide.md", "nested docs");

            var manifest = new DeploymentManifest
            {
                AutoDeployResources = true,
                TargetRoot = "Assets/ObFramework/Editor",
                Exclude = new[] { "**/*.md", "docs/" },
            };
            var deployer = context.CreateDeployer(manifest);

            var result = deployer.Deploy(context.ProjectDirectory);

            Assert.That(result.InstalledCount, Is.EqualTo(2), result.Summary);
            Assert.That(context.ProjectFileExists("Assets/ObFramework/Editor/Tool.cs"), Is.True);
            Assert.That(context.ProjectFileExists("Assets/ObFramework/Editor/Editor/Notes.txt"), Is.True);
            Assert.That(context.ProjectFileExists("Assets/ObFramework/Editor/README.md"), Is.False);
            Assert.That(context.ProjectFileExists("Assets/ObFramework/Editor/docs/Guide.md"), Is.False);
        }
    }
}
