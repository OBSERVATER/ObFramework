using ObFramework.Integrated.Diagnostics;
using UnrealSharp.UnrealSharpUtilities;

namespace ObFramework.Adapter.UnrealSharp.Diagnostics;

public static class UnrealDiagnostics
{
    public static DiagnosticsPlugin Create() => new(
        Path.Combine(Path.GetFullPath(Path.Combine(
                UCSPathsBlueprintFunctionLibrary.PluginsDirectory,
                "..", "Saved", "Logs")), "Logs"),
        new UnrealLogSink());
}
