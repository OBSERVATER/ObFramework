using ObFramework.Cli.Commands.Config;
using ObFramework.Cli.Commands.Config.Modules;
using ObFramework.Utility.Pipeline;

namespace ObFramework.Cli.Commands.Init
{
    internal sealed class LubanInstallStep : IPipelineStep<InitContext>
    {
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
            using var installer = new LubanReleaseInstaller();
            installer.Install(dll, agent, context.CancellationToken);
            return StepResult.Continue;
        }
    }
}
