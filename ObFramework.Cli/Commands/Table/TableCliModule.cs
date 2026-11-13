using ObFramework.Cli.Core;

namespace ObFramework.Cli.Commands.Table
{
    public static class TableCliModule
    {
        public static void Register(CliModule cli) => cli.InjectCommand<TableCommand>("table");
    }
}
