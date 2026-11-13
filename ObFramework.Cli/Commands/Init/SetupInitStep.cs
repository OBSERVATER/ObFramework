using ObFramework.Cli.Commands.Config;
using ObFramework.Cli.Commands.Config.Modules;
using ObFramework.Integrated.Diagnostics;
using ObFramework.Utility.Pipeline;

namespace ObFramework.Cli.Commands.Init
{
    internal sealed class SetupInitStep : IPipelineStep<InitContext>
    {
        public StepResult Execute(ref InitContext context)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(context.Root);
            var document = SetupJsonDocument.LoadOrCreate(context.SetupFile);
            var existing = document.GetRootValue();
            if (existing != null &&
                !SetupConfigPaths.ValidateAbsoluteRoot(existing).Equals(context.Root,
                    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                throw new SetupConfigException($"Existing setup.json root is '{existing}', not '{context.Root}'.");

            var changed = !document.Exists;
            if (existing == null)
            {
                document.SetRoot(context.Root);
                changed = true;
            }
            if (context.Engine == EngineKind.Unity && !document.HasModuleSection("unity"))
            {
                document.SetModuleConfig("unity", new UnitySetupConfig());
                changed = true;
            }
            if (!document.HasModuleSection("luban"))
            {
                document.SetModuleConfig("luban", new LubanSetupConfig());
                changed = true;
            }
            if (!document.HasModuleSection("diagnostics"))
            {
                document.SetModuleConfig("diagnostics", new DiagnosticsOption());
                changed = true;
            }
            if (changed)
                document.Save();
            Console.WriteLine("Init: setup.json ready.");
            return StepResult.Continue;
        }
    }
}
