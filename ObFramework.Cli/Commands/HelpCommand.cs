using System.ComponentModel;
using ObFramework.Cli.Core;
using Spectre.Console.Cli;

namespace ObFramework.Cli.Commands
{
    internal sealed class HelpSettings : CommandSettings
    {
        [CommandArgument(0, "[command]")]
        [Description("Command path to show help for")]
        public string[] Path { get; set; } = [];
    }

    internal sealed class HelpCommand : Command<HelpSettings>
    {
        public override int Execute(CommandContext context,HelpSettings settings,CancellationToken cancellationToken)
        {
            if (context.Data is not CliSessionState session)
                throw new InvalidOperationException("CLI session is not configured.");

            session.RequestHelp(settings.Path);
            return 0;
        }
    }
}