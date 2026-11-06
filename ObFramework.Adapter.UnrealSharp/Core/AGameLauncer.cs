using UnrealSharp.Attributes;
using UnrealSharp.Engine;
using UnrealSharp.Log;

namespace ObFramework.Adapter.UnrealSharp.Core;

[UClass(ClassFlags.Abstract)]
public partial class AGameLauncher : AActor
{
    private CancellationTokenSource? _launchCts;

    protected CancellationToken LaunchToken { get; private set; }

    public override void BeginPlay()
    {
        base.BeginPlay();

        _launchCts = new CancellationTokenSource();
        LaunchToken = _launchCts.Token;

        _ = RunLaunchAsync(_launchCts);
    }

    public override void EndPlay(EEndPlayReason endPlayReason)
    {
        try
        {
            _launchCts?.Cancel();
        }
        finally
        {
            base.EndPlay(endPlayReason);
        }
    }

    private async Task RunLaunchAsync(CancellationTokenSource cts)
    {
        try
        {
            await LaunchAsync();
        }
        catch (OperationCanceledException)when (cts.IsCancellationRequested)
        {
        }
        catch (Exception e)
        {
            UnrealLogger.LogError("ObFramework", e.ToString());
        }
        finally
        {
            if (ReferenceEquals(_launchCts, cts))
                _launchCts = null;

            cts.Dispose();
        }
    }

    protected virtual async Task LaunchAsync()
    {
        LaunchToken.ThrowIfCancellationRequested();

        await InitResourcesAsync();
        LaunchToken.ThrowIfCancellationRequested();

        await InitConfigAsync();
        LaunchToken.ThrowIfCancellationRequested();

        await InitGameSystemsAsync();
        LaunchToken.ThrowIfCancellationRequested();

        await EnterGameAsync();
        LaunchToken.ThrowIfCancellationRequested();
    }

    protected virtual Task InitResourcesAsync()
        => throw new NotImplementedException(
            $"{GetType().Name} must override InitResourcesAsync.");

    protected virtual Task InitConfigAsync()
        => throw new NotImplementedException($"{GetType().Name} must override InitConfigAsync.");

    protected virtual Task InitGameSystemsAsync()
        => throw new NotImplementedException($"{GetType().Name} must override InitGameSystemsAsync.");

    protected virtual Task EnterGameAsync()
        => throw new NotImplementedException($"{GetType().Name} must override EnterGameAsync.");
}
