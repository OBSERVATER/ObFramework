using System;

namespace ObFramework.Cli.Config
{
    /// <summary>
    /// A package contributes one module configuration type to the Config CLI by exposing an
    /// explicit implementation of this interface (or by calling
    /// <see cref="ModuleConfigRegistry.Register{TConfig}"/> directly).
    /// <para>
    /// Registration is always explicit: there is deliberately no assembly scanning, no reflection
    /// based discovery of packages and no runtime ConfigPlugin coupling. Adding a module means
    /// adding one registration line to the CLI module catalog.
    /// </para>
    /// </summary>
    public interface ISetupConfigModule
    {
        /// <summary>CLI facing module name, for example <c>unity</c> (<c>ob config unity</c>).</summary>
        string ModuleName { get; }

        /// <summary>JSON property name of the module section under <c>modules</c>, for example <c>unity</c>.</summary>
        string SectionName { get; }

        /// <summary>POCO configuration type with a public parameterless constructor.</summary>
        Type ConfigType { get; }
    }

    /// <summary>
    /// Convenience implementation of <see cref="ISetupConfigModule"/>; registration still happens
    /// explicitly through <see cref="ModuleConfigRegistry.Register(ISetupConfigModule)"/>.
    /// </summary>
    /// <typeparam name="TConfig">POCO configuration type.</typeparam>
    public sealed class SetupConfigModule<TConfig> : ISetupConfigModule
        where TConfig : class, new()
    {
        public SetupConfigModule(string moduleName, string sectionName)
        {
            ModuleName = string.IsNullOrWhiteSpace(moduleName)
                ? throw new ArgumentException("Module name must not be empty.", nameof(moduleName))
                : moduleName.Trim();
            SectionName = string.IsNullOrWhiteSpace(sectionName)
                ? throw new ArgumentException("Section name must not be empty.", nameof(sectionName))
                : sectionName.Trim();
        }

        public string ModuleName { get; }

        public string SectionName { get; }

        public Type ConfigType => typeof(TConfig);
    }
}
