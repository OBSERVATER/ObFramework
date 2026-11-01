using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ObFramework.Integrated.Diagnostics;
using Serilog.Events;
using Spectre.Console;
using Spectre.Console.Cli;

namespace ObFramework.Cli.Commands
{
    /// <summary>
    /// Creates, edits, displays and validates the Diagnostics JSON configuration.
    /// This command does not initialize the runtime logger.
    /// </summary>
    public sealed class DiagnosticsCommand : Command<DiagnosticsCommand.Settings>
    {
        public sealed class Settings : CommandSettings
        {
            [CommandArgument(0, "[ACTION]")]
            [Description("init, edit, show or validate. Omit to open the guided menu.")]
            public string Action { get; set; } = "menu";

            [CommandOption("-o|--output <FILE>")]
            [Description("Diagnostics JSON configuration path")]
            public string Output { get; set; } = "Config/diagnostics.json";

            [CommandOption("--log-dir <DIR>")]
            [Description("Initial log directory used by init")]
            public string LogDirectory { get; set; } = "Logs";
        }

        private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

        public override int Execute(
            CommandContext context,
            Settings settings,
            CancellationToken cancellation)
        {
            if (string.IsNullOrWhiteSpace(settings.Output))
                return Fail("--output must not be empty.");

            try
            {
                cancellation.ThrowIfCancellationRequested();
                var path = Path.GetFullPath(settings.Output);
                var action = string.IsNullOrWhiteSpace(settings.Action)
                    ? "menu"
                    : settings.Action.Trim().ToLowerInvariant();

                switch (action)
                {
                    case "menu":
                        return RunMenu(path, settings.LogDirectory);
                    case "init":
                        return Init(path, settings.LogDirectory);
                    case "edit":
                        return Edit(path);
                    case "show":
                        return Show(path);
                    case "validate":
                        return ValidateConfig(path);
                    default:
                        return Fail($"Unknown diagnostics action: {action}. Expected init, edit, show or validate.");
                }
            }
            catch (OperationCanceledException)
            {
                return Fail("Operation cancelled.");
            }
            catch (Exception ex) when (
                ex is IOException ||
                ex is UnauthorizedAccessException ||
                ex is ArgumentException ||
                ex is NotSupportedException ||
                ex is JsonException ||
                ex is InvalidOperationException ||
                ex is FormatException ||
                ex is OverflowException)
            {
                return Fail(ex.Message);
            }
        }

        private static int RunMenu(string path, string defaultLogDirectory)
        {
            AnsiConsole.Write(new Rule("[cyan]ObFramework Diagnostics[/]"));
            AnsiConsole.MarkupLine($"[grey]Configuration:[/] {Markup.Escape(path)}");
            AnsiConsole.WriteLine();

            var choices = File.Exists(path)
                ? new[] { "Edit configuration", "Show configuration", "Validate configuration", "Exit" }
                : ["Create configuration", "Exit"];

            var selected = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("What would you like to do?")
                    .PageSize(8)
                    .WrapAround()
                    .AddChoices(choices));

            switch (selected)
            {
                case "Create configuration":
                    return Init(path, defaultLogDirectory);
                case "Edit configuration":
                    return Edit(path);
                case "Show configuration":
                    return Show(path);
                case "Validate configuration":
                    return ValidateConfig(path);
                default:
                    return 0;
            }
        }

