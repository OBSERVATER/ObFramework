using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using NUnit.Framework;
using ObFramework.Cli.Deployment;

namespace ObFramework.Test.Cli
{
    /// <summary>
    /// Pure .NET tests for uninstalling the Unity Editor payload. The core requirement is that
    /// remove never deletes user content (scripts, Prefabs, setup.json) and only deletes files the
    /// deployer owns and that are still unchanged.
    /// </summary>
    [TestFixture]
    internal sealed class UnityEditorRemovalTest
    {
        private static DeployableFile EditorSource(string source, string target)
            => new(source, target, "editor-source");

        private static UnityEditorDeployer CreateTwoFileDeployer(DeploymentTestContext context)
        {
            context.AddPayload("Editor/Tool.cs", "// tool");
            context.AddPayload("Editor/Tool.cs.meta", "guid: tool");
            context.AddPayload("Editor/ObFramework.Editor.asmdef", "{}");
            return context.CreateDeployer(
                EditorSource("Editor/Tool.cs", "Assets/ObFramework/Editor/Tool.cs"),
                new DeployableFile("Editor/Tool.cs.meta", "Assets/ObFramework/Editor/Tool.cs.meta", "meta"),
                new DeployableFile("Editor/ObFramework.Editor.asmdef", "Assets/ObFramework/Editor/ObFramework.Editor.asmdef", "asmdef"));
        }

        [Test]
        public void Remove_DeletesOwnedFilesAndPreservesUserContent()
        {
            using var context = new DeploymentTestContext();
            var deployer = CreateTwoFileDeployer(context);
            deployer.Deploy(context.ProjectDirectory);

            // User-owned content that must survive any remove.
            context.WriteProjectText("Assets/Scripts/MyScript.cs", "// user script");
            context.WriteProjectText("Assets/Prefabs/Shop.prefab", "%YAML user prefab");
            context.WriteProjectText("setup.json", "{ \"root\": \"" + context.ProjectDirectory.Replace("\\", "/") + "\" }");
            context.WriteProjectText("Assets/ObFramework/Editor/UserNotes.md", "user notes");
            context.WriteProjectText("Assets/ObFramework/Editor/UserScript.cs", "// user script next to tooling");

            var result = deployer.Remove(context.ProjectDirectory);

            Assert.That(result.RemovedCount, Is.EqualTo(3), result.Summary);
            Assert.That(context.ProjectFileExists("Assets/ObFramework/Editor/Tool.cs"), Is.False);
            Assert.That(context.ProjectFileExists("Assets/ObFramework/Editor/Tool.cs.meta"), Is.False);
            Assert.That(context.ProjectFileExists("Assets/ObFramework/Editor/ObFramework.Editor.asmdef"), Is.False);
            Assert.That(context.OwnedManifestExists, Is.False);

            Assert.That(context.ReadProjectText("Assets/Scripts/MyScript.cs"), Is.EqualTo("// user script"));
            Assert.That(context.ReadProjectText("Assets/Prefabs/Shop.prefab"), Is.EqualTo("%YAML user prefab"));
            Assert.That(context.ProjectFileExists("setup.json"), Is.True);
            Assert.That(context.ProjectFileExists("Assets/ObFramework/Editor/UserNotes.md"), Is.True);
            Assert.That(context.ProjectFileExists("Assets/ObFramework/Editor/UserScript.cs"), Is.True);
            Assert.That(context.Workspace.DirectoryExists("UnityProject/Assets"), Is.True);
        }

        [Test]
        public void Remove_WithoutOwnedManifest_IsNoOp()
        {
            using var context = new DeploymentTestContext();
            context.WriteProjectText("Assets/Scripts/MyScript.cs", "// user script");
            var deployer = CreateTwoFileDeployer(context);

            var result = deployer.Remove(context.ProjectDirectory);

            Assert.That(result.Changes, Is.Empty);
            Assert.That(result.Warnings, Has.Some.Contains("nothing to remove"));
            Assert.That(context.ReadProjectText("Assets/Scripts/MyScript.cs"), Is.EqualTo("// user script"));
        }

        [Test]
        public void Remove_IsIdempotent()
        {
            using var context = new DeploymentTestContext();
            var deployer = CreateTwoFileDeployer(context);
            deployer.Deploy(context.ProjectDirectory);

            var first = deployer.Remove(context.ProjectDirectory);
            var second = deployer.Remove(context.ProjectDirectory);

            Assert.That(first.RemovedCount, Is.EqualTo(3), first.Summary);
            Assert.That(second.Changes, Is.Empty);
            Assert.That(second.Warnings, Has.Some.Contains("nothing to remove"));
        }

        [Test]
        public void Remove_PreservesModifiedOwnedFile()
        {
            using var context = new DeploymentTestContext();
            var deployer = CreateTwoFileDeployer(context);
            deployer.Deploy(context.ProjectDirectory);

            context.WriteProjectText("Assets/ObFramework/Editor/Tool.cs", "// user modified deployed file");

            var result = deployer.Remove(context.ProjectDirectory);

            Assert.That(context.ProjectFileExists("Assets/ObFramework/Editor/Tool.cs"), Is.True);
            Assert.That(
                context.ReadProjectText("Assets/ObFramework/Editor/Tool.cs"),
                Is.EqualTo("// user modified deployed file"));
            Assert.That(result.SkippedCount, Is.EqualTo(1), result.Summary);
            Assert.That(result.Warnings, Has.Some.Contains("modified"));
            Assert.That(context.ProjectFileExists("Assets/ObFramework/Editor/ObFramework.Editor.asmdef"), Is.False);

            // Ownership is kept for the file that could not be removed.
            Assert.That(context.OwnedManifestExists, Is.True);
            var owned = context.LoadOwnedManifest()!;
            Assert.That(
                owned.Files.Select(static file => file.Path),
                Is.EqualTo(new[] { "Assets/ObFramework/Editor/Tool.cs" }));
        }

        [Test]
        public void Remove_DoesNotDeleteSetupJsonListedByTamperedManifest()
        {
            using var context = new DeploymentTestContext();
            var deployer = CreateTwoFileDeployer(context);
            deployer.Deploy(context.ProjectDirectory);

            const string setupJson = "{ \"root\": \"D:/Game\" }";
            context.WriteProjectText("setup.json", setupJson);

            context.RewriteOwnedManifest(manifestJson =>
            {
                var node = JsonNode.Parse(manifestJson)!;
                var files = node["files"]!.AsArray();
                files.Add(new JsonObject
                {
                    ["path"] = "setup.json",
                    ["sha256"] = DeploymentTestHashes.Sha256Hex(setupJson),
                    ["kind"] = "file",
                });
                return node.ToJsonString();
            });

            var result = deployer.Remove(context.ProjectDirectory);

            Assert.That(context.ProjectFileExists("setup.json"), Is.True);
            Assert.That(context.ReadProjectText("setup.json"), Is.EqualTo(setupJson));
            Assert.That(result.Warnings, Has.Some.Contains("not a deployable Assets path"));
            Assert.That(context.OwnedManifestExists, Is.True);
        }

        [Test]
        public void Remove_DryRun_DeletesNothing()
        {
            using var context = new DeploymentTestContext();
            var deployer = CreateTwoFileDeployer(context);
            deployer.Deploy(context.ProjectDirectory);
            var manifestBefore = context.ReadProjectText(context.OwnedManifestProjectPath);

            var result = deployer.Remove(context.ProjectDirectory, new DeploymentOptions { DryRun = true });

            Assert.That(result.DryRun, Is.True);
            Assert.That(result.RemovedCount, Is.EqualTo(3), result.Summary);
            Assert.That(context.ProjectFileExists("Assets/ObFramework/Editor/Tool.cs"), Is.True);
            Assert.That(context.ProjectFileExists("Assets/ObFramework/Editor/ObFramework.Editor.asmdef"), Is.True);
            Assert.That(context.ReadProjectText(context.OwnedManifestProjectPath), Is.EqualTo(manifestBefore));
        }
    }
}
