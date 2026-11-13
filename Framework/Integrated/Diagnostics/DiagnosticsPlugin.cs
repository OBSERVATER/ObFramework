using System;
using System.Threading;
using System.Threading.Tasks;
using ObFramework.Core;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace ObFramework.Integrated.Diagnostics
{
    public sealed class DiagnosticsPlugin : IPlugin
    {
        private readonly string? _outputDirectory;
        private readonly ILogEventSink? _extraSink;
        private readonly ILogger? _externalLogger;
        private readonly DiagnosticsManager _manager = new DiagnosticsManager();
        private readonly LogEventLevel _lvl;
        public DiagnosticsPlugin(string outputDirectory, LogEventLevel lvl, ILogEventSink? extraSink = null)
        {
            _outputDirectory = outputDirectory ?? throw new ArgumentNullException(nameof(outputDirectory));
            _lvl = lvl;
            _extraSink = extraSink;
        }

        public DiagnosticsPlugin(ILogger logger, LogEventLevel lvl)
        {
            _externalLogger = logger ?? throw new ArgumentNullException(nameof(logger));
            _lvl = lvl;
        }

        public ILogger GetLogger(string source) => _manager.GetLogger(source);
        void IPlugin.Register(PluginRegistration registration) { }

        Task IPlugin.InitAsync(IPluginContext plugins, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (_externalLogger != null)
                _manager.Initialize(_externalLogger);
            else
                _manager.Initialize(_outputDirectory!,_lvl, _extraSink);
            return Task.CompletedTask;
        }

        Task IPlugin.StopAsync(IPluginContext plugins)
        {
            _manager.Shutdown();
            return Task.CompletedTask;
        }
    }
}
