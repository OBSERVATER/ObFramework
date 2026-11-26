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
}