using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using NUnit.Framework;
using ObFramework.Cli.Commands.Config;

namespace ObFramework.Test.Cli
{
    [TestFixture]
    internal sealed class SetupJsonDocumentTest
    {
        private static SetupConfigRegistration UnityRegistration()
            => new("unity", "unity", typeof(UnityLikeTestConfig));

        private static string Quote(string value) => JsonSerializer.Serialize(value);

        [Test]
        public void Load_ParsesAbsoluteRootAndModuleSection()
        {
            using var workspace = new TempWorkspace();
            workspace.WriteText(
                "setup.json",
                $$"""
                {
                  "root": {{Quote(workspace.Root)}},
                  "modules": {
                    "unity": {
                      "scriptRoot": "Assets/Generated",
                      "keyFormat": "UI/{view}",
                      "retries": 5
                    }
                  }
                }
                """);

            var document = SetupJsonDocument.Load(workspace.FullPath("setup.json"));

            Assert.That(document.Exists, Is.True);
            Assert.That(
                document.GetAbsoluteRoot(),
                Is.EqualTo(Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspace.Root))));

            var config = document.GetModuleConfig<UnityLikeTestConfig>("unity");
            Assert.That(config, Is.Not.Null);
            Assert.That(config!.ScriptRoot, Is.EqualTo("Assets/Generated"));
            Assert.That(config.KeyFormat, Is.EqualTo("UI/{view}"));
            Assert.That(config.Retries, Is.EqualTo(5));
        }

        [Test]
        public void Load_RejectsNonObjectRoot()
        {
            using var workspace = new TempWorkspace();
            workspace.WriteText("setup.json", "[]");

            Assert.Throws<SetupConfigException>(() => SetupJsonDocument.Load(workspace.FullPath("setup.json")));
        }

        [Test]
        public void Load_RejectsInvalidJson()
        {
            using var workspace = new TempWorkspace();
            workspace.WriteText("setup.json", "{ nope");

            Assert.Throws<SetupConfigException>(() => SetupJsonDocument.Load(workspace.FullPath("setup.json")));
        }

        [Test]
        public void Load_RejectsEmptyFile()
        {
            using var workspace = new TempWorkspace();
            workspace.WriteText("setup.json", "   ");

            Assert.Throws<SetupConfigException>(() => SetupJsonDocument.Load(workspace.FullPath("setup.json")));
        }

        [Test]
        public void Load_MissingFile_Throws()
        {
            using var workspace = new TempWorkspace();

            Assert.Throws<SetupConfigException>(
                () => SetupJsonDocument.Load(workspace.FullPath("missing/setup.json")));
        }

        [Test]
        public void GetModuleConfig_MissingSection_ReturnsNull()
        {
            using var workspace = new TempWorkspace();
            var document = SetupJsonDocument.Parse($$"""{ "root": {{Quote(workspace.Root)}} }""");

            Assert.That(document.GetModuleConfig<UnityLikeTestConfig>("unity"), Is.Null);
            Assert.That(document.HasModuleSection("unity"), Is.False);
        }

        [Test]
        public void GetModuleConfig_NonObjectSection_Throws()
        {
            using var workspace = new TempWorkspace();
            var document = SetupJsonDocument.Parse(
                $$"""{ "root": {{Quote(workspace.Root)}}, "modules": { "unity": 5 } }""");

            Assert.Throws<SetupConfigException>(() => document.GetModuleConfig<UnityLikeTestConfig>("unity"));
        }

        [Test]
        public void Save_PreservesUnknownTopLevelAndUnregisteredSections()
        {
            using var workspace = new TempWorkspace();
            workspace.WriteText(
                "setup.json",
                $$"""
                {
                  "root": {{Quote(workspace.Root)}},
                  "tool": "game-project",
                  "custom": { "keep": [1, 2, 3], "nested": { "enabled": true } },
                  "modules": {
                    "unity": { "scriptRoot": "Old/Path", "keyFormat": "UI/{view}" },
                    "godot": { "uiRoot": "res://UI", "keys": ["a", "b"] },
                    "disabledModule": { "enabled": false }
                  }
                }
                """);

            var original = JsonNode.Parse(workspace.ReadText("setup.json"))!;
            var registration = UnityRegistration();
            var document = SetupJsonDocument.Load(workspace.FullPath("setup.json"));
            var config = (UnityLikeTestConfig)document.GetOrCreateModuleConfig(registration);
            config.ScriptRoot = "Assets/New/UI";
            document.SetModuleConfig(registration, config);
            document.Save();

            var saved = JsonNode.Parse(workspace.ReadText("setup.json"))!;

            Assert.That(saved["tool"]!.GetValue<string>(), Is.EqualTo("game-project"));
            Assert.That(JsonNode.DeepEquals(saved["custom"], original["custom"]), Is.True);
            Assert.That(
                JsonNode.DeepEquals(saved["modules"]!["godot"], original["modules"]!["godot"]),
                Is.True);
            Assert.That(
                JsonNode.DeepEquals(saved["modules"]!["disabledModule"], original["modules"]!["disabledModule"]),
                Is.True);
            Assert.That(saved["modules"]!["unity"]!["scriptRoot"]!.GetValue<string>(), Is.EqualTo("Assets/New/UI"));
            Assert.That(saved["root"]!.GetValue<string>(), Is.EqualTo(workspace.Root));
        }

        [Test]
        public void Save_OnlyReplacesCurrentModuleSection()
        {
            using var workspace = new TempWorkspace();
            workspace.WriteText(
                "setup.json",
                $$"""
                {
                  "root": {{Quote(workspace.Root)}},
                  "modules": {
                    "unity": { "scriptRoot": "Old" },
                    "other": { "syncRoot": "Assets/Sync" }
                  }
                }
                """);

            var before = JsonNode.Parse(workspace.ReadText("setup.json"))!;
            var otherBefore = before["modules"]!["other"]!.ToJsonString();

            var registration = UnityRegistration();
            var document = SetupJsonDocument.Load(workspace.FullPath("setup.json"));
            var config = (UnityLikeTestConfig)document.GetOrCreateModuleConfig(registration);
            config.ScriptRoot = "New";
            document.SetModuleConfig(registration, config);
            document.Save();

            var after = JsonNode.Parse(workspace.ReadText("setup.json"))!;

            Assert.That(after["modules"]!["other"]!.ToJsonString(), Is.EqualTo(otherBefore));
            Assert.That(after["modules"]!["unity"]!["scriptRoot"]!.GetValue<string>(), Is.EqualTo("New"));
        }

        [Test]
        public void SetModuleConfig_CreatesModulesContainerAndSaves()
        {
            using var workspace = new TempWorkspace();
            var registration = UnityRegistration();
            var document = SetupJsonDocument.Create(workspace.FullPath("nested/dir/setup.json"));
            document.SetRoot(workspace.Root);
            document.SetModuleConfig(registration, new UnityLikeTestConfig { ScriptRoot = "Assets/UI" });

            document.Save();

            Assert.That(workspace.Exists("nested/dir/setup.json"), Is.True);
            var reloaded = SetupJsonDocument.Load(workspace.FullPath("nested/dir/setup.json"));
            Assert.That(reloaded.ModuleSectionNames, Is.EqualTo(new[] { "unity" }));
            Assert.That(
                reloaded.GetModuleConfig<UnityLikeTestConfig>("unity")!.ScriptRoot,
                Is.EqualTo("Assets/UI"));
        }

        [Test]
        public void SetRoot_RejectsRelativeRoot()
        {
            using var workspace = new TempWorkspace();
            var document = SetupJsonDocument.Create(workspace.FullPath("setup.json"));

            Assert.Throws<SetupConfigException>(() => document.SetRoot("relative/path"));
        }

        [Test]
        public void GetAbsoluteRoot_RejectsRelativeRootFromFile()
        {
            using var workspace = new TempWorkspace();
            workspace.WriteText("setup.json", """{ "root": "relative/path" }""");
            var document = SetupJsonDocument.Load(workspace.FullPath("setup.json"));

            Assert.Throws<SetupConfigException>(() => document.GetAbsoluteRoot());
            Assert.That(document.TryGetAbsoluteRoot(out _, out var error), Is.False);
            Assert.That(error, Does.Contain("absolute"));
        }

        [Test]
        public void GetAbsoluteRoot_MissingRoot_Throws()
        {
            using var workspace = new TempWorkspace();
            var document = SetupJsonDocument.Parse("{}");

            Assert.Throws<SetupConfigException>(() => document.GetAbsoluteRoot());
        }

        [Test]
        public void ModuleSectionNames_IncludesRegisteredAndUnknownSections()
        {
            using var workspace = new TempWorkspace();
            var document = SetupJsonDocument.Parse(
                $$"""
                {
                  "root": {{Quote(workspace.Root)}},
                  "modules": {
                    "unity": { "scriptRoot": "Assets/UI" },
                    "retiredEngine": { "x": 1 }
                  }
                }
                """);

            Assert.That(document.ModuleSectionNames, Is.EqualTo(new[] { "unity", "retiredEngine" }));
        }

        [Test]
        public void RemoveModuleConfig_RemovesOnlyTargetSection()
        {
            using var workspace = new TempWorkspace();
            var document = SetupJsonDocument.Parse(
                $$"""
                {
                  "root": {{Quote(workspace.Root)}},
                  "modules": {
                    "unity": { "scriptRoot": "Assets/UI" },
                    "other": { "syncRoot": "Assets/Sync" }
                  }
                }
                """);

            Assert.That(document.RemoveModuleConfig("unity"), Is.True);
            Assert.That(document.RemoveModuleConfig("unity"), Is.False);
            Assert.That(document.ModuleSectionNames, Is.EqualTo(new[] { "other" }));
        }

        [Test]
        public void Create_DoesNotWriteUntilSave()
        {
            using var workspace = new TempWorkspace();
            var document = SetupJsonDocument.Create(workspace.FullPath("setup.json"));

            Assert.That(document.Exists, Is.False);
            Assert.That(workspace.Exists("setup.json"), Is.False);

            document.SetRoot(workspace.Root);
            document.Save();

            Assert.That(document.Exists, Is.True);
            Assert.That(workspace.Exists("setup.json"), Is.True);
        }

        [Test]
        public void TryLoad_ReturnsNullForMissingFile()
        {
            using var workspace = new TempWorkspace();

            Assert.That(SetupJsonDocument.TryLoad(workspace.FullPath("setup.json")), Is.Null);
        }

        [Test]
        public void PropertyNames_ReportsUnknownTopLevelProperties()
        {
            using var workspace = new TempWorkspace();
            var document = SetupJsonDocument.Parse(
                $$"""{ "root": {{Quote(workspace.Root)}}, "custom": 1, "tool": "x" }""");

            Assert.That(document.PropertyNames, Is.EquivalentTo(new[] { "root", "custom", "tool" }));
        }
    }
}
