using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using ObFramework.Cli.Config;

namespace ObFramework.Test.Cli
{
    [TestFixture]
    internal sealed class SetupConfigPathsTest
    {
        [Test]
        public void ValidateAbsoluteRoot_ReturnsNormalizedRoot()
        {
            using var workspace = new TempWorkspace();
            var rootWithTrailingSeparator = workspace.Root + Path.DirectorySeparatorChar;

            var normalized = SetupConfigPaths.ValidateAbsoluteRoot(rootWithTrailingSeparator);

            Assert.That(normalized, Is.EqualTo(Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspace.Root))));
        }

        [Test]
        public void ValidateAbsoluteRoot_RejectsRelativePath()
        {
            var error = Assert.Throws<SetupConfigException>(
                () => SetupConfigPaths.ValidateAbsoluteRoot("Assets/UI"));

            Assert.That(error!.Message, Does.Contain("absolute"));
        }

        [Test]
        public void ValidateAbsoluteRoot_RejectsEmptyAndMissingValue()
        {
            Assert.Throws<SetupConfigException>(() => SetupConfigPaths.ValidateAbsoluteRoot(null));
            Assert.Throws<SetupConfigException>(() => SetupConfigPaths.ValidateAbsoluteRoot("   "));
        }

        [Test]
        public void NormalizeModulePath_NormalizesSegmentsAndSeparators()
        {
            using var workspace = new TempWorkspace();

            var normalized = SetupConfigPaths.NormalizeModulePath(
                workspace.Root,
                "Assets/./UI/../Generated/");

            Assert.That(normalized, Is.EqualTo("Assets/Generated"));
        }

        [Test]
        public void NormalizeModulePath_RejectsParentEscape()
        {
            using var workspace = new TempWorkspace();

            var error = Assert.Throws<SetupConfigException>(
                () => SetupConfigPaths.NormalizeModulePath(workspace.Root, "../outside"));

            Assert.That(error!.Message, Does.Contain("escapes Root"));
        }

        [Test]
        public void NormalizeModulePath_RejectsNestedEscape()
        {
            using var workspace = new TempWorkspace();

            Assert.Throws<SetupConfigException>(
                () => SetupConfigPaths.NormalizeModulePath(workspace.Root, "Assets/../../outside"));
        }

        [Test]
        public void NormalizeModulePath_RejectsAbsolutePathOutsideRoot()
        {
            using var workspace = new TempWorkspace();
            var outside = Path.Combine(workspace.Root, "..", "outside-" + Guid.NewGuid().ToString("N"));

            Assert.Throws<SetupConfigException>(
                () => SetupConfigPaths.NormalizeModulePath(workspace.Root, Path.GetFullPath(outside)));
        }

        [Test]
        public void NormalizeModulePath_AcceptsAbsolutePathInsideRoot()
        {
            using var workspace = new TempWorkspace();
            var inside = Path.Combine(workspace.Root, "Assets", "UI");

            var normalized = SetupConfigPaths.NormalizeModulePath(workspace.Root, inside);

            Assert.That(normalized, Is.EqualTo("Assets/UI"));
        }

        [Test]
        public void NormalizeModulePath_RootItself_IsDot()
        {
            using var workspace = new TempWorkspace();

            var normalized = SetupConfigPaths.NormalizeModulePath(workspace.Root, ".");

            Assert.That(normalized, Is.EqualTo("."));
            Assert.That(
                SetupConfigPaths.GetFullModulePath(workspace.Root, "."),
                Is.EqualTo(Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspace.Root))));
        }

        [Test]
        public void IsPathWithinRoot_RejectsSiblingWithSharedPrefix()
        {
            using var workspace = new TempWorkspace();

            Assert.That(SetupConfigPaths.IsPathWithinRoot(workspace.Root, workspace.Root), Is.True);
            Assert.That(
                SetupConfigPaths.IsPathWithinRoot(workspace.Root, workspace.Root + "-sibling"),
                Is.False);
        }

        [Test]
        public void NormalizePaths_WritesNormalizedValuesBack()
        {
            using var workspace = new TempWorkspace();
            var registration = new SetupConfigRegistration("unity", "unity", typeof(UnityLikeTestConfig));
            var config = new UnityLikeTestConfig
            {
                ScriptRoot = "./Assets//UI/",
                ExtraRoots = new[] { "Assets/UI/./Extra" },
            };

            var result = registration.NormalizePaths(workspace.Root, config);

            Assert.That(result.IsValid, Is.True);
            Assert.That(config.ScriptRoot, Is.EqualTo("Assets/UI"));
            Assert.That(config.ExtraRoots, Is.EqualTo(new[] { "Assets/UI/Extra" }));
            Assert.That(
                result.Paths.Select(static path => path.MemberName),
                Is.EqualTo(new[] { "ExtraRoots[0]", "ScriptRoot" }));
        }

        [Test]
        public void NormalizePaths_CollectsErrorsForEscapingValues()
        {
            using var workspace = new TempWorkspace();
            var registration = new SetupConfigRegistration("unity", "unity", typeof(UnityLikeTestConfig));
            var config = new UnityLikeTestConfig
            {
                ScriptRoot = "../outside",
                ExtraRoots = new[] { "Assets/ok", "../bad" },
            };

            var result = registration.NormalizePaths(workspace.Root, config);

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Errors.Count, Is.EqualTo(2));
            Assert.That(result.Errors.Any(message => message.Contains("ScriptRoot")), Is.True);
            Assert.That(result.Errors.Any(message => message.Contains("ExtraRoots[1]")), Is.True);
            Assert.That(config.ScriptRoot, Is.EqualTo("../outside"));
        }

        [Test]
        public void NormalizePaths_ReportsEmptyRequiredValue()
        {
            using var workspace = new TempWorkspace();
            var registration = new SetupConfigRegistration("unity", "unity", typeof(UnityLikeTestConfig));
            var config = new UnityLikeTestConfig { ScriptRoot = "  " };

            var result = registration.NormalizePaths(workspace.Root, config);

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Errors, Has.Some.Contains("ScriptRoot"));
        }

        [Test]
        public void NormalizePaths_AllowsEmptyValueWhenAttributeAllowsIt()
        {
            using var workspace = new TempWorkspace();
            var registration = new SetupConfigRegistration("optional", "optional", typeof(OptionalPathTestConfig));
            var config = new OptionalPathTestConfig { OptionalRoot = string.Empty };

            var result = registration.NormalizePaths(workspace.Root, config);

            Assert.That(result.IsValid, Is.True);
            Assert.That(result.Paths, Is.Empty);
        }

        [Test]
        public void ReadPathValues_ReturnsStoredValuesWithoutNormalizing()
        {
            using var workspace = new TempWorkspace();
            var registration = new SetupConfigRegistration("unity", "unity", typeof(UnityLikeTestConfig));
            var config = new UnityLikeTestConfig { ScriptRoot = "./Assets//UI/" };

            var values = registration.ReadPathValues(config);

            Assert.That(values.Count, Is.EqualTo(1));
            Assert.That(values[0].MemberName, Is.EqualTo("ScriptRoot"));
            Assert.That(values[0].OriginalValue, Is.EqualTo("./Assets//UI/"));
        }
    }
}
