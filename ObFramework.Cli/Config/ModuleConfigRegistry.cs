using System;
using System.Collections.Generic;
using System.Linq;

namespace ObFramework.Cli.Config
{
    /// <summary>
    /// Explicit composition root for per-module setup.json configuration types.
    /// <para>
    /// Packages contribute their POCO configuration type through one explicit registration call;
    /// the registry never scans assemblies. Duplicate module names or duplicate section names are
    /// rejected so configuration never depends on registration order.
    /// </para>
    /// </summary>
    public sealed class ModuleConfigRegistry
    {
        private readonly List<SetupConfigRegistration> _modules = new();
        private readonly Dictionary<string, SetupConfigRegistration> _byModuleName =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, SetupConfigRegistration> _bySectionName =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>All registrations in registration order.</summary>
        public IReadOnlyList<SetupConfigRegistration> Modules => _modules;

        /// <summary>Registers a module configuration POCO explicitly.</summary>
        /// <typeparam name="TConfig">POCO type with a public parameterless constructor.</typeparam>
        /// <param name="moduleName">CLI module name (<c>ob config &lt;module&gt;</c>).</param>
        /// <param name="sectionName">JSON section name below <c>modules</c>.</param>
        public SetupConfigRegistration Register<TConfig>(string moduleName, string sectionName)
            where TConfig : class, new()
        {
            var registration = new SetupConfigRegistration(moduleName, sectionName, typeof(TConfig));
            Add(registration);
            return registration;
        }

        /// <summary>Runtime-typed variant of <see cref="Register{TConfig}"/>.</summary>
        public SetupConfigRegistration Register(Type configType, string moduleName, string sectionName)
        {
            var registration = new SetupConfigRegistration(moduleName, sectionName, configType);
            Add(registration);
            return registration;
        }

        /// <summary>Registers a package provided <see cref="ISetupConfigModule"/>.</summary>
        public SetupConfigRegistration Register(ISetupConfigModule module)
        {
            if (module == null)
                throw new ArgumentNullException(nameof(module));

            return Register(module.ConfigType, module.ModuleName, module.SectionName);
        }

        /// <summary>
        /// Looks a module up by CLI module name first, then by section name (so
        /// <c>ob config unity</c> and <c>--set</c> diagnostics accept either spelling).
        /// </summary>
        public bool TryGet(string moduleOrSectionName, out SetupConfigRegistration registration)
        {
            registration = null!;
            if (string.IsNullOrWhiteSpace(moduleOrSectionName))
                return false;

            var key = moduleOrSectionName.Trim();
            if (_byModuleName.TryGetValue(key, out var byModule))
            {
                registration = byModule;
                return true;
            }

            if (_bySectionName.TryGetValue(key, out var bySection))
            {
                registration = bySection;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Resolves a module registration or throws a <see cref="SetupConfigException"/> listing the
        /// registered module names.
        /// </summary>
        public SetupConfigRegistration Get(string moduleOrSectionName)
        {
            if (TryGet(moduleOrSectionName, out var registration))
                return registration;

            var known = _modules.Count == 0
                ? "none"
                : string.Join(", ", _modules.Select(static module => module.ModuleName).OrderBy(static name => name, StringComparer.OrdinalIgnoreCase));

            throw new SetupConfigException(
                $"Unknown config module '{moduleOrSectionName}'. Registered modules: {known}.");
        }

        /// <summary>Registered CLI module names in registration order.</summary>
        public IReadOnlyList<string> GetModuleNames()
            => _modules.Select(static module => module.ModuleName).ToArray();

        private void Add(SetupConfigRegistration registration)
        {
            if (_byModuleName.TryGetValue(registration.ModuleName, out var existingModule))
                throw new InvalidOperationException(
                    $"Config module '{registration.ModuleName}' is already registered " +
                    $"(module '{existingModule.ModuleName}', section '{existingModule.SectionName}').");
            if (_bySectionName.TryGetValue(registration.SectionName, out var existingSection))
                throw new InvalidOperationException(
                    $"setup.json section '{registration.SectionName}' is already registered by module " +
                    $"'{existingSection.ModuleName}'.");

            _byModuleName.Add(registration.ModuleName, registration);
            _bySectionName.Add(registration.SectionName, registration);
            _modules.Add(registration);
        }
    }
}
