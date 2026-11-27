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

        public ObFramework.Cli.Commands.Config.Modules.LubanSetupConfig? LubanPaths { get; set; }
        public string Resources { get; set; } = "custom";
        public string PackageVersion { get; set; } = ObFramework.Cli.Unity.UnityPackageInstaller.DefaultVersion;
        public string? Source { get; set; }
        public bool SkipPackages { get; set; }
        public string Root { get; }
        public string SetupFile => Path.Combine(Root, "setup.json");
        public EngineKind Engine { get; }
        public bool SkipDownload { get; }
        public CancellationToken CancellationToken { get; }
    }
}
