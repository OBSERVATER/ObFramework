using ObFramework.Cli.Core;
using ObFramework.Utility.Pipeline;

namespace ObFramework.Cli.Commands.Init
{
    internal static class InitCliModule
    {
        public static void Register(CliModule cli) => cli.InjectCommand<InitCommand>("init");

        public static Pipeline<InitContext> CreatePipeline(EngineKind engine)
        {
            var builder = new PipelineBuilder<InitContext>().Add(new SetupInitStep());
            if (engine == EngineKind.Unity)
                builder.Add(new UnityEditorInitStep());
            builder.Add(new LubanInstallStep());
            return builder.Build();
        }
    }
}
