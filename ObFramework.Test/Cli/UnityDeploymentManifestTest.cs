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
            var source = RequireEmbeddedPayload();
            var files = source.ListFiles();

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
        public void EmbeddedPayload_ContainsBothRoslynAnalyzersAndImportMetas()
        {
            // Both Roslyn source generators are part of the deployable Unity tooling: CDE generates
            // ObservableProperty/RelayCommand members, the Unity generator generates the VC
            // factory/binding partial. Each ships with a .meta carrying the RoslynAnalyzer label so
            // `ob unity deploy` installs them without a manual copy step.
            var source = RequireEmbeddedPayload();
            var files = source.ListFiles();

            foreach (var name in new[] { "ObFramework.Generator", "ObFramework.Generator.Unity" })
            {
                var dll = $"Analyzers/{name}.dll";
                var meta = $"{dll}.meta";
                Assert.That(files, Does.Contain(dll), name);
                Assert.That(files, Does.Contain(meta), name);

                Assert.That(source.TryRead(dll, out var assemblyBytes), Is.True, dll);
                Assert.That(assemblyBytes.Length, Is.GreaterThan(0), dll);

                Assert.That(source.TryRead(meta, out var metaBytes), Is.True, meta);
                var metaText = System.Text.Encoding.UTF8.GetString(metaBytes);
                Assert.That(metaText, Does.Contain("RoslynAnalyzer"), name);
                // The analyzer must not be imported as a Unity plugin: platforms stay disabled.
                Assert.That(metaText, Does.Not.Contain("enabled: 1"), name);
            }
        }

        [Test]
        public void EmbeddedPlan_DeploysAnalyzersOutsideTheEditorOnlyToolFolder()
        {
            // Unity analyzer scope follows the folder/assembly definition an analyzer lives in.
            // Analyzers under the Editor-only asmdef folder only analyze Editor assemblies, so the
            // deployment plan must map them to Assets/ObFramework/Analyzers (outside Editor/).
            var manifest = DeploymentManifest.LoadEmbeddedPlan();
            var source = RequireEmbeddedPayload();
            var resolved = manifest.ResolveFiles(source);

            var analyzers = resolved
                .Where(static file => file.Kind == "roslyn-analyzer")
                .ToArray();
            Assert.That(analyzers, Has.Length.EqualTo(2), "both generators must be deployed as analyzers");

            foreach (var analyzer in analyzers)
            {
                Assert.That(
                    analyzer.Target,
                    Does.StartWith("Assets/ObFramework/Analyzers/"),
                    analyzer.Target);
                Assert.That(
                    analyzer.Target,
                    Does.Not.StartWith("Assets/ObFramework/Editor/"),
                    analyzer.Target);
                Assert.That(source.TryRead(analyzer.Source, out var content), Is.True, analyzer.Source);
                Assert.That(content.Length, Is.GreaterThan(0), analyzer.Source);
            }

            var metas = resolved
                .Where(static file => file.Source.StartsWith("Analyzers/", StringComparison.Ordinal) &&
                                      file.Source.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            Assert.That(metas, Has.Length.EqualTo(2));
            foreach (var meta in metas)
                Assert.That(meta.Target, Does.StartWith("Assets/ObFramework/Analyzers/"), meta.Target);
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

        /// <summary>
        /// The deployable payload only exists when the CLI project was built with its EditorDeploy
        /// resources; tests that exercise it must fail loudly instead of silently returning when it
        /// is absent (a vacuous pass would hide a broken packaging change).
        /// </summary>
        private static IDeployableFileSource RequireEmbeddedPayload()
        {
            var source = EmbeddedDeployableFileSource.CreateDefault();
            Assert.That(
                source.ListFiles(),
                Is.Not.Empty,
                "The Unity Editor deployable payload is not embedded in ObFramework.Cli; build the CLI project before running the CLI tests.");
            return source;
        }
    }
}
