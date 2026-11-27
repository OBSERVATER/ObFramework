using ObFramework.Integrated.GAS;
using ObFramework.Integrated.GAS.GameplayCue;
using ObFramework.Integrated.GAS.GameplayEffect;
using ObFramework.Integrated.GAS.GameplayEffect.Components;
using ObFramework.Integrated.GAS.GameplayTag;

namespace ObFramework.Test.Core.Plugins.GAS;

[TestFixture]
public class CueTest
{
    [Test]
    public void InstantCueTest()
    {
        var source = new AbilitySystem();
        var target = new AbilitySystem();

        var hit = new GameplayTag("GameplayCue.Hit");

        var events = new List<CueEvent>();
        target.Cues.Emitted += events.Add;

        var effect = new GameplayEffect(EEffectDurationType.Instant, [new CueComponent(hit)]);

        Assert.That(target.Effects.TryApply(effect, source, out var active), Is.True);

        Assert.That(active, Is.Null);
        Assert.That(events, Has.Count.EqualTo(1));

        var cue = events[0];

        Assert.That(cue.Tag, Is.EqualTo(hit));
        Assert.That(cue.Type, Is.EqualTo(ECueEventType.Executed));
        Assert.That(cue.Source, Is.SameAs(source));
        Assert.That(cue.Target, Is.SameAs(target));
    }

    [Test]
    public void PersistentCueTest()
    {
        var source = new AbilitySystem();
        var target = new AbilitySystem();

        var burning = new GameplayTag("GameplayCue.Burning");

        var events = new List<CueEvent>();
        target.Cues.Emitted += events.Add;

        var effect = new GameplayEffect(
            EEffectDurationType.Duration, 
            [new CueComponent(burning, ECueTrigger.Persistent)],
            durationTicks: 5
        );

        var manager = target.Effects;

        Assert.That(manager.TryApply(effect, source, out var first), Is.True);
        Assert.That(manager.TryApply(effect, source, out var second), Is.True);
        Assert.That(first, Is.Not.SameAs(second));

        // 两个效果只产生一次 Added
        Assert.That(events, Has.Count.EqualTo(1));
        Assert.That(events[0].Type, Is.EqualTo(ECueEventType.Added));

        manager.Remove(first!);

        // 仍然存在一个效果，不发送 Removed
        Assert.That(events, Has.Count.EqualTo(1));

        manager.Remove(second!);

        // 最后一个效果被移除
        Assert.That(events, Has.Count.EqualTo(2));
        Assert.That(events[1].Type, Is.EqualTo(ECueEventType.Removed));
    }

    [Test]
    public void PeriodicCueTest()
    {
        var target = new AbilitySystem();

        var events = new List<CueEvent>();
        target.Cues.Emitted += events.Add;

        var effect = new GameplayEffect(
            EEffectDurationType.Duration,
            [new CueComponent(new GameplayTag("GameplayCue.Poison"), ECueTrigger.Periodic)],
            durationTicks: 6,
            periodTicks: 2
        );

        var manager = target.Effects;

        Assert.That(manager.TryApply(effect, target, out _), Is.True);

        Assert.That(events, Is.Empty);

        for (var i = 0; i < 8; i++)
            manager.Tick();

        Assert.That(events, Has.Count.EqualTo(3));

        foreach (var cue in events)
        {
            Assert.That(cue.Type, Is.EqualTo(ECueEventType.Executed));
        }
    }

    [Test]
    public void DirectCueTest()
    {
        var source = new AbilitySystem();
        var target = new AbilitySystem();

        var tag = new GameplayTag("GameplayCue.Hit");

        var events = new List<CueEvent>();
        target.Cues.Emitted += events.Add;

        target.Cues.Execute(tag, source, 20f);

        Assert.That(events, Has.Count.EqualTo(1));
        Assert.That(events[0].Tag, Is.EqualTo(tag));
        Assert.That(events[0].Magnitude, Is.EqualTo(20f));
        Assert.That(events[0].Source, Is.SameAs(source));
        Assert.That(events[0].Target, Is.SameAs(target));
    }

    [Test]
    public void PersistentCueExpirationTest()
    {
        var target = new AbilitySystem();

        var events = new List<CueEvent>();
        target.Cues.Emitted += events.Add;

        var effect = new GameplayEffect(
            EEffectDurationType.Duration,
            [new CueComponent(new GameplayTag("GameplayCue.Burning"), ECueTrigger.Persistent)],
            durationTicks: 3
        );

        var manager = target.Effects;

        Assert.That(manager.TryApply(effect, target, out _), Is.True);

        Assert.That(events, Has.Count.EqualTo(1));
        Assert.That(events[0].Type, Is.EqualTo(ECueEventType.Added));

        manager.Tick();
        manager.Tick();

        Assert.That(events, Has.Count.EqualTo(1));

        manager.Tick();

        Assert.That(events, Has.Count.EqualTo(2));
        Assert.That(events[1].Type, Is.EqualTo(ECueEventType.Removed));
    }
}