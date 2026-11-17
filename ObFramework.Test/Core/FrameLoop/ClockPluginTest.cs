using ObFramework.Integrated.Clock;
namespace ObFramework.Test.Core.FrameLoop;
[TestFixture]
public class ClockPluginTest
{
    private sealed class Callback(Action action) : ILogicTickable, ITickable
    {
        public void LogicTick() => action();
        public void Tick() => action();
    }
    [TestCase(false)]
    [TestCase(true)]
    public void ExitFinishesCurrentCallbackAndSkipsRemainingWork(bool fromTimer)
    {
        var clock = new ClockPlugin(1);
        var running = true;
        var calls = 0;
        var stop = () => { running = false; calls++; };
        if (fromTimer) clock.Schedule(1, stop);
        else clock.RegisterTickable(new Callback(stop));
        clock.RegisterTickable(new Callback(() => { if (!running) Assert.Fail("Callback after exit"); }));
        clock.Schedule(1, () => Assert.Fail("Timer after exit"));
        clock.DispatchUpdate(5, () => running);
        Assert.That(calls, Is.EqualTo(1));
        Assert.That(clock.Tick, Is.EqualTo(1));
    }
    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public void RejectsNestedAdvancementAndRecovers(bool outerNext, bool innerNext)
    {
        var clock = new ClockPlugin(1);
        clock.Schedule(1, () => { if (innerNext) clock.Next(1); else clock.Advance(1); });
        Assert.Throws<InvalidOperationException>(() => { if (outerNext) clock.Next(1); else clock.Advance(1); });
        clock.Advance(1);
        Assert.That(clock.Tick, Is.EqualTo(2));
    }
    [Test]
    public void ClosedGateDoesNotAdvance()
    {
        var clock = new ClockPlugin(1);
        clock.DispatchUpdate(5, () => false);
        Assert.That(clock.Tick, Is.Zero);
    }
    [Test]
    public void AccumulatedTimeAndDirectTicksAgree()
    {
        var clock = new ClockPlugin(0.5f);
        var calls = 0;
        clock.Repeat(2, () => calls++);
        clock.Next(0.25f);
        clock.Next(1.75f);
        clock.Advance(2);
        Assert.That(clock.Tick, Is.EqualTo(6));
        Assert.That(calls, Is.EqualTo(3));
    }
    [TestCase(0f)]
    [TestCase(-1f)]
    [TestCase(float.NaN)]
    [TestCase(float.PositiveInfinity)]
    public void RejectsInvalidInterval(float interval) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new ClockPlugin(interval));
}
