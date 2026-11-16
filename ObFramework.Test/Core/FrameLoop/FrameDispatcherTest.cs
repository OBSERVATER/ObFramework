using ObFramework.Integrated.Clock;

namespace ObFramework.Test.Core.FrameLoop;

[TestFixture]
public class FrameDispatcherTest
{
    private sealed class Tickable : ITickable, IFixedTickable, ILateTickable
    {
        public Action? OnTick;
        public int Calls;
        public void Tick() { Calls++; OnTick?.Invoke(); }
        public void FixedTick() => Tick();
        public void LateTick() => Tick();
    }

    [Test]
    public void RegistrationDuringUpdateCanRunInLatePhase()
    {
        var frame = new FrameDispatcher();
        var added = new Tickable();
        var spawner = new Tickable();
        spawner.OnTick = () =>
        {
            frame.UnRegister(spawner);
            frame.Register(added);
        };
        frame.Register(spawner);

        frame.Dispatch();
        Assert.That(added.Calls, Is.Zero);
        frame.DispatchLate();
        Assert.That(added.Calls, Is.EqualTo(1));
    }

    [Test]
    public void StopGateLetsCurrentCallbackFinishAndSkipsRemainingCallbacks()
    {
        var frame = new FrameDispatcher();
        var running = true;
        var completed = 0;
        void Stop()
        {
            running = false;
            completed++;
        }
        frame.Register(new Tickable { OnTick = Stop });
        frame.Register(new Tickable { OnTick = Stop });

        frame.Dispatch(() => running);
        Assert.That(completed, Is.EqualTo(1));
    }

    [Test]
    public void StructTickableCanStillBeUnregistered()
    {
        var frame = new FrameDispatcher();
        var calls = 0;
        var tickable = new StructTickable(() => calls++);
        frame.Register(tickable);
        frame.Dispatch();
        frame.UnRegister(tickable);
        frame.Dispatch();
        Assert.That(calls, Is.EqualTo(1));
    }

    private readonly struct StructTickable : ITickable
    {
        private readonly Action _tick;
        public StructTickable(Action tick) => _tick = tick;
        public void Tick() => _tick();
    }
}
