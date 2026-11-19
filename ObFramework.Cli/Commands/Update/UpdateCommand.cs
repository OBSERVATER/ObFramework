using System.ComponentModel;
using NuGet.Versioning;
using ObFramework.Cli.Core;
using ObFramework.Cli.Unity;
using Spectre.Console.Cli;

namespace ObFramework.Cli.Commands.Update;

internal sealed class UpdateCommand : AsyncCommand<UpdateCommand.Settings>
{
    internal sealed class Settings : CommandSettings
    {
        [CommandOption("--check")]
        [Description("Show available versions without changing the installation.")]
        public bool Check { get; set; }

        [CommandOption("--version <VERSION>")]
        [Description("Select an exact release. Required for previews and upgrades across compatibility lines.")]
        public string? Version { get; set; }

        [CommandOption("--root <DIR>")]
        public string? Root { get; set; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken ct)
    {
        try
        {
            var root = Path.GetFullPath(settings.Root ?? Directory.GetCurrentDirectory());
            UnityProject.Validate(root);
            if (!NuGetVersion.TryParse(UnityProject.FrameworkVersion(root), out var project))
                throw new InvalidOperationException("Initialize this Unity project with ob init before updating it.");

            var cli = UpdateCatalog.CliVersion;
            var releases = await UpdateCatalog.FetchAsync(TimeSpan.FromSeconds(15), ct);
            var available = UpdateCatalog.Available(releases, project, cli);
            var compatible = available.FirstOrDefault(version => UpdateCatalog.IsCompatible(project, version));
            if (settings.Check)
            {
                Console.WriteLine($"CLI: {cli}. Unity project: {project}.");
                if (compatible != null) Console.WriteLine($"Compatible update: {compatible}.");
                var stable = available.FirstOrDefault(version => !version.IsPrerelease);
                if (stable != null && (compatible == null || stable > compatible))
                    PrintExplicitUpdate(stable);
                var preview = available.FirstOrDefault(version => version.IsPrerelease);
                if (preview != null) PrintExplicitUpdate(preview);
                if (available.Length == 0) Console.WriteLine("No newer release is available.");
                return 0;
            }

            var target = compatible;
            if (settings.Version != null)
            {
                if (!NuGetVersion.TryParse(settings.Version, out target) || target.HasMetadata ||
                    !releases.Contains(target))
                    throw new ArgumentException("Select an exact listed version from NuGet.org.");
                if (target < project || target < cli)
                    throw new ArgumentException("Select a version at least as recent as both the CLI and current project.");
            }
            if (target == null)
            {
                Console.WriteLine("No compatible update is available. Run 'ob update --check' for all available versions.");
                return 0;
            }

            UnityProject.RequireClosed(root);
            var editor = UnityEditorLocator.Resolve(root);
            if (UpdateWorker.GlobalVersion(root, ct) != cli)
                throw new InvalidOperationException("Run ob update from the installed global ObFramework.Cli tool.");

            var log = UpdateWorker.Start(root, target.ToNormalizedString(), editor);
            Console.WriteLine($"Update to {target} scheduled. Result and Unity compilation status: {log}");
            if (context.Data is CliSessionState session) session.RequestExit();
            return 0;
        }
        catch (Exception ex) when (UpdateCatalog.IsUpdateError(ex))
        {
            await Console.Error.WriteLineAsync(ex is OperationCanceledException
                ? "Update: cancelled or the release lookup timed out."
                : $"Update: {ex.Message}");
            return 1;
        }
    }

    private static void PrintExplicitUpdate(NuGetVersion version)
    {
        Console.WriteLine($"Explicit upgrade: ob update --version {version.ToNormalizedString()}");
        Console.WriteLine($"Release notes: https://github.com/OBSERVATER/ObFramework/releases/tag/v{version.ToNormalizedString()}");
    }
}
