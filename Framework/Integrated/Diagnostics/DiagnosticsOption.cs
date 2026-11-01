namespace ObFramework.Integrated.Diagnostics
{
    public class DiagnosticsOption
    {
        public int MaxFileSizeMB { get; set; } = 10;
        public int MaxFileCount { get;set; } = 10;
        public string OutputDirPath { get;set; }
        public string ConfigDirPath { get; set; } = "Config";
        public Serilog.Events.LogEventLevel MinLevel { get; set; } = Serilog.Events.LogEventLevel.Debug;

        public DiagnosticsOption(string outputPath)
        {
            OutputDirPath = outputPath;
        }
    }
}
