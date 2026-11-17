using System.ComponentModel;
using ObFramework.Cli.Unity;
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

            [CommandOption("--resources <BACKEND>")]
            [Description("custom (default) or addressables. Existing selection is preserved.")]
            public string? Resources { get; set; }

            [CommandOption("--package-version <VERSION>")]
            public string? PackageVersion { get; set; }

            [CommandOption("--source <FEED>")]
            public string? Source { get; set; }

            [CommandOption("--skip-packages")]
            [Description("Deploy tooling without restoring framework NuGet packages.")]
            public bool SkipPackages { get; set; }
        }

        public override int Execute(CommandContext commandContext, Settings settings, CancellationToken cancellationToken)
        {
            try
            {
                var root = SetupConfigPaths.ValidateAbsoluteRoot(Path.GetFullPath(settings.Root ?? Directory.GetCurrentDirectory()));
                var engine = EngineProject.Resolve(settings.Engine, root);
                if (engine != EngineKind.Unity && settings.Resources != null)
                    throw new ArgumentException("--resources is only supported for Unity.");
                if (engine == EngineKind.Unity)
                {
                    UnityProject.Validate(root);
                    UnityProject.RequireClosed(root);
                    if (!settings.SkipPackages) UnityProject.ValidateSdk(root, cancellationToken);
                }
                var resources = engine == EngineKind.Unity ? UnityProject.ResourceChoice(root, settings.Resources) : "custom";
                var init = new InitContext(root, engine, settings.SkipDownload, cancellationToken)
                {
                    Resources = resources,
                    PackageVersion = settings.PackageVersion ?? UnityPackageInstaller.DefaultVersion,
                    Source = settings.Source == null ? null : (Uri.TryCreate(settings.Source, UriKind.Absolute, out var uri) ? (uri.IsFile ? uri.LocalPath : settings.Source) : Path.GetFullPath(settings.Source)),
                    SkipPackages = settings.SkipPackages
                };
                InitCliModule.CreatePipeline(engine).Execute(ref init);
                if (engine == EngineKind.Unity)
                {
                    var checks = UnityDoctor.Check(root);
                    if (settings.SkipDownload) checks.RemoveAll(x => x.Id == "OBD070");
                    if (settings.SkipPackages) checks.RemoveAll(x => x.Id is "OBD020" or "OBD030");
                    foreach (var check in checks) Console.WriteLine($"[{check.Status}] {check.Id}: {check.Message}");
                    Console.WriteLine("Init: Unity compilation/runtime not verified. Run ob doctor --unity --unity-editor <FILE>, then test your bootstrap scene.");
                    if (checks.Any(x => x.Status == "fail")) return 1;
                }
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
                or ArgumentException or System.Text.Json.JsonException or System.Xml.XmlException or System.ComponentModel.Win32Exception)
            {
                Console.Error.WriteLine($"Init: {ex.Message}");
                return 1;
            }
        }
    }
}