        private static int Init(string path, string logDirectory)
        {
            if (File.Exists(path))
                return Fail($"Configuration already exists: {path}. Use 'edit' to modify it.");

            var option = new DiagnosticsOption(logDirectory)
            {
                MaxFileSizeMB = 10,
                MaxFileCount = 10,
                MinLevel = LogEventLevel.Debug,
            };

            AnsiConsole.MarkupLine("[bold]Let's configure Diagnostics.[/]");
            AnsiConsole.MarkupLine("[grey]Press Enter to keep each suggested default.[/]");
            AnsiConsole.WriteLine();

            option.OutputDirPath = AskString("Log output directory", option.OutputDirPath);
            option.MinLevel = AskLogLevel(option.MinLevel);
            option.MaxFileSizeMB = AskPositiveInt("Maximum file size (MB)", option.MaxFileSizeMB);
            option.MaxFileCount = AskPositiveInt("Maximum retained file count", option.MaxFileCount);

            EnsureValid(option);
            ShowOption(option);

            if (!AnsiConsole.Confirm("Create this configuration?", true))
            {
                AnsiConsole.MarkupLine("[yellow]Creation cancelled.[/]");
                return 0;
            }

            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            // CreateNew prevents overwriting an existing file, including a concurrent CLI invocation.
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                writer.WriteLine(JsonSerializer.Serialize(option, JsonOptions));
            }

            AnsiConsole.MarkupLine($"[green]Created:[/] {Markup.Escape(path)}");
            return 0;
        }

        private static int Edit(string path)
        {
            if (!File.Exists(path))
                return Fail($"Configuration not found: {path}. Run 'diagnostics init' first.");

            var option = Load(path);
            AnsiConsole.MarkupLine("[bold]Edit Diagnostics configuration[/]");
            AnsiConsole.MarkupLine("[grey]Choose a setting to edit. Select Save when finished.[/]");

            while (true)
            {
                AnsiConsole.WriteLine();
                var selected = AnsiConsole.Prompt(
                    new SelectionPrompt<string>()
                        .Title("Select a setting")
                        .PageSize(8)
                        .WrapAround()
                        .AddChoices(
                            "Log output directory",
                            "Minimum log level",
                            "Maximum file size (MB)",
                            "Maximum retained file count",
                            "Show current configuration",
                            "Save changes",
                            "Discard changes"));

                switch (selected)
                {
                    case "Log output directory":
                        option.OutputDirPath = AskString("Log output directory", option.OutputDirPath);
                        break;
                    case "Minimum log level":
                        option.MinLevel = AskLogLevel(option.MinLevel);
                        break;
                    case "Maximum file size (MB)":
                        option.MaxFileSizeMB = AskPositiveInt("Maximum file size (MB)", option.MaxFileSizeMB);
                        break;
                    case "Maximum retained file count":
                        option.MaxFileCount = AskPositiveInt("Maximum retained file count", option.MaxFileCount);
                        break;
                    case "Show current configuration":
                        ShowOption(option);
                        break;
                    case "Save changes":
                        EnsureValid(option);
                        ShowOption(option);
                        if (!AnsiConsole.Confirm("Save these changes?", true))
                            break;
                        SaveAtomically(path, option);
                        AnsiConsole.MarkupLine($"[green]Updated:[/] {Markup.Escape(path)}");
                        return 0;
                    case "Discard changes":
                        if (AnsiConsole.Confirm("Discard all unsaved changes?", false))
                        {
                            AnsiConsole.MarkupLine("[yellow]Changes discarded.[/]");
                            return 0;
                        }
                        break;
                }
            }
        }

        private static int Show(string path)
        {
            var option = Load(path);
            ShowOption(option);
            return 0;
        }

        private static int ValidateConfig(string path)
        {
            var option = Load(path);
            EnsureValid(option);
            AnsiConsole.MarkupLine($"[green]Configuration is valid:[/] {Markup.Escape(path)}");
            return 0;
        }

        private static DiagnosticsOption Load(string path)
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new JsonException("Diagnostics configuration must be a JSON object.");

            var outputDir = GetRequiredString(root, nameof(DiagnosticsOption.OutputDirPath));
            var levelName = GetRequiredString(root, nameof(DiagnosticsOption.MinLevel));
            if (!Enum.TryParse(levelName, true, out LogEventLevel level) ||
                !Enum.IsDefined(typeof(LogEventLevel), level))
                throw new JsonException("MinLevel must be a valid Serilog level name.");

            var option = new DiagnosticsOption(outputDir)
            {
                MaxFileSizeMB = GetRequiredInt32(root, nameof(DiagnosticsOption.MaxFileSizeMB)),
                MaxFileCount = GetRequiredInt32(root, nameof(DiagnosticsOption.MaxFileCount)),
                MinLevel = level,
            };

            EnsureValid(option);
            return option;
        }

        private static void SaveAtomically(string path, DiagnosticsOption option)
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            var tempPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(
                    tempPath,
                    JsonSerializer.Serialize(option, JsonOptions),
                    new UTF8Encoding(false));

                File.Move(tempPath, path, true);
            }
            finally
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
        }

        private static void EnsureValid(DiagnosticsOption option)
        {
            if (string.IsNullOrWhiteSpace(option.OutputDirPath))
                throw new ArgumentException("OutputDirPath must not be empty.");
            if (option.MaxFileSizeMB <= 0)
                throw new ArgumentException("MaxFileSizeMB must be greater than zero.");
            if (option.MaxFileCount <= 0)
                throw new ArgumentException("MaxFileCount must be greater than zero.");
            if (!Enum.IsDefined(typeof(LogEventLevel), option.MinLevel))
                throw new ArgumentException("MinLevel is invalid.");
        }

        private static string AskString(string label, string current)
        {
            var value = AnsiConsole.Prompt(
                new TextPrompt<string>($"{Markup.Escape(label)} [grey]({Markup.Escape(current)})[/]")
                    .AllowEmpty());
            return string.IsNullOrWhiteSpace(value) ? current : value.Trim();
        }

        private static int AskPositiveInt(string label, int current)
        {
            while (true)
            {
                var input = AnsiConsole.Prompt(
                    new TextPrompt<string>($"{Markup.Escape(label)} [grey]({current})[/]")
                        .AllowEmpty());

                if (string.IsNullOrWhiteSpace(input))
                    return current;

                if (int.TryParse(input, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value > 0)
                    return value;

                AnsiConsole.MarkupLine("[red]Please enter a positive integer, or press Enter to keep the current value.[/]");
            }
        }

        private static LogEventLevel AskLogLevel(LogEventLevel current)
        {
            var levels = Enum.GetNames(typeof(LogEventLevel));
            var prompt = new SelectionPrompt<string>
            {
                Title = $"Minimum log level [grey](current: {current})[/]",
                DefaultValue = current.ToString()
            };
            prompt.AddChoices(levels);
            return (LogEventLevel)Enum.Parse(typeof(LogEventLevel), AnsiConsole.Prompt(prompt), true);
        }

        private static void ShowOption(DiagnosticsOption option)
        {
            var table = new Table().Border(TableBorder.Rounded).AddColumn("Setting").AddColumn("Value");

            table.AddRow("OutputDirPath", Markup.Escape(option.OutputDirPath));
            table.AddRow("MinLevel", option.MinLevel.ToString());
            table.AddRow("MaxFileSizeMB", option.MaxFileSizeMB.ToString(CultureInfo.InvariantCulture));
            table.AddRow("MaxFileCount", option.MaxFileCount.ToString(CultureInfo.InvariantCulture));
            AnsiConsole.Write(table);
        }

        private static string GetRequiredString(JsonElement root, string name)
        {
            if (!TryGetProperty(root, name, out var element) || element.ValueKind != JsonValueKind.String)
                throw new JsonException($"{name} must be a string.");
            return element.GetString() ?? throw new JsonException($"{name} must not be null.");
        }

        private static int GetRequiredInt32(JsonElement root, string name)
        {
            if (!TryGetProperty(root, name, out var element) || element.ValueKind != JsonValueKind.Number || !element.TryGetInt32(out var value))
                throw new JsonException($"{name} must be a 32-bit integer.");
            return value;
        }

        private static bool TryGetProperty(JsonElement root, string name, out JsonElement value)
        {
            foreach (var property in root.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }

            value = default;
            return false;
        }

        private static JsonSerializerOptions CreateJsonOptions()
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNameCaseInsensitive = true
            };
            options.Converters.Add(new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false));
            return options;
        }

        private static int Fail(string message)
        {
            Console.Error.WriteLine($"Diagnostics: {message}");
            return 1;
        }
    }
}
