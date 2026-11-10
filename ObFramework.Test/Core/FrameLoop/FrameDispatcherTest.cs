using ObFramework.Core.FrameLoop;

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

    [TestCase(nameof(FrameDispatcher.Dispatch))]
    [TestCase(nameof(FrameDispatcher.DispatchFixed))]
    [TestCase(nameof(FrameDispatcher.DispatchLate))]
    public void ChangesOnlyAffectEligibleRegistrations(string phase)
    {
        var frame = new FrameDispatcher();
        Action dispatch = phase switch
        {
            nameof(FrameDispatcher.Dispatch) => frame.Dispatch,
            nameof(FrameDispatcher.DispatchFixed) => frame.DispatchFixed,
            _ => frame.DispatchLate
        };
        var left = new Tickable();
        var right = new Tickable();
        var added = new Tickable();
        var completed = 0;
        void ChangeRegistrations(Tickable self, Tickable other)
        {
            frame.UnRegister(self);
            frame.UnRegister(other);
            other.OnTick = null;
            frame.Register(other);
            frame.Register(added);
            completed++;
        }

        left.OnTick = () => ChangeRegistrations(left, right);
        right.OnTick = () => ChangeRegistrations(right, left);
        frame.Register(left);
        frame.Register(right);

        dispatch();
        Assert.That(completed, Is.EqualTo(1));
        Assert.That(left.Calls + right.Calls, Is.EqualTo(1));
        Assert.That(added.Calls, Is.Zero);

        dispatch();
        Assert.That(completed, Is.EqualTo(1));
        Assert.That(left.Calls + right.Calls, Is.EqualTo(2));
        Assert.That(added.Calls, Is.EqualTo(1));
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
