using System;
using System.Linq;
using NUnit.Framework;
using ObFramework.Cli.Commands.Config;

namespace ObFramework.Test.Cli
{
    [TestFixture]
    internal sealed class ModuleConfigRegistryTest
    {
        [Test]
        public void Register_ComposesExplicitModuleTypes()
        {
            var registry = new ModuleConfigRegistry();

            registry.Register<UnityLikeTestConfig>("unity", "unitySection");
            registry.Register<OtherTestConfig>("other", "otherSection");

            Assert.That(registry.Modules.Count, Is.EqualTo(2));
            Assert.That(registry.TryGet("unity", out var unity), Is.True);
            Assert.That(unity.ConfigType, Is.EqualTo(typeof(UnityLikeTestConfig)));
            Assert.That(unity.SectionName, Is.EqualTo("unitySection"));
            Assert.That(registry.TryGet("otherSection", out var bySection), Is.True);
            Assert.That(bySection.ModuleName, Is.EqualTo("other"));
            Assert.That(registry.TryGet("missing", out _), Is.False);
        }

        [Test]
        public void Lookup_IsCaseInsensitive()
        {
            var registry = new ModuleConfigRegistry();
            registry.Register<UnityLikeTestConfig>("Unity", "unity");

            Assert.That(registry.Get("UNITY").ModuleName, Is.EqualTo("Unity"));
        }

        [Test]
        public void Register_DuplicateModuleName_Throws()
        {
            var registry = new ModuleConfigRegistry();
            registry.Register<UnityLikeTestConfig>("unity", "unity");

            var error = Assert.Throws<InvalidOperationException>(
                () => registry.Register<OtherTestConfig>("unity", "other"));

            Assert.That(error!.Message, Does.Contain("already registered"));
        }

        [Test]
        public void Register_DuplicateSectionName_Throws()
        {
            var registry = new ModuleConfigRegistry();
            registry.Register<UnityLikeTestConfig>("first", "shared");
            registry.Register<OtherTestConfig>("second", "second");

            var error = Assert.Throws<InvalidOperationException>(
                () => registry.Register<OtherTestConfig>("third", "shared"));

            Assert.That(error!.Message, Does.Contain("shared"));
        }

        [Test]
        public void Register_ByModuleDescription()
        {
            var registry = new ModuleConfigRegistry();

            var registration = registry.Register(new OtherModuleDescription());

            Assert.That(registration.ModuleName, Is.EqualTo("other"));
            Assert.That(registration.ConfigType, Is.EqualTo(typeof(OtherTestConfig)));
        }

        [Test]
        public void Register_ByGenericModuleDescription()
        {
            var registry = new ModuleConfigRegistry();

            var registration = registry.Register(new SetupConfigModule<OtherTestConfig>("generic", "genericSection"));

            Assert.That(registration.ModuleName, Is.EqualTo("generic"));
            Assert.That(registration.SectionName, Is.EqualTo("genericSection"));
            Assert.That(registration.ConfigType, Is.EqualTo(typeof(OtherTestConfig)));
            Assert.That(registry.Get("genericSection").ModuleName, Is.EqualTo("generic"));
        }

        [Test]
        public void Register_RejectsTypeWithoutPublicParameterlessConstructor()
        {
            var registry = new ModuleConfigRegistry();

            Assert.Throws<ArgumentException>(
                () => registry.Register(typeof(NoDefaultConstructorConfig), "invalid", "invalid"));
        }

        [Test]
        public void Register_RejectsModuleDescriptionWithInvalidType()
        {
            var registry = new ModuleConfigRegistry();

            Assert.Throws<ArgumentException>(() => registry.Register(new InvalidModuleDescription()));
        }

        [Test]
        public void Register_RejectsInvalidNames()
        {
            var registry = new ModuleConfigRegistry();

            Assert.Throws<ArgumentException>(() => registry.Register<UnityLikeTestConfig>("bad.name", "bad"));
            Assert.Throws<ArgumentException>(() => registry.Register<UnityLikeTestConfig>("bad", "bad section"));
            Assert.Throws<ArgumentException>(() => registry.Register<UnityLikeTestConfig>("", "bad"));
        }

        [Test]
        public void Registration_DiscoversModulePathProperties()
        {
            var registration = new SetupConfigRegistration("unity", "unity", typeof(UnityLikeTestConfig));

            Assert.That(
                registration.PathProperties.Select(static property => property.Name),
                Is.EqualTo(new[] { "ExtraRoots", "ScriptRoot" }));
        }

        [Test]
        public void Registration_RejectsUnsupportedModulePathMemberType()
        {
            Assert.Throws<ArgumentException>(
                () => new SetupConfigRegistration("bad", "bad", typeof(UnsupportedPathTypeConfig)));
        }

        [Test]
        public void Get_UnknownModule_ListsRegisteredModules()
        {
            var registry = new ModuleConfigRegistry();
            registry.Register<UnityLikeTestConfig>("unity", "unity");

            var error = Assert.Throws<SetupConfigException>(() => registry.Get("nope"));

            Assert.That(error!.Message, Does.Contain("nope"));
            Assert.That(error.Message, Does.Contain("unity"));
        }

        [Test]
        public void CreateDefault_ReturnsFreshPocoInstance()
        {
            var registration = new SetupConfigRegistration("unity", "unity", typeof(UnityLikeTestConfig));

            var first = (UnityLikeTestConfig)registration.CreateDefault();
            var second = (UnityLikeTestConfig)registration.CreateDefault();

            Assert.That(first, Is.Not.SameAs(second));
            Assert.That(first.ScriptRoot, Is.EqualTo("Assets/Generated/UI"));
        }
    }
}
