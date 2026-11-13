using System.IO;
using Godot;
using ObFramework.Integrated.Diagnostics;

namespace ObFramework.Adapter.Godot.Diagnostics
{
    public static class GodotDiagnostics
    {
        public static DiagnosticsPlugin Create() => new DiagnosticsPlugin(
            Path.Combine(OS.GetUserDataDir(), "Logs"), new GodotLogSink());
    }
}
