using ObFramework.Cli.Commands.Config.Modules;
using ObFramework.Cli.Core;
using ObFramework.Integrated.Diagnostics;

namespace ObFramework.Cli.Commands.Config
{
    /// <summary>
    /// Registers the setup.json Config CLI capability through the existing CLI extension point.
    /// <para>
    /// This is the only place that knows the set of modules the CLI supports. Each module package
    /// contributes exactly one explicit registration line in
    /// <see cref="RegisterBuiltInModules"/>; no assembly is scanned, <c>CliModule</c> is not
    /// modified and the runtime <c>ConfigPlugin</c> is never involved.
    /// </para>
    /// </summary>
    public static class ConfigCliModule
    {
        /// <summary>CLI command name registered by <see cref="Register"/>.</summary>
        public const string CommandName = "config";

        /// <summary>
        /// Composes the registry the command uses. Kept public so hosts/tests can inspect the
        /// explicit module catalog without running the interactive CLI.
        /// </summary>
        public static ModuleConfigRegistry CreateDefaultRegistry()
        {
            var registry = new ModuleConfigRegistry();
            RegisterBuiltInModules(registry);
            return registry;
        }

        /// <summary>Explicit per-package module registration (this is the whole "module catalog").</summary>
        public static void RegisterBuiltInModules(ModuleConfigRegistry registry)
        {
            if (registry == null)
                throw new ArgumentNullException(nameof(registry));

            registry.Register<UnitySetupConfig>("unity", "unity");
            registry.Register<LubanSetupConfig>("luban", "luban");
            registry.Register<DiagnosticsOption>("diagnostics", "diagnostics");
        }

        /// <summary>Registers <c>ob config</c> through the existing <c>CliModule.InjectCommand&lt;T&gt;</c> extension point.</summary>
        public static void Register(CliModule cli)
        {
            if (cli == null)
                throw new ArgumentNullException(nameof(cli));

            cli.InjectCommand<ConfigCommand>(CommandName);
        }
    }
}
