using ObFramework.Adapter.UnrealSharp.Diagnostics;
using ObFramework.Core;
using UnrealSharp.Attributes;
using UnrealSharp.Engine;

namespace ObFramework.Adapter.UnrealSharp.Core;

[UClass(ClassFlags.Abstract)]
public partial class AGameLauncher : AActor
{
    protected virtual PluginApplication CreateApplication() =>
        throw new NotImplementedException($"{GetType().Name} must override CreateApplication.");

    public override void BeginPlay()
    {
        base.BeginPlay();
        _ = LaunchAsync();
    }

    private async Task LaunchAsync()
    {
        UObFrameworkSubsystem? subsystem = null;
        try
        {
            subsystem = GetGameInstanceSubsystem<UObFrameworkSubsystem>()
                ?? throw new InvalidOperationException("The game instance has no ObFramework subsystem.");
            await subsystem.StartAsync(CreateApplication);
        }
        catch (OperationCanceledException) when (subsystem?.IsStopping == true) { }
        catch (Exception error) { ObFLog.LogError(error.ToString()); }
    }
}
