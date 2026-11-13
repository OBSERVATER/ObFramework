using ObFramework.Cli.Deployment;
using ObFramework.Utility.Pipeline;

namespace ObFramework.Cli.Commands.Init
{
    internal sealed class UnityEditorInitStep : IPipelineStep<InitContext>
    {
        public StepResult Execute(ref InitContext context)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            var source = EmbeddedDeployableFileSource.CreateDefault();
            var plan = DeploymentManifest.LoadEmbeddedPlan();
            var result = new UnityEditorDeployer(source, plan).Deploy(context.Root);
            Console.WriteLine($"Init: Unity Editor {result.Summary}");
            foreach (var warning in result.Warnings)
                Console.WriteLine($"Init: warning: {warning}");
            return StepResult.Continue;
        }
    }
}
