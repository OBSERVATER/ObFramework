using ObFramework.Cli.Unity;
using ObFramework.Utility.Pipeline;

namespace ObFramework.Cli.Commands.Init;

internal sealed class UnityPackagesInitStep : IPipelineStep<InitContext>
{
    public StepResult Execute(ref InitContext context)
    {
        if (!context.SkipPackages)
        {
            UnityProject.ConfigureUpm(context.Root, context.Resources);
            UnityPackageInstaller.Install(context.Root, context.PackageVersion, context.Source, context.CancellationToken);
        }
        else if (context.Resources == "addressables")
        {
            UnityProject.ConfigureUpm(context.Root, context.Resources);
        }
        UnityIntegrationInstaller.Install(context.Root, context.Resources);
        UnityIntegrationInstaller.InstallGuide(context.Root);
        UnityIntegrationInstaller.SaveSelection(context.Root, context.Resources, context.SkipPackages ? null : context.PackageVersion, context.SkipDownload);
        return StepResult.Continue;
    }
}
