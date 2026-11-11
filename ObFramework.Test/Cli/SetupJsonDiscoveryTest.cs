using System;
using System.IO;
using NUnit.Framework;
using ObFramework.Cli.Config;

namespace ObFramework.Test.Cli
{
    [TestFixture]
    internal sealed class SetupJsonDiscoveryTest
    {
        [Test]
        public void FindSetupFile_WalksUpFromNestedDirectory()
        {
            using var workspace = new TempWorkspace();
            var setupPath = workspace.WriteText("setup.json", "{}");
            workspace.EnsureDirectory("a/b/c");

            var found = SetupJsonDiscovery.FindSetupFile(workspace.FullPath("a/b/c"));

            Assert.That(found, Is.EqualTo(Path.GetFullPath(setupPath)));
        }

        [Test]
        public void FindSetupFile_PrefersSetupJsonOverLegacyObfJson()
        {
            using var workspace = new TempWorkspace();
            workspace.WriteText("obf.json", """{ "legacy": true }""");
            var setupPath = workspace.WriteText("setup.json", "{}");
            workspace.EnsureDirectory("a/b");

            var found = SetupJsonDiscovery.FindSetupFile(workspace.FullPath("a/b"));

            Assert.That(found, Is.EqualTo(Path.GetFullPath(setupPath)));
            Assert.That(
                File.ReadAllText(found!),
                Is.EqualTo("{}"),
                "setup.json must win over a legacy obf.json; obf.json is intentionally never read.");
        }

        [Test]
        public void FindSetupFile_NearestFileWins()
        {
            using var workspace = new TempWorkspace();
            workspace.WriteText("setup.json", "{}");
            var nearest = workspace.WriteText("a/setup.json", "{}");
            workspace.EnsureDirectory("a/b/c");

            var found = SetupJsonDiscovery.FindSetupFile(workspace.FullPath("a/b/c"));

            Assert.That(found, Is.EqualTo(Path.GetFullPath(nearest)));
        }

        [Test]
        public void FindSetupFile_ReturnsNullWhenAbsent()
        {
            using var workspace = new TempWorkspace();
            workspace.EnsureDirectory("empty/nested");

            Assert.That(SetupJsonDiscovery.FindSetupFile(workspace.FullPath("empty/nested")), Is.Null);
        }

        [Test]
        public void ResolveSetupFile_ExplicitFileWinsOverDiscovery()
        {
            using var workspace = new TempWorkspace();
            workspace.WriteText("setup.json", "{}");
            var explicitPath = workspace.WriteText("custom/other.json", "{}");

            var resolved = SetupJsonDiscovery.ResolveSetupFile("custom/other.json", workspace.Root);

            Assert.That(resolved, Is.EqualTo(Path.GetFullPath(explicitPath)));
        }

        [Test]
        public void ResolveSetupFile_ResolvesRelativeFileAgainstStartDirectory()
        {
            using var workspace = new TempWorkspace();
            workspace.EnsureDirectory("nested");
            var explicitPath = workspace.WriteText("nested/alt.json", "{}");

            var resolved = SetupJsonDiscovery.ResolveSetupFile("alt.json", workspace.FullPath("nested"));

            Assert.That(resolved, Is.EqualTo(Path.GetFullPath(explicitPath)));
        }

        [Test]
        public void ResolveSetupFile_MissingExplicitFile_Throws()
        {
            using var workspace = new TempWorkspace();

            var error = Assert.Throws<SetupConfigException>(
                () => SetupJsonDiscovery.ResolveSetupFile("missing.json", workspace.Root));

            Assert.That(error!.Message, Does.Contain("not found"));
        }

        [Test]
        public void ResolveSetupFile_MissingExplicitFile_ToleratedWhenNotRequired()
        {
            using var workspace = new TempWorkspace();

            var resolved = SetupJsonDiscovery.ResolveSetupFile(
                "missing.json",
                workspace.Root,
                requireExplicitFileExists: false);

            Assert.That(resolved, Is.EqualTo(Path.GetFullPath(Path.Combine(workspace.Root, "missing.json"))));
        }

        [Test]
        public void ResolveSetupFile_ReturnsDiscoveredFile()
        {
            using var workspace = new TempWorkspace();
            var setupPath = workspace.WriteText("setup.json", "{}");
            workspace.EnsureDirectory("deep/nested");

            var resolved = SetupJsonDiscovery.ResolveSetupFile(null, workspace.FullPath("deep/nested"));

            Assert.That(resolved, Is.EqualTo(Path.GetFullPath(setupPath)));
        }

        [Test]
        public void ResolveSetupFile_ReturnsNullWhenNoFileAndNoExplicitPath()
        {
            using var workspace = new TempWorkspace();
            workspace.EnsureDirectory("empty");

            Assert.That(SetupJsonDiscovery.ResolveSetupFile(null, workspace.FullPath("empty")), Is.Null);
        }
    }
}
