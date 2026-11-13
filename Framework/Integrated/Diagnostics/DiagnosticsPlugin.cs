using System;
using System.Threading;
using System.Threading.Tasks;
using ObFramework.Core;
using Serilog;
using Serilog.Core;

namespace ObFramework.Integrated.Diagnostics
{
    public sealed class DiagnosticsPlugin : IPlugin
    {
        private readonly string? _outputDirectory;
        private readonly ILogEventSink? _extraSink;
        private readonly ILogger? _externalLogger;
        private readonly DiagnosticsManager _manager = new DiagnosticsManager();

        public DiagnosticsPlugin(string outputDirectory, ILogEventSink? extraSink = null)
        {
            _outputDirectory = outputDirectory ?? throw new ArgumentNullException(nameof(outputDirectory));
            _extraSink = extraSink;
        }

        public DiagnosticsPlugin(ILogger logger) => _externalLogger = logger ?? throw new ArgumentNullException(nameof(logger));

        public ILogger GetLogger(string source) => _manager.GetLogger(source);
        void IPlugin.Register(PluginRegistration registration) { }

        Task IPlugin.InitAsync(IPluginContext plugins, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (_externalLogger != null)
                _manager.Initialize(_externalLogger);
            else
                _manager.Initialize(_outputDirectory!, _extraSink);
            return Task.CompletedTask;
        }

        Task IPlugin.StopAsync(IPluginContext plugins)
        {
            _manager.Shutdown();
            return Task.CompletedTask;
        }
    }
}
