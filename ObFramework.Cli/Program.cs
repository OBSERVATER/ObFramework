using ObFramework.Cli.Config;
using ObFramework.Cli.Core;
using ObFramework.Cli.Deployment;
using System.Text;

namespace ObFramework.Cli
{
    internal static class Program
    {
        private static async Task<int> Main(string[] args)
        {
            var cli = new CliModule();
            cli.Initialize();

            // Package contributed commands use the existing extension point; CliModule itself is
            // not modified and never scans assemblies.
            ConfigCliModule.Register(cli);
            UnityDeployCliModule.Register(cli);

            // 非交互模式：直接执行后退出
            if (args.Length > 0)
                return await cli.RunAsync(args);

            // 交互模式
            return await RunInteractiveAsync(cli);
        }

        private static async Task<int> RunInteractiveAsync(CliModule cli)
        {
            Console.WriteLine("ObFramework CLI");
            Console.WriteLine($"It's {DateTime.Now:yyyy-MM-dd HH:mm:ss} now, type 'help' to get more information or 'exit' to exit");
            while (!cli.ExitRequested)
            {
                Console.Write("<3 ");
                var input = Console.ReadLine();

                if (input == null)
                    break;

                if (string.IsNullOrWhiteSpace(input))
                    continue;

                try
                {
                    var args = SplitArguments(input);

                    if (args.Length == 0)
                        continue;

                    await cli.RunAsync(args);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(ex.Message);
                }
            }

            return 0;
        }

        private static string[] SplitArguments(string input)
        {
            var result = new List<string>();
            var current = new StringBuilder();

            bool quoted = false;
            bool started = false;

            foreach (char c in input)
            {
                if (c == '"')
                {
                    quoted = !quoted;
                    started = true;
                    continue;
                }

                if (char.IsWhiteSpace(c) && !quoted)
                {
                    if (started)
                    {
                        result.Add(current.ToString());
                        current.Clear();
                        started = false;
                    }

                    continue;
                }

                current.Append(c);
                started = true;
            }

            if (quoted)
                throw new FormatException("Unclosed quotation mark.");

            if (started)
                result.Add(current.ToString());

            return [..result];
        }
    }
}