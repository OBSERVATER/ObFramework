using ObFramework.Core;
using ObFramework.Core.FrameLoop;
using ObFramework.Adapter.UnrealSharp.Diagnostics;
using UnrealSharp.Attributes;
using UnrealSharp.UnrealSharpCore;

namespace ObFramework.Adapter.UnrealSharp.Core;

[UClass]
public partial class UObFrameworkSubsystem : UCSGameInstanceSubsystem, IFrameLoopDriver
{
    private const float FixedStep = 0.02f;
    private const int MaxFixedStepsPerFrame = 4;
    private PluginApplication? _application;
    private FrameDispatcher? _dispatcher;
    private float _accumulator;
    private bool _closed;

    public Task StartAsync(PluginApplication application)
    {
        if (_application != null || _closed)
            throw new InvalidOperationException("Subsystem can only start one application.");
        _application = application ?? throw new ArgumentNullException(nameof(application));
        return _application.StartAsync(this);
    }

    public Task StopAsync() => _application?.StopAsync() ?? Task.CompletedTask;

    public void Bind(FrameDispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        if (_closed || _dispatcher != null)
            throw new InvalidOperationException("Unreal frame loop cannot be bound.");
        _dispatcher = dispatcher;
        _accumulator = 0;
    }

    public void Unbind()
    {
        _dispatcher = null;
        _accumulator = 0;
    }

    public override void Tick(float deltaSeconds)
    {
        base.Tick(deltaSeconds);
        var dispatcher = _dispatcher;
        if (dispatcher == null)
            return;

        var elapsed = Math.Clamp(deltaSeconds, 0f, FixedStep * MaxFixedStepsPerFrame);
        _accumulator += elapsed;
        var steps = 0;
        while (_accumulator >= FixedStep && steps < MaxFixedStepsPerFrame)
        {
            dispatcher.DispatchFixed();
            _accumulator -= FixedStep;
            steps++;
        }
        if (steps == MaxFixedStepsPerFrame && _accumulator >= FixedStep)
            _accumulator = 0;

        dispatcher.Dispatch();
        dispatcher.DispatchLate();
    }

    public override void Deinitialize()
    {
        _closed = true;
        Unbind();
        _ = StopAndObserveAsync();
        base.Deinitialize();
    }

    private async Task StopAndObserveAsync()
    {
        try { await StopAsync(); }
        catch (Exception error) { ObFLog.LogError(error.ToString()); }
    }
}