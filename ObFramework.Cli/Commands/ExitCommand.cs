using ObFramework.Cli.Core;
using Spectre.Console.Cli;

namespace ObFramework.Cli.Commands
{
    internal sealed class ExitSettings : CommandSettings
    {
    }

    internal class ExitCommand : Command<ExitSettings>
    {
        public override int Execute(CommandContext context, ExitSettings settings, CancellationToken cancellationToken)
        {
            if (context.Data is not CliSessionState state)
            {
                throw new InvalidOperationException("CLI session is not configured.");
            }
            state.RequestExit();
            return 0;
        }
    }
}
