using System.ComponentModel;
using System.Text.Json;
using Spectre.Console;
using Spectre.Console.Cli;

namespace ObFramework.Cli.Commands.Config;

internal sealed class ConfigCommand : Command<ConfigCommand.Settings>
{
    internal sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "[MODULE]")]
        [Description("Config module to edit, for example 'unity'. Omit to list registered modules.")]
        public string? Module { get; set; }

        [CommandOption("--root <DIR>")]
        [Description("Absolute development root written to setup.json and used to validate module paths.")]
        public string? Root { get; set; }

        [CommandOption("--file <FILE>")]
        [Description("Explicit setup.json path. Defaults to the nearest setup.json found upward from the working directory.")]
        public string? File { get; set; }

        [CommandOption("--set <KEY=VALUE>")]
        [Description("Set a module option (repeatable), for example --set scriptOutputRoot=Assets/Generated/UI.")]
        public string[] Set { get; set; } = [];

        [CommandOption("--unset <KEY>")]
        [Description("Reset a module option to its default value (repeatable).")]
        public string[] Unset { get; set; } = [];

        [CommandOption("--create")]
        [Description("Create setup.json at the resolved path when it does not exist. Requires --root.")]
        public bool Create { get; set; }

        [CommandOption("--validate")]
        [Description("Validate the absolute root and module paths without writing the file.")]
        public bool ValidateOnly { get; set; }

        [CommandOption("--list")]
        [Description("List registered modules and the discovered setup.json file.")]
        public bool List { get; set; }
    }

    public override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ExecuteCore(settings);
        }
        catch (OperationCanceledException)
        {
            AnsiConsole.MarkupLine("[red]Config:[/] Operation cancelled.");
            return 1;
        }
        catch (Exception ex) when (
            ex is SetupConfigException ||
            ex is IOException ||
            ex is UnauthorizedAccessException ||
            ex is ArgumentException ||
            ex is JsonException ||
            ex is InvalidOperationException ||
            ex is FormatException ||
            ex is OverflowException)
        {
            AnsiConsole.MarkupLine($"[red]Config:[/] {Markup.Escape(ex.Message)}");
            return 1;
        }
    }

    private static int ExecuteCore(Settings settings)
    {
        var workingDirectory = Directory.GetCurrentDirectory();
        var registry = ConfigCliModule.CreateDefaultRegistry();

        var filePath = string.IsNullOrWhiteSpace(settings.File)
            ? SetupJsonDiscovery.FindSetupFile(workingDirectory)
            : SetupJsonDiscovery.ResolvePath(settings.File!, workingDirectory);

        var fileExists = filePath != null && File.Exists(filePath);
        var overviewRequested = string.IsNullOrWhiteSpace(settings.Module) || settings.List;

        if (!fileExists)
        {
            if (!settings.Create)
            {
                // Listing registered modules stays useful before setup.json exists.
                if (overviewRequested)
                {
                    ShowOverview(
                        registry,
                        SetupJsonDocument.Create(
                            filePath ?? Path.Combine(workingDirectory, SetupJsonDocument.DefaultFileName)));
                    return 0;
                }

                throw new SetupConfigException(
                    filePath != null && !string.IsNullOrWhiteSpace(settings.File)
                        ? $"Configuration file not found: {filePath}"
                        : $"No setup.json found from '{workingDirectory}' upward. " +
                          "Pass --file <FILE> or --create --root <DIR> to create one.");
            }

            if (string.IsNullOrWhiteSpace(settings.Root))
                throw new SetupConfigException("Creating setup.json requires an absolute --root <DIR>.");

            filePath ??= Path.Combine(workingDirectory, SetupJsonDocument.DefaultFileName);
        }

        var document = fileExists
            ? SetupJsonDocument.Load(filePath!)
            : SetupJsonDocument.Create(filePath!);

        if (overviewRequested)
        {
            ShowOverview(registry, document);
            return 0;
        }

        var registration = registry.Get(settings.Module!.Trim());

        string? root;
        string? rootError = null;
        if (!string.IsNullOrWhiteSpace(settings.Root))
            root = SetupConfigPaths.ValidateAbsoluteRoot(settings.Root);
        else
            document.TryGetAbsoluteRoot(out root, out rootError);

        if (settings.ValidateOnly)
            return ValidateModule(registration, document, root, rootError);

        var config = document.GetOrCreateModuleConfig(registration);

        if (settings.Unset.Length == 0 && settings.Set.Length == 0 && string.IsNullOrWhiteSpace(settings.Root) && !settings.Create)
        {
            ShowModule(registration, document, config, root);
            return 0;
        }

        foreach (var key in settings.Unset)
        {
            if (!SetupConfigValues.TryUnset(config, key, out var error))
                throw new SetupConfigException($"--unset {key}: {error}");
        }

        foreach (var assignment in settings.Set)
        {
            if (!TryParseAssignment(assignment, out var key, out var value, out var parseError))
                throw new SetupConfigException($"--set {assignment}: {parseError}");

            if (!SetupConfigValues.TrySet(config, key, value, out var error))
                throw new SetupConfigException($"--set {key}: {error}");
        }

        if (registration.PathProperties.Count > 0 && root == null)
        {
            throw new SetupConfigException(
                rootError ?? "Root is not configured; pass --root <DIR> to validate module paths.");
        }

        if (root != null)
        {
            var normalization = registration.NormalizePaths(root, config);
            if (!normalization.IsValid)
            {
                AnsiConsole.MarkupLine("[red]Module paths must stay inside Root:[/]");
                PrintErrors(normalization.Errors);
                return 1;
            }
        }

        if (root != null)
            document.SetRoot(root);
        document.SetModuleConfig(registration, config);
        document.Save();

        AnsiConsole.MarkupLine($"[green]Updated:[/] {Markup.Escape(document.FilePath!)}");
        ShowModule(registration, document, config, root);
        return 0;
    }

    private static int ValidateModule(
        SetupConfigRegistration registration,
        SetupJsonDocument document,
        string? root,
        string? rootError)
    {
        if (registration.PathProperties.Count > 0 && root == null)
        {
            AnsiConsole.MarkupLine($"[red]Root:[/] {Markup.Escape(rootError ?? "Root is not configured.")}");
            return 1;
        }

        var config = document.GetModuleConfig(registration) ?? registration.CreateDefault();
        var result = root == null
            ? new SetupPathNormalizationResult(Array.Empty<string>(), Array.Empty<SetupPathValue>())
            : registration.NormalizePaths(root, config);

        if (!result.IsValid)
        {
            AnsiConsole.MarkupLine("[red]Invalid setup.json:[/]");
            PrintErrors(result.Errors);
            return 1;
        }

        AnsiConsole.MarkupLine($"[green]Valid:[/] {Markup.Escape(document.FilePath!)}");
        if (root != null)
            AnsiConsole.MarkupLine($"[grey]Root:[/] {Markup.Escape(root)}");

        foreach (var path in result.Paths)
            AnsiConsole.MarkupLine($"  [grey]{Markup.Escape(registration.ModuleName)}.{Markup.Escape(path.MemberName)}[/] = {Markup.Escape(path.NormalizedValue)}");

        if (!document.HasModuleSection(registration.SectionName))
            AnsiConsole.MarkupLine(
                $"[yellow]Section 'modules.{Markup.Escape(registration.SectionName)}' is not written yet; defaults are valid.[/]");

        return 0;
    }

    private static void ShowOverview(ModuleConfigRegistry registry, SetupJsonDocument document)
    {
        AnsiConsole.Write(new Rule("[cyan]ObFramework setup.json[/]"));
        AnsiConsole.MarkupLine($"[grey]File:[/] {Markup.Escape(document.FilePath ?? "(unknown)")}");
        if (!document.Exists)
            AnsiConsole.MarkupLine(
                "[yellow]Not found.[/] Use 'ob config <module> --create --root <DIR>' to create it, " +
                "or pass --file <FILE>.");
        AnsiConsole.MarkupLine(document.TryGetAbsoluteRoot(out var root, out var rootError)
            ? $"[green]Root:[/] {Markup.Escape(root)}"
            : $"[yellow]Root:[/] {Markup.Escape(rootError ?? "not configured")}");

        var table = new Spectre.Console.Table().Border(TableBorder.Rounded);
        table.AddColumn("Module");
        table.AddColumn("setup.json section");
        table.AddColumn("In file");
        table.AddColumn("Module paths");

        foreach (var registration in registry.Modules)
        {
            table.AddRow(
                new Text(registration.ModuleName),
                new Text(registration.SectionName),
                new Text(document.HasModuleSection(registration.SectionName) ? "yes" : "no"),
                new Text(registration.PathProperties.Count.ToString()));
        }

        AnsiConsole.Write(table);

        var unregistered = document.ModuleSectionNames
            .Where(section => !registry.TryGet(section, out _))
            .ToArray();
        if (unregistered.Length > 0)
            AnsiConsole.MarkupLine(
                $"[grey]Preserved unregistered module sections:[/] {Markup.Escape(string.Join(", ", unregistered))}");

        var unknownTopLevel = document.PropertyNames
            .Where(name => !string.Equals(name, SetupJsonDocument.RootPropertyName, StringComparison.Ordinal) &&
                           !string.Equals(name, SetupJsonDocument.ModulesPropertyName, StringComparison.Ordinal))
            .ToArray();
        if (unknownTopLevel.Length > 0)
            AnsiConsole.MarkupLine(
                $"[grey]Preserved unknown top-level properties:[/] {Markup.Escape(string.Join(", ", unknownTopLevel))}");
    }

    private static void ShowModule(
        SetupConfigRegistration registration,
        SetupJsonDocument document,
        object config,
        string? root)
    {
        AnsiConsole.Write(new Rule($"[cyan]{Markup.Escape(registration.ModuleName)}[/]"));
        AnsiConsole.MarkupLine($"[grey]File:[/] {Markup.Escape(document.FilePath ?? "(unknown)")}");
        if (root != null)
            AnsiConsole.MarkupLine($"[green]Root:[/] {Markup.Escape(root)}");

        if (!document.HasModuleSection(registration.SectionName))
            AnsiConsole.MarkupLine(
                $"[yellow]Section 'modules.{Markup.Escape(registration.SectionName)}' is not present; defaults shown.[/]");

        var table = new Spectre.Console.Table().Border(TableBorder.Rounded);
        table.AddColumn("Option");
        table.AddColumn("Value");
        table.AddColumn("Resolved path");

        foreach (var described in SetupConfigValues.Describe(config))
        {
            if (described.IsModulePath)
            {
                foreach (var path in ReadPathValues(registration, config, described.PropertyName))
                {
                    var resolved = string.Empty;
                    if (root != null)
                    {
                        try
                        {
                            var normalized = SetupConfigPaths.NormalizeModulePath(root, path.OriginalValue);
                            resolved = SetupConfigPaths.GetFullModulePath(root, normalized);
                        }
                        catch (SetupConfigException ex)
                        {
                            resolved = $"! {ex.Message}";
                        }
                    }

                    table.AddRow(
                        new Text(described.JsonName),
                        new Text(path.OriginalValue),
                        new Text(resolved));
                }

                continue;
            }

            table.AddRow(new Text(described.JsonName), new Text(described.Value), new Text("-"));
        }

        AnsiConsole.Write(table);

        var unregistered = document.ModuleSectionNames
            .Where(section => !string.Equals(section, registration.SectionName, StringComparison.Ordinal))
            .ToArray();
        if (unregistered.Length > 0)
            AnsiConsole.MarkupLine(
                $"[grey]Other module sections preserved:[/] {Markup.Escape(string.Join(", ", unregistered))}");
    }

    private static IEnumerable<SetupPathValue> ReadPathValues(SetupConfigRegistration registration, object config, string propertyName)
    {
        foreach (var value in registration.ReadPathValues(config))
        {
            if (string.Equals(value.MemberName, propertyName, StringComparison.Ordinal) ||
                value.MemberName.StartsWith(propertyName + "[", StringComparison.Ordinal))
            {
                yield return value;
            }
        }
    }

    private static bool TryParseAssignment(string? assignment, out string key, out string value, out string? error)
    {
        key = string.Empty;
        value = string.Empty;
        error = null;

        if (string.IsNullOrWhiteSpace(assignment))
        {
            error = "expected KEY=VALUE.";
            return false;
        }

        var separator = assignment.IndexOf('=');
        if (separator <= 0)
        {
            error = "expected KEY=VALUE.";
            return false;
        }

        key = assignment.Substring(0, separator).Trim();
        value = assignment.Substring(separator + 1);

        if (key.Length == 0)
        {
            error = "KEY must not be empty.";
            return false;
        }

        return true;
    }

    private static void PrintErrors(IReadOnlyList<string> errors)
    {
        foreach (var error in errors)
            AnsiConsole.MarkupLine($"[red]-[/] {Markup.Escape(error)}");
    }
}