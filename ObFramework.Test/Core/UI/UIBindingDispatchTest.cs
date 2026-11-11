using ObFramework.Integrated.UI;

namespace ObFramework.Test.Core.UI;

[TestFixture]
public class UIBindingDispatchTest
{
    [TearDown]
    public void ResetDispatch() => UIBindingDispatch.ConfigureMainThread(-1, null);

    [Test]
    public void OnTheMainThreadDispatchRunsInlineWithoutScheduling()
    {
        var scheduled = 0;
        UIBindingDispatch.ConfigureMainThread(Environment.CurrentManagedThreadId, _ => scheduled++);
        var ran = false;

        UIBindingDispatch.Post(() => ran = true);

        Assert.That(ran, Is.True);
        Assert.That(scheduled, Is.Zero);
    }

    [Test]
    public void BackgroundNotificationsAreQueuedUntilTheMainThreadPumps()
    {
        var queue = new Queue<Action>();
        // Simulate "this thread is not the UI main thread": a configured id that cannot match.
        UIBindingDispatch.ConfigureMainThread(Environment.CurrentManagedThreadId + 1_000_000, queue.Enqueue);
        var runs = 0;

        Assert.That(UIBindingDispatch.IsMainThread, Is.False);
        UIBindingDispatch.Post(() => runs++);

        Assert.That(runs, Is.Zero, "background updates must not touch native state inline");
        Assert.That(queue.Count, Is.EqualTo(1));

        while (queue.Count > 0)
            queue.Dequeue()();

        Assert.That(runs, Is.EqualTo(1));
    }

    [Test]
    public void UnconfiguredDispatchFallsBackToInlineInvocation()
    {
        UIBindingDispatch.ConfigureMainThread(-1, null);
        var ran = false;

        UIBindingDispatch.Post(() => ran = true);

        Assert.That(ran, Is.True);
        Assert.That(UIBindingDispatch.IsMainThread, Is.True);
    }

    [Test]
    public void NullPostCallbackRunsInlineEvenOffTheMainThread()
    {
        UIBindingDispatch.ConfigureMainThread(-2, null);
        var ran = false;

        Task.Run(() => UIBindingDispatch.Post(() => ran = true)).GetAwaiter().GetResult();

        Assert.That(ran, Is.True);
    }
}
