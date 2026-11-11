using System.ComponentModel;
using ObFramework.Cli.Config;
using ObFramework.Cli.Deployment;
using Spectre.Console;
using Spectre.Console.Cli;

namespace ObFramework.Cli.Commands
{
    /// <summary>
    /// <c>ob unity deploy|remove &lt;project&gt;</c> — installs, updates or removes the Unity Editor
    /// tooling carried by the NuGet package inside a Unity project.
    /// <para>
    /// Registered through <see cref="UnityDeployCliModule"/> using the existing
    /// <c>CliModule.InjectCommand&lt;T&gt;</c> extension point. The file deployment logic is pure
    /// .NET (<see cref="UnityEditorDeployer"/>); Unity itself is never launched.
    /// </para>
    /// </summary>
    internal sealed class UnityCommand : Command<UnityCommand.Settings>
    {
        internal sealed class Settings : CommandSettings
        {
            [CommandArgument(0, "<ACTION>")]
            [Description("deploy (install or update) or remove.")]
            public string Action { get; set; } = "deploy";

            [CommandArgument(1, "[PROJECT]")]
            [Description("Unity project directory; defaults to the current directory.")]
            public string Project { get; set; } = ".";

            [CommandOption("--dry-run")]
            [Description("Report the planned changes without writing or deleting any file.")]
            public bool DryRun { get; set; }

            [CommandOption("--source <DIR>")]
            [Description("Deploy from a local EditorDeploy directory instead of the payload embedded in the installed CLI.")]
            public string? Source { get; set; }

            [CommandOption("--manifest <FILE>")]
            [Description("Explicit deployment plan JSON; defaults to the payload's deploy-manifest.json or the embedded plan.")]
            public string? Manifest { get; set; }
        }

        public override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                var action = (settings.Action ?? string.Empty).Trim().ToLowerInvariant();
                if (action != "deploy" && action != "remove")
                    throw new DeploymentException($"Unknown unity action '{settings.Action}'. Expected 'deploy' or 'remove'.");

                var source = CreateSource(settings);
                var manifest = LoadManifest(source, settings);
                var deployer = new UnityEditorDeployer(source, manifest);
                var options = new DeploymentOptions { DryRun = settings.DryRun };

                var result = action == "deploy"
                    ? deployer.Deploy(settings.Project, options)
                    : deployer.Remove(settings.Project, options);

                PrintResult(action, settings.Project, result);
                return 0;
            }
            catch (OperationCanceledException)
            {
                AnsiConsole.MarkupLine("[red]Unity:[/] Operation cancelled.");
                return 1;
            }
            catch (Exception ex) when (
                ex is DeploymentException ||
                ex is IOException ||
                ex is UnauthorizedAccessException ||
                ex is ArgumentException ||
                ex is InvalidOperationException ||
                ex is NotSupportedException)
            {
                AnsiConsole.MarkupLine($"[red]Unity:[/] {Markup.Escape(ex.Message)}");
                return 1;
            }
        }

        private static IDeployableFileSource CreateSource(Settings settings)
        {
            if (string.IsNullOrWhiteSpace(settings.Source))
                return EmbeddedDeployableFileSource.CreateDefault();

            var sourceDirectory = SetupJsonDiscovery.ResolvePath(settings.Source!, Directory.GetCurrentDirectory());
            return new DirectoryDeployableFileSource(sourceDirectory);
        }

        private static DeploymentManifest LoadManifest(IDeployableFileSource source, Settings settings)
        {
            if (!string.IsNullOrWhiteSpace(settings.Manifest))
            {
                var manifestPath = SetupJsonDiscovery.ResolvePath(settings.Manifest!, Directory.GetCurrentDirectory());
                if (!File.Exists(manifestPath))
                    throw new DeploymentException($"Deployment manifest not found: {manifestPath}");

                return DeploymentManifest.Load(File.ReadAllText(manifestPath));
            }

            // A local --source payload may carry its own plan; otherwise the plan compiled into the
            // CLI package is authoritative.
            return source.TryRead(DeploymentManifest.DefaultSourceFileName, out _)
                ? DeploymentManifest.LoadFromSource(source)
                : DeploymentManifest.LoadEmbeddedPlan();
        }

        private static void PrintResult(string action, string project, DeploymentResult result)
        {
            AnsiConsole.Write(new Rule($"[cyan]ob unity {Markup.Escape(action)}[/]"));
            AnsiConsole.MarkupLine($"[grey]Project:[/] {Markup.Escape(Path.GetFullPath(project))}");

            if (result.Changes.Count == 0)
            {
                AnsiConsole.MarkupLine("[yellow]No file changes.[/]");
            }
            else
            {
                var table = new Table().Border(TableBorder.Rounded);
                table.AddColumn("File");
                table.AddColumn("Change");
                table.AddColumn("Detail");

                foreach (var change in result.Changes)
                {
                    table.AddRow(
                        new Text(change.Path),
                        new Text(change.Change.ToString()),
                        new Text(change.Detail ?? string.Empty));
                }

                AnsiConsole.Write(table);
            }

            foreach (var warning in result.Warnings)
                AnsiConsole.MarkupLine($"[yellow]warning:[/] {Markup.Escape(warning)}");

            var prefix = result.DryRun ? "[yellow]Dry run:[/] " : string.Empty;
            AnsiConsole.MarkupLine($"{prefix}{Markup.Escape(result.Summary)}");
        }
    }
}
