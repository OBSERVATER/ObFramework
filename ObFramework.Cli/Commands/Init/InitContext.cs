namespace ObFramework.Cli.Commands.Init
{
    internal sealed class InitContext
    {
        public InitContext(string root, EngineKind engine, bool skipDownload, CancellationToken cancellationToken)
        {
            Root = root;
            Engine = engine;
            SkipDownload = skipDownload;
            CancellationToken = cancellationToken;
        }

        public string Root { get; }
        public string SetupFile => Path.Combine(Root, "setup.json");
        public EngineKind Engine { get; }
        public bool SkipDownload { get; }
        public CancellationToken CancellationToken { get; }
    }
}
