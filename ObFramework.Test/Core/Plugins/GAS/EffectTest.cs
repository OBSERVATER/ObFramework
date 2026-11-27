using ObFramework.Integrated.GAS;
using ObFramework.Integrated.GAS.Attributes.Core;
using ObFramework.Integrated.GAS.GameplayEffect;
using ObFramework.Integrated.GAS.GameplayEffect.Components;
using ObFramework.Integrated.GAS.GameplayTag;

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

    [Test]
    public void InstantComponentTest()
    {
        var player = new Enemy(100f);
        var enemy = new Enemy(100f);

        var damage = new GameplayEffect(
            EEffectDurationType.Instant,
            [
                new BaseComponent(Health, -20f)
            ]
        );

        var active = enemy.Gas.Effects.Apply(damage, player.Gas);

        Assert.That(active, Is.Null);
        Assert.That(enemy.Gas.Attributes.Get(Health).Value, Is.EqualTo(80f));
    }
    [Test]
    public void PeriodicComponentsTest()
    {
        var player = new Enemy(100f);
        var enemy = new Enemy(100f);

        var poisoned = new GameplayTag("State.Poisoned");

        var poison = new GameplayEffect(
            EEffectDurationType.Duration,
            [
                new BaseComponent(Health, -10f, periodic: true),
                new TagComponent(poisoned)
            ],
            durationTicks: 6,
            periodTicks: 2,
            maxStacks: 3
        );

        var manager = enemy.Gas.Effects;
        var health = enemy.Gas.Attributes.Get(Health);
        var tags = enemy.Gas.Tags;

        var active = manager.Apply(poison, player.Gas)!;

        Assert.That(tags.Has(poisoned), Is.True);
        Assert.That(health.Value, Is.EqualTo(100f));

        manager.Apply(poison, player.Gas);

        Assert.That(active.Stacks, Is.EqualTo(2));
        Assert.That(tags.GetCount(poisoned), Is.EqualTo(1));

        for (var i = 0; i < 6; i++)
            manager.Tick();

        Assert.That(health.Value, Is.EqualTo(40f));
        Assert.That(tags.Has(poisoned), Is.False);
        Assert.That(active.Stacks, Is.Zero);

        manager.Tick();
        manager.Tick();

        Assert.That(health.Value, Is.EqualTo(40f));
    }
}