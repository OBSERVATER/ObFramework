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

        enemy.Gas.Effects.TryApply(damage, player.Gas, out var active);

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

        manager.TryApply(poison, player.Gas, out var active);

        Assert.That(tags.Has(poisoned), Is.True);
        Assert.That(health.Value, Is.EqualTo(100f));

        manager.TryApply(poison, player.Gas, out active);

        Assert.That(active!.Stacks, Is.EqualTo(2));
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
    [Test]
    public void TagRequirementTest()
    {
        var enemy = new Enemy(50f);

        var dead = new GameplayTag("State.Dead");

        var heal = new GameplayEffect(EEffectDurationType.Instant,
            [
                new BaseComponent(Health, 20f),
                new TagRequirementComponent(blocked: [dead])
            ]
        );

        var gas = enemy.Gas;
        var health = gas.Attributes.Get(Health);

        gas.Tags.Add(dead);

        Assert.That(gas.Effects.TryApply(heal, gas, out _), Is.False);
        Assert.That(health.Value, Is.EqualTo(50f));
        gas.Tags.Remove(dead);
        Assert.That(gas.Effects.TryApply(heal, gas, out _), Is.True);
        Assert.That(health.Value, Is.EqualTo(70f));
    }
    [Test]
    public void ImmunityTest()
    {
        var player = new Enemy(100f);
        var enemy = new Enemy(100f);

        var damageTag = new GameplayTag("Effect.Damage");
        var fireTag = new GameplayTag("Effect.Damage.Fire");

        var damage = new GameplayEffect(
            EEffectDurationType.Instant,
            [new BaseComponent(Health, -20f)],
            effectTags: [fireTag]
        );

        var immunity = new GameplayEffect(
            EEffectDurationType.Duration,
            [new ImmunityComponent(damageTag)],
            durationTicks: 3
        );

        var gas = enemy.Gas;
        var manager = gas.Effects;
        var health = gas.Attributes.Get(Health);

        // 自身获得无敌效果
        manager.TryApply(immunity, gas,out _);

        // 无敌期间拒绝伤害
        Assert.That(manager.TryApply(damage, player.Gas, out _), Is.False);
        Assert.That(health.Value, Is.EqualTo(100f));

        // 无敌效果到期
        manager.Tick();
        manager.Tick();
        manager.Tick();

        // 恢复正常受伤
        Assert.That(manager.TryApply(damage, player.Gas, out _), Is.True);

        Assert.That(health.Value, Is.EqualTo(80f));
    }
}