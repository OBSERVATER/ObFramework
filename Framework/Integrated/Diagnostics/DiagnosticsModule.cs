using Newtonsoft.Json;
using Serilog;
using System;
using System.IO;

namespace ObFramework.Integrated.Diagnostics
{
    public class DiagnosticsModule
    {
        private static Serilog.Core.Logger _logger;
        public static void Init(string configDir)
        {
            var raw = File.ReadAllText(Path.Combine(configDir, "diagnostics.json"));
            var option = JsonConvert.DeserializeObject<DiagnosticsOption>(raw);
            Init(option);
        }
        public static void Init(DiagnosticsOption option)
        {
            if (_logger != null)
            {
                throw new InvalidOperationException("duplicate init");
            }
            _logger = new LoggerConfiguration()
                    .MinimumLevel.Is(option.MinLevel)
                    .WriteTo.Async(sink => sink.File(Path.Combine(option.OutputDirPath, $"game-{DateTime.Now:yyyy-MM-dd HH:mm:ss}"),
                    outputTemplate: "[{Timestamp:HH:mm:ss.fff}][{Level}][{SourceContext}]{Message:lj}{NewLine}{Exception}",
                    fileSizeLimitBytes: (long)option.MaxFileSizeMB * 1024 * 1024,
                    rollOnFileSizeLimit: true,
                    retainedFileCountLimit: option.MaxFileCount))
                    .CreateLogger();
        }
        public static ILogger GetLogger(string s) => _logger.ForContext("SourceContext",s);
        public static void Shutdown()
        {
            _logger?.Dispose();
            _logger = null;
        }
        public static void I(string content) => _logger.Information(content);
        public static void W(string content) => _logger.Warning(content);
        public static void E(string content) => _logger.Error(content);
        public static void D(string content) => _logger.Debug(content);
    }
}
