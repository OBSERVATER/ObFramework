using System;
using System.IO;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace ObFramework.Integrated.Diagnostics
{
    public interface IDiagnosticsManager
    {
        ILogger GetLogger(string source);
    }

    internal sealed class DiagnosticsManager : IDiagnosticsManager, IDisposable
    {
        private ILogger? _logger;
        private Logger? _ownedLogger;
        internal void Initialize(DiagnosticsOption option, ILogEventSink? extraSink)
        {
            var config = new LoggerConfiguration().MinimumLevel.Is(option.MinLevel).WriteTo.Async(sink =>
                sink.File(Path.Combine(option.OutputDirPath, $"game-{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.log"),
                    outputTemplate: "[{Timestamp:HH:mm:ss.fff}][{Level}][{SourceContext}]{Message:lj}{NewLine}{Exception}",
                    fileSizeLimitBytes: checked((long)option.MaxFileSizeMB * 1024 * 1024),
                    rollOnFileSizeLimit: true, retainedFileCountLimit: option.MaxFileCount));
            if (extraSink != null)
                config.WriteTo.Sink(new BorrowedSink(extraSink));
            _ownedLogger = config.CreateLogger();
            _logger = _ownedLogger;
        }
        internal void Initialize(ILogger logger) => _logger = logger;
        public ILogger GetLogger(string source) => (_logger ?? throw new InvalidOperationException("Diagnostics is not initialized or has been stopped.")).ForContext("SourceContext", source);
        internal void Shutdown()
        {
            var logger = _ownedLogger;
            _ownedLogger = null;
            _logger = null;
            logger?.Dispose();
        }
        public void Dispose() => Shutdown();

        private sealed class BorrowedSink : ILogEventSink
        {
            private readonly ILogEventSink _sink;
            internal BorrowedSink(ILogEventSink sink) => _sink = sink;
            public void Emit(LogEvent logEvent) => _sink.Emit(logEvent);
        }
    }
}
