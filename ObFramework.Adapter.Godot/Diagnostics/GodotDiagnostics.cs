using System.IO;
using Godot;
using ObFramework.Integrated.Diagnostics;
using Serilog.Events;

namespace ObFramework.Adapter.Godot.Diagnostics
{
    public static class GodotDiagnostics
    {
        public static DiagnosticsPlugin Create()
        {
            var level = OS.IsDebugBuild() ? LogEventLevel.Information : LogEventLevel.Warning;
            return new DiagnosticsPlugin(Path.Combine(OS.GetUserDataDir(), "Logs"), level,new GodotLogSink());
        }
    }
}
