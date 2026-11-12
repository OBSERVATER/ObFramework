using ObFramework.Cli.Commands;
using ObFramework.Cli.Commands.Reference;
using Spectre.Console.Cli;

namespace ObFramework.Cli.Core
{
    public sealed class CliModule
    {
        private readonly CommandApp _app = new CommandApp();
        private readonly CliSessionState _session = new CliSessionState();

        private bool _initialized;
        private bool _started;

        public bool ExitRequested => _session.ExitRequested;

        public void Initialize()
        {
            if (_initialized)
                throw new InvalidOperationException("CLI is already initialized.");

            _app.Configure(config =>
            {
                config.SetApplicationName("ob");

                config.AddCommand<HelpCommand>("help")
                    .WithDescription("Show command help")
                    .WithData(_session);

                config.AddCommand<ExitCommand>("exit")
                    .WithDescription("Exit interactive mode")
                    .WithData(_session);

                config.AddCommand<ClearCommand>("clear")
                    .WithDescription("Clear console")
                    .WithData(_session);

                config.AddCommand<ListCommand>("list")
                    .WithDescription("List templates")
                    .WithData(_session);

                config.AddCommand<CreateCommand>("create")
                    .WithDescription("Create template code")
                    .WithData(_session);

                config.AddCommand<DiagnosticsCommand>("diagnostics")
                    .WithDescription("something about diagnostics and log")
                    .WithData(_session);
                
                config.AddCommand<ReferenceCommand>("reference")
                    .WithDescription("references among systems and models")
                    .WithData(_session);
            });

            _initialized = true;
        }

        public void InjectCommand<T>(string name) where T : class, ICommand
        {
            if (!_initialized || _started)
                throw new InvalidOperationException("Commands can only be registered during initialization.");

            _app.Configure(config =>
            {
                config.AddCommand<T>(name);
            });
        }

        public async Task<int> RunAsync(string[] args,CancellationToken cancellationToken = default)
        {
            if (!_initialized)
                throw new InvalidOperationException("CLI is not initialized.");

            _started = true;

            // 执行正常命令
            var result = await _app.RunAsync(args, cancellationToken);

            // 如果 HelpCommand 请求帮助，
            // 则转交 Spectre 原生帮助系统
            var helpPath = _session.TakeHelpRequest();

            if (helpPath == null)
                return result;

            var helpArgs = new string[helpPath.Length + 1];

            Array.Copy(helpPath, helpArgs, helpPath.Length);
            helpArgs[^1] = "--help";

            return await _app.RunAsync(helpArgs, cancellationToken);
        }
    }
}