using System;
using System.Linq;
using NUnit.Framework;
using ObFramework.Cli.Config;

namespace ObFramework.Test.Cli
{
    [TestFixture]
    internal sealed class SetupConfigValuesTest
    {
        [Test]
        public void TrySet_StringByName()
        {
            var config = new UnityLikeTestConfig();

            Assert.That(SetupConfigValues.TrySet(config, "scriptRoot", "Assets/Generated/New", out var error), Is.True);
            Assert.That(error, Is.Null);
            Assert.That(config.ScriptRoot, Is.EqualTo("Assets/Generated/New"));
        }

        [Test]
        public void TrySet_AcceptsClrPropertyNameCaseInsensitively()
        {
            var config = new UnityLikeTestConfig();

            Assert.That(SetupConfigValues.TrySet(config, "ScriptRoot", "Assets/A", out _), Is.True);

            Assert.That(config.ScriptRoot, Is.EqualTo("Assets/A"));
        }

        [Test]
        public void TrySet_BoolAndIntAndEnum()
        {
            var config = new UnityLikeTestConfig();

            Assert.That(SetupConfigValues.TrySet(config, "enabled", "false", out _), Is.True);
            Assert.That(SetupConfigValues.TrySet(config, "retries", "7", out _), Is.True);
            Assert.That(SetupConfigValues.TrySet(config, "severity", "warning", out _), Is.True);

            Assert.That(config.Enabled, Is.False);
            Assert.That(config.Retries, Is.EqualTo(7));
            Assert.That(config.Severity, Is.EqualTo(TestSeverity.Warning));
        }

        [Test]
        public void TrySet_StringArrayFromCommaSeparatedValue()
        {
            var config = new UnityLikeTestConfig();

            Assert.That(
                SetupConfigValues.TrySet(config, "extraRoots", "Assets/A, Assets/B", out _),
                Is.True);

            Assert.That(config.ExtraRoots, Is.EqualTo(new[] { "Assets/A", "Assets/B" }));
        }

        [Test]
        public void TrySet_UnknownOption_ReportsAvailableOptions()
        {
            var config = new UnityLikeTestConfig();

            Assert.That(SetupConfigValues.TrySet(config, "nope", "x", out var error), Is.False);

            Assert.That(error, Does.Contain("Unknown option"));
            Assert.That(error, Does.Contain("scriptRoot"));
        }

        [Test]
        public void TrySet_InvalidNumber_ReportsConversionError()
        {
            var config = new UnityLikeTestConfig();

            Assert.That(SetupConfigValues.TrySet(config, "retries", "abc", out var error), Is.False);

            Assert.That(error, Does.Contain("Retries"));
            Assert.That(error, Does.Contain("not a valid"));
        }

        [Test]
        public void TrySet_DictionaryOption_ReportsUnsupportedType()
        {
            var config = new UnityLikeTestConfig();

            Assert.That(SetupConfigValues.TrySet(config, "overrides", "Shop=UI/Shop", out var error), Is.False);

            Assert.That(error, Does.Contain("edit setup.json directly"));
        }

        [Test]
        public void TrySet_ReadOnlyMember_ReportsError()
        {
            var config = new ReadOnlyMemberTestConfig();

            Assert.That(SetupConfigValues.TrySet(config, "name", "x", out var error), Is.False);

            Assert.That(error, Does.Contain("read-only"));
        }

        [Test]
        public void TryUnset_RestoresDefaultValue()
        {
            var config = new UnityLikeTestConfig { ScriptRoot = "Custom/Path" };

            Assert.That(SetupConfigValues.TryUnset(config, "scriptRoot", out _), Is.True);

            Assert.That(config.ScriptRoot, Is.EqualTo("Assets/Generated/UI"));
        }

        [Test]
        public void Describe_ReportsJsonNamesAndValues()
        {
            var config = new UnityLikeTestConfig();

            var described = SetupConfigValues.Describe(config);

            Assert.That(described.Select(static item => item.JsonName), Has.Member("scriptRoot"));
            Assert.That(described.Select(static item => item.JsonName), Has.Member("keyFormat"));
            Assert.That(
                described.Single(static item => item.JsonName == "scriptRoot").Value,
                Is.EqualTo("Assets/Generated/UI"));
            Assert.That(
                described.Single(static item => item.JsonName == "scriptRoot").IsModulePath,
                Is.True);
        }
    }
}
