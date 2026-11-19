using ObFramework.Cli.Commands.Config;
using ObFramework.Cli.Commands.Config.Modules;
using ObFramework.Cli.Unity;
using ObFramework.Utility.Pipeline;

namespace ObFramework.Cli.Commands.Init
{
    internal sealed class LubanInstallStep : IPipelineStep<InitContext>
    {
        private readonly Func<LubanReleaseInstaller> _createInstaller;

        public LubanInstallStep() : this(() => new LubanReleaseInstaller()) { }

        internal LubanInstallStep(Func<LubanReleaseInstaller> createInstaller) => _createInstaller = createInstaller;

        public StepResult Execute(ref InitContext context)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            if (context.SkipDownload)
            {
                Console.WriteLine("Init: skipped Luban download.");
                return StepResult.Continue;
            }

            var document = SetupJsonDocument.Load(context.SetupFile);
            var config = document.GetModuleConfig<LubanSetupConfig>("luban")!;
            var validated = ConfigCliModule.CreateDefaultRegistry().Get("luban").NormalizePaths(context.Root, config);
            if (!validated.IsValid)
                throw new SetupConfigException(string.Join(Environment.NewLine, validated.Errors));

            var dll = SetupConfigPaths.GetFullModulePath(context.Root, config.LubanDll);
            var agent = string.IsNullOrWhiteSpace(config.AgentDll)
                ? null : SetupConfigPaths.GetFullModulePath(context.Root, config.AgentDll);
            try
            {
                using var installer = _createInstaller();
                installer.Install(dll, agent, context.CancellationToken);
            }
            catch (Exception ex) when (ex is HttpRequestException ||
                ex is TaskCanceledException && !context.CancellationToken.IsCancellationRequested)
            {
                if (context.Engine == EngineKind.Unity)
                    UnityIntegrationInstaller.SaveSelection(context.Root, context.Resources, null, true);
                Console.Error.WriteLine($"Init: warning: Luban download failed ({ex.Message}); continuing without Luban. Retry ob init later.");
            }
            return StepResult.Continue;
        }
    }
}
