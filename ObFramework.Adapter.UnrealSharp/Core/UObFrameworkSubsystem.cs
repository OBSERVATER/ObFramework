using ObFramework.Adapter.UnrealSharp.Diagnostics;
using ObFramework.Core;
using ObFramework.Core.FrameLoop;
using UnrealSharp.Attributes;
using UnrealSharp.UnrealSharpCore;

namespace ObFramework.Adapter.UnrealSharp.Core;

[UClass]
public partial class UObFrameworkSubsystem : UCSGameInstanceSubsystem, IFrameLoopDriver
{
    private const float FixedStep = 0.02f;
    private const int MaxFixedStepsPerFrame = 4;
    private FrameDispatcher? _dispatcher;
    private Func<bool>? _canContinue;
    private float _accumulator;
    private bool _started;
    private bool _closed;

    public PluginApplication? Application { get; private set; }
    internal bool IsStopping => _closed;

    public override void Initialize(FSubsystemCollectionBaseRef collection)
    {
        base.Initialize(collection);
        IsTickable = true;
    }

    public Task StartAsync(PluginApplication application)
    {
        ArgumentNullException.ThrowIfNull(application);
        return StartAsync(() => application);
    }

    internal Task StartAsync(Func<PluginApplication> createApplication)
    {
        if (_started || _closed)
            throw new InvalidOperationException("Subsystem can only start one application.");
        _started = true;
        var application = createApplication();
        if (_closed)
            return application.StopAsync();

        Application = application;
        Task startup;
        try { startup = application.StartAsync(this); }
        catch
        {
            Application = null;
            throw;
        }
        return ObserveStartupAsync(application, startup);
    }

    private async Task ObserveStartupAsync(PluginApplication application, Task startup)
    {
        try { await startup; }
        catch when (!_closed)
        {
            await ObserveCleanupAsync(application.StopAsync());
            throw;
        }
    }

    public Task StopAsync()
    {
        _closed = true;
        if (Application is { } application)
            return application.StopAsync();

        Unbind();
        return Task.CompletedTask;
    }

    public void Bind(FrameDispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        if (_closed || _dispatcher != null)
            throw new InvalidOperationException("Unreal frame loop cannot be bound.");
        _dispatcher = dispatcher;
        _canContinue = () => !_closed && ReferenceEquals(_dispatcher, dispatcher);
        _accumulator = 0;
    }

    public void Unbind()
    {
        _dispatcher = null;
        _canContinue = null;
        _accumulator = 0;
    }

    public override void Tick(float deltaSeconds)
    {
        base.Tick(deltaSeconds);
        var dispatcher = _dispatcher;
        var canContinue = _canContinue;
        if (_closed || dispatcher == null || canContinue == null)
            return;

        var elapsed = Math.Clamp(deltaSeconds, 0f, FixedStep * MaxFixedStepsPerFrame);
        _accumulator += elapsed;
        var steps = 0;
        while (_accumulator >= FixedStep && steps < MaxFixedStepsPerFrame)
        {
            _accumulator -= FixedStep;
            dispatcher.DispatchFixed(canContinue);
            if (!canContinue())
                return;
            steps++;
        }
        if (steps == MaxFixedStepsPerFrame && _accumulator >= FixedStep)
            _accumulator = 0;

        dispatcher.Dispatch(canContinue);
        if (canContinue())
            dispatcher.DispatchLate(canContinue);
    }

    public override void Deinitialize()
    {
        IsTickable = false;
        _ = ObserveCleanupAsync(StopAsync());
        base.Deinitialize();
    }

    private static async Task ObserveCleanupAsync(Task cleanup)
    {
        try { await cleanup; }
        catch (Exception error) { ObFLog.LogError(error.ToString()); }
    }
}
