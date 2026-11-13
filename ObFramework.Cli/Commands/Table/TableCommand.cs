using System.ComponentModel;
using ObFramework.Cli.Commands.Config;
using Spectre.Console.Cli;

namespace ObFramework.Cli.Commands.Table
{
    internal sealed class TableCommand : AsyncCommand<TableCommand.Settings>
    {
        internal sealed class Settings : CommandSettings
        {
            [CommandArgument(0, "<ACTION>")]
            [Description("validate or generate")]
            public string Action { get; set; } = string.Empty;

            [CommandOption("--file <FILE>")]
            [Description("Explicit setup.json file; defaults to the nearest setup.json.")]
            public string? File { get; set; }

            [CommandOption("-d")]
            [Description("Generate only data file.")]
            public bool DataOnly { get; set; }

            [CommandOption("-c")]
            [Description("Generate only code file.")]
            public bool CodeOnly { get; set; }
        }

        public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
        {
            try
            {
                var setupFile = SetupJsonDiscovery.ResolveSetupFile(settings.File)
                    ?? throw new SetupConfigException("No setup.json found. Run 'ob init --root <DIR>'.");
                return await LubanRunner.RunAsync(settings.Action.Trim().ToLowerInvariant(), setupFile,
                    settings.DataOnly, settings.CodeOnly, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                Console.Error.WriteLine("Table: cancelled.");
                return 1;
            }
            catch (Exception ex) when (ex is SetupConfigException or IOException or UnauthorizedAccessException
                or ArgumentException or InvalidOperationException or Win32Exception)
            {
                Console.Error.WriteLine($"Table: {ex.Message}");
                return 1;
            }
        }
    }
}
