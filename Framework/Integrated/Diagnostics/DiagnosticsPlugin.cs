using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using ObFramework.Core;
using Serilog;
using Serilog.Core;

namespace ObFramework.Integrated.Diagnostics
{
    public sealed class DiagnosticsPlugin : IPlugin
    {
        private readonly DiagnosticsOption? _option;
        private readonly string? _configDirectory;
        private readonly ILogEventSink? _extraSink;
        private readonly ILogger? _externalLogger;
        public DiagnosticsPlugin(DiagnosticsOption option, ILogEventSink? extraSink = null)
        {
            if (option == null)
                throw new ArgumentNullException(nameof(option));
            _option = new DiagnosticsOption(option.OutputDirPath)
            {
                ConfigDirPath = option.ConfigDirPath, MinLevel = option.MinLevel,
                MaxFileCount = option.MaxFileCount, MaxFileSizeMB = option.MaxFileSizeMB
            };
            _extraSink = extraSink;
        }
        public DiagnosticsPlugin(string configDirectory, ILogEventSink? extraSink = null)
        {
            _configDirectory = configDirectory ?? throw new ArgumentNullException(nameof(configDirectory));
            _extraSink = extraSink;
        }
        public DiagnosticsPlugin(ILogger logger) => _externalLogger = logger ?? throw new ArgumentNullException(nameof(logger));
        public void Register(PluginRegistration registration) => registration.Export<IDiagnosticsManager, DiagnosticsManager>();
        public async Task InitAsync(IPluginContext plugins, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var manager = (DiagnosticsManager)plugins.Get<IDiagnosticsManager>();
            if (_externalLogger != null)
                manager.Initialize(_externalLogger);
            else
            {
                var option = _option;
                if (option == null)
                {
                    var raw = await File.ReadAllTextAsync(Path.Combine(_configDirectory!, "diagnostics.json"), ct);
                    option = JsonConvert.DeserializeObject<DiagnosticsOption>(raw) ?? throw new JsonSerializationException("Invalid diagnostics configuration.");
                }
                ct.ThrowIfCancellationRequested();
                manager.Initialize(option, _extraSink);
            }
        }
        public Task StopAsync(IPluginContext plugins)
        {
            ((DiagnosticsManager)plugins.Get<IDiagnosticsManager>()).Shutdown();
            return Task.CompletedTask;
        }
    }
}
