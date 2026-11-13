using ObFramework.Cli.Utility;
using Spectre.Console;
using Spectre.Console.Cli;

namespace ObFramework.Cli.Commands
{
    internal sealed class ListSettings : CommandSettings
    {
    }

    internal sealed class ListCommand : Command<ListSettings>
    {
        public override int Execute(CommandContext context,ListSettings settings,CancellationToken cancellationToken)
        {
            var assembly = typeof(TemplateGenerateHelper).Assembly;
            var resources = assembly.GetManifestResourceNames();
            Array.Sort(resources, StringComparer.Ordinal);

            if (resources.Length == 0)
            {
                AnsiConsole.MarkupLine("[yellow]No embedded resources found.[/]");
                return 0;
            }

            var table = new Spectre.Console.Table();
            table.AddColumn("Embedded Resource");

            foreach (var resource in resources)
            {
                table.AddRow(new Text(resource));
            }

            AnsiConsole.Write(table);
            return 0;
        }
    }
}
