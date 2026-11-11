using System;
using System.Collections.Generic;
using ObFramework.Cli.Config;

namespace ObFramework.Test.Cli
{
    /// <summary>Representative module POCO configuration used by the Config CLI tests.</summary>
    internal sealed class UnityLikeTestConfig
    {
        [SetupPath]
        public string ScriptRoot { get; set; } = "Assets/Generated/UI";

        [SetupPath]
        public string[] ExtraRoots { get; set; } = Array.Empty<string>();

        public string KeyFormat { get; set; } = "UI/{view}";

        public bool Enabled { get; set; } = true;

        public int Retries { get; set; } = 2;

        public TestSeverity Severity { get; set; } = TestSeverity.Info;

        public Dictionary<string, string>? Overrides { get; set; }
    }

    internal enum TestSeverity
    {
        Info,
        Warning,
        Error,
    }

    /// <summary>Second module POCO so multi-module composition can be tested.</summary>
    internal sealed class OtherTestConfig
    {
        [SetupPath]
        public string SyncRoot { get; set; } = "Assets/Sync";
    }

    /// <summary>POCO with an optional module path member.</summary>
    internal sealed class OptionalPathTestConfig
    {
        [SetupPath(AllowEmpty = true)]
        public string OptionalRoot { get; set; } = string.Empty;
    }

    /// <summary>POCO exposing a getter-only property (must be rejected by --set).</summary>
    internal sealed class ReadOnlyMemberTestConfig
    {
        public string Name => "fixed";
    }

    /// <summary>POCO without a public parameterless constructor (invalid registration).</summary>
    internal sealed class NoDefaultConstructorConfig
    {
        public NoDefaultConstructorConfig(string value)
        {
            Value = value;
        }

        public string Value { get; }
    }

    /// <summary>Invalid POCO: [SetupPath] on an unsupported member type.</summary>
    internal sealed class UnsupportedPathTypeConfig
    {
        [SetupPath]
        public int NotAPath { get; set; }
    }

    /// <summary>Package-style module description implementation.</summary>
    internal sealed class OtherModuleDescription : ISetupConfigModule
    {
        public string ModuleName => "other";

        public string SectionName => "otherSection";

        public Type ConfigType => typeof(OtherTestConfig);
    }

    /// <summary>Package-style module description with a POCO that has no default constructor.</summary>
    internal sealed class InvalidModuleDescription : ISetupConfigModule
    {
        public string ModuleName => "invalid";

        public string SectionName => "invalidSection";

        public Type ConfigType => typeof(NoDefaultConstructorConfig);
    }
}
