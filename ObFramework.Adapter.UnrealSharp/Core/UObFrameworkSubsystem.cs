using ObFramework.Core;
using ObFramework.Adapter.UnrealSharp.Diagnostics;
using UnrealSharp.Attributes;
using UnrealSharp.UnrealSharpCore;

namespace ObFramework.Adapter.UnrealSharp.Core;

[UClass]
public partial class UObFrameworkSubsystem : UCSGameInstanceSubsystem
{
    private PluginApplication? _application;
    private bool _closed;
    public IArchitecture Architecture => _application?.Architecture ?? throw new InvalidOperationException("Application has not started.");

    public Task StartAsync(PluginApplication application)
    {
        if (_application != null || _closed)
            throw new InvalidOperationException("Subsystem can only start one application.");
        _application = application ?? throw new ArgumentNullException(nameof(application));
        return _application.StartAsync();
    }

    public Task StopAsync() => _application?.StopAsync() ?? Task.CompletedTask;

    public override void Deinitialize()
    {
        _closed = true;
        _ = StopAndObserveAsync();
        base.Deinitialize();
    }

    private async Task StopAndObserveAsync()
    {
        try { await StopAsync(); }
        catch (Exception error) { ObFLog.LogError(error.ToString()); }
    }
}
