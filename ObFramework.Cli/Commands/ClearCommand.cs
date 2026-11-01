using ObFramework.Core;
using Spectre.Console.Cli;

namespace ObFramework.Cli.Commands
{
    internal class ClearSetting : CommandSettings { }
    internal class ClearCommand : Command<ClearSetting>
    {
        public override int Execute(CommandContext context, ClearSetting settings, CancellationToken cancellationToken)
        {
            Console.Clear();
            return 0;
        }
    }
}
