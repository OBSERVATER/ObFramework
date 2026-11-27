using ObFramework.Integrated.GAS;
using ObFramework.Integrated.GAS.Attributes.Core;
using ObFramework.Integrated.GAS.GameplayEffect;

namespace ObFramework.Test.Core.Plugins.GAS;

[TestFixture]
public class EffectTest
{
    private static readonly AttributeKey<float> Health = new("Health");

    public class Enemy
    {
        public AbilitySystem Gas { get; } = new();

        public Enemy(float health)
        {
            Gas.Attributes.Register(Health, new Attribute<FormulaFloat, float>(health, default));
        }
    }

    public sealed class DamageEffect(float damage) : AGameplayEffect(EEffectDurationType.Instant)
    {
        public override void OnApply(EffectContext context)
        {
            var health = context.Target.Attributes.Get(Health);
            health.SetBase(health.Base - damage);
        }
    }
    public sealed class PoisonEffect(float damage) : AGameplayEffect(EEffectDurationType.Duration, durationTicks: 6, periodTicks: 2)
    {
        public override void OnApply(EffectContext context)
        {
        }

        public override void OnPeriodic(EffectContext context)
        {
            var health = context.Target.Attributes.Get(Health);
            health.SetBase(health.Base - damage);
        }
    }
    public sealed class StackingPoison(float damage) : AGameplayEffect(
            EEffectDurationType.Duration,
            durationTicks: 6,
            periodTicks: 2,
            maxStacks: 3)
    {
        public override void OnApply(EffectContext context)
        {
        }

        public override void OnPeriodic(EffectContext context, int stacks)
        {
            var health = context.Target.Attributes.Get(Health);
            health.SetBase(health.Base - damage * stacks);
        }
    }
    [Test]
    public void DamageEffectTest()
    {
        var player = new Enemy(100f);
        var enemy = new Enemy(100f);
        var effect = new DamageEffect(20f);
        var active = enemy.Gas.Effects.Apply(effect, player.Gas);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(enemy.Gas.Attributes.Get(Health).Value, Is.EqualTo(80f));
            Assert.That(player.Gas.Attributes.Get(Health).Value, Is.EqualTo(100f));
            Assert.That(active, Is.Null);
        }
    }
    [Test]
    public void PoisonEffectTest()
    {
        var player = new Enemy(100f);
        var enemy = new Enemy(100f);
        var effect = new PoisonEffect(10);
        var mgr = enemy.Gas.Effects;
        mgr.Apply(effect, player.Gas);
        int val = 100;
        for (int i = 0; i < 7; i++)
        {
            if (i != 0 && i % 2 == 0)
            {
                val -= 10;
            }
            Assert.That(enemy.Gas.Attributes.Get(Health).Value, Is.EqualTo(val));
            mgr.Tick();
        }
        mgr.Tick();
        mgr.Tick();
        Assert.That(enemy.Gas.Attributes.Get(Health).Value, Is.EqualTo(val));
    }
    [Test]
    public void StackingPoisonTest()
    {
        var player = new Enemy(100f);
        var enemy = new Enemy(100f);

        var poison = new StackingPoison(10f);

        var manager = enemy.Gas.Effects;
        var health = enemy.Gas.Attributes.Get(Health);

        var active = manager.Apply(poison, player.Gas)!;

        Assert.That(active.Stacks, Is.EqualTo(1));

        manager.Tick();
        var second = manager.Apply(poison, player.Gas);

        Assert.That(second, Is.SameAs(active));
        Assert.That(active.Stacks, Is.EqualTo(2));
        Assert.That(active.RemainingTicks, Is.EqualTo(6));

        // 周期没有被重置，因此此时扣除20点生命
        manager.Tick();

        Assert.That(health.Value, Is.EqualTo(80f));

        manager.Apply(poison, player.Gas);
        manager.Apply(poison, player.Gas);

        // 验证最大层数
        Assert.That(active.Stacks, Is.EqualTo(3));

        manager.Tick();
        manager.Tick();

        Assert.That(health.Value, Is.EqualTo(50f));

        // 主动移除
        manager.Remove(active);

        Assert.That(active.Stacks, Is.Zero);

        manager.Tick();
        manager.Tick();

        Assert.That(health.Value, Is.EqualTo(50f));
    }
}