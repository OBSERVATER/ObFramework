using System.ComponentModel;
using ObFramework.Cli.Commands.Config;
using ObFramework.Cli.Deployment;
using Spectre.Console.Cli;

namespace ObFramework.Cli.Commands.Init
{
    internal sealed class InitCommand : Command<InitCommand.Settings>
    {
        internal sealed class Settings : CommandSettings
        {
            [CommandOption("--root <DIR>")]
            [Description("Project root; defaults to current directory.")]
            public string? Root { get; set; }

            [CommandOption("--engine <ENGINE>")]
            [Description("unity, godot, unreal or auto (default).")]
            public string Engine { get; set; } = "auto";

            [CommandOption("--skip-download")]
            [Description("Skip Luban installation; still update setup.json and engine tooling.")]
            public bool SkipDownload { get; set; }
        }

        public override int Execute(CommandContext commandContext, Settings settings, CancellationToken cancellationToken)
        {
            try
            {
                var root = SetupConfigPaths.ValidateAbsoluteRoot(Path.GetFullPath(settings.Root ?? Directory.GetCurrentDirectory()));
                var engine = EngineProject.Resolve(settings.Engine, root);
                var init = new InitContext(root, engine, settings.SkipDownload, cancellationToken);
                InitCliModule.CreatePipeline(engine).Execute(ref init);
                Console.WriteLine($"Init: complete ({root}; engine: {engine}).");
                return 0;
            }
            catch (OperationCanceledException)
            {
                Console.Error.WriteLine("Init: cancelled.");
                return 1;
            }
            catch (Exception ex) when (ex is SetupConfigException or DeploymentException or IOException
                or UnauthorizedAccessException or HttpRequestException or InvalidOperationException
                or ArgumentException or System.Text.Json.JsonException)
            {
                Console.Error.WriteLine($"Init: {ex.Message}");
                return 1;
            }
        }
    }
}
