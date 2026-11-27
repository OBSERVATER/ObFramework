using NuGet.Versioning;
using ObFramework.Cli.Commands.Config;
using ObFramework.Cli.Commands.Config.Modules;
using ObFramework.Cli.Unity;

namespace ObFramework.Cli.Commands.Init;

internal sealed class InitWizard(TextReader input, TextWriter output)
{
    internal void Configure(InitCommand.Settings options)
    {
        options.Root ??= Ask("Project root", Directory.GetCurrentDirectory(),
            value => SetupConfigPaths.ValidateAbsoluteRoot(Path.GetFullPath(value.Trim('"'))));
        var root = Path.GetFullPath(options.Root);
        var detected = EngineProject.Resolve("auto", root);
        options.Engine ??= Ask($"Engine (auto detects {detected}, unity, godot, unreal)", "auto", value =>
        {
            EngineProject.Resolve(value, root);
            return value;
        });

        if (EngineProject.Resolve(options.Engine, root) == EngineKind.Unity)
        {
            options.Resources ??= Ask("Unity resources (addressables/custom)", UnityProject.ResourceChoice(root, "addressables"),
                InitCommand.ParseResources);
            if (!options.SkipPackages)
                options.SkipPackages = !Confirm("Install framework NuGet packages?", true);
            if (!options.SkipPackages)
            {
                if (options.PackageVersion == null)
                {
                    var version = Ask("Package version (Enter uses the CLI version)", UnityPackageInstaller.DefaultVersion,
                        value => NuGetVersion.TryParse(value, out _) ? value : throw new ArgumentException("Enter a NuGet version."));
                    if (version != UnityPackageInstaller.DefaultVersion) options.PackageVersion = version;
                }
                if (options.Source == null)
                {
                    var feed = Ask("Additional NuGet feed URL or directory (optional)", "", value => value);
                    if (feed.Length > 0) options.Source = feed;
                }
            }
        }

        var skipPreviouslyRequested = options.SkipDownload;
        if (!options.SkipDownload && !options.Gas)
            options.SkipDownload = !Confirm("Install Luban table tools?", true);
        if (!options.Gas && (!options.SkipDownload || skipPreviouslyRequested))
            options.Gas = Confirm("Create the GAS attribute, effect and ability Excel workbooks?", false);

        if (Confirm("Customize Luban schema and output paths?", false))
        {
            var document = SetupJsonDocument.LoadOrCreate(Path.Combine(root, "setup.json"));
            var config = document.GetModuleConfig<LubanSetupConfig>("luban") ?? new LubanSetupConfig();
            string ReadPath(string label, string current) => Ask(label, current, value =>
                SetupConfigPaths.NormalizeModulePath(root, value.Trim('"')));
            config.ConfPath = ReadPath("Primary Luban configuration", config.ConfPath);
            config.CodeOutputRoot = ReadPath("Generated code directory", config.CodeOutputRoot);
            config.DataOutputRoot = ReadPath("Generated data directory", config.DataOutputRoot);
            options.LubanPaths = config;
        }
    }

    private bool Confirm(string question, bool fallback) =>
        Ask(question + " (y/n)", fallback ? "y" : "n", value => value.ToLowerInvariant() switch
        {
            "y" or "yes" => true,
            "n" or "no" => false,
            _ => throw new ArgumentException("Enter y or n.")
        });

    private T Ask<T>(string question, string fallback, Func<string, T> parse)
    {
        while (true)
        {
            output.Write($"{question} [{fallback}] ");
            var answer = input.ReadLine() ?? throw new OperationCanceledException();
            try { return parse(string.IsNullOrWhiteSpace(answer) ? fallback : answer.Trim()); }
            catch (Exception error) when (error is ArgumentException or SetupConfigException or NotSupportedException)
            {
                output.WriteLine(error.Message);
            }
        }
    }
}

