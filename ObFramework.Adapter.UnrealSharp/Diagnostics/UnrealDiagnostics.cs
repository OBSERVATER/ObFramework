using ObFramework.Integrated.Diagnostics;
using Serilog.Events;
using UnrealSharp.UnrealSharpUtilities;

namespace ObFramework.Adapter.UnrealSharp.Diagnostics;

public static class UnrealDiagnostics
{
    public static DiagnosticsPlugin Create()
    {
        return new(
            Path.Combine(Path.GetFullPath(Path.Combine(
                    UCSPathsBlueprintFunctionLibrary.PluginsDirectory,
                    "..",
                    "Saved",
                    "Logs")),
                "Logs"),LogEventLevel.Information,
            new UnrealLogSink());
    }
}
