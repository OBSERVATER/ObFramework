using ObFramework.Integrated.GAS;
using ObFramework.Integrated.GAS.Attributes.Core;
using ObFramework.Integrated.GAS.GameplayAbility;
using ObFramework.Integrated.GAS.GameplayEffect;
using ObFramework.Integrated.GAS.GameplayEffect.Components;
using ObFramework.Integrated.GAS.GameplayTag;
using Tag = ObFramework.Integrated.GAS.GameplayTag.GameplayTag;

namespace ObFramework.Test.Core.Plugins.GAS;

[TestFixture]
public class AbilityTest
{
    private static readonly AttributeKey<float> Health = new("Health");
    private static readonly AttributeKey<float> Mana = new("Mana");

    public class Enemy
    {
        public AbilitySystem Gas { get; } = new();

        public Enemy(float health)
        {
            Gas.Attributes.Register(Health, new Attribute<FormulaFloat, float>(health, default));
        }
    }

    public sealed class AttackBehavior(GameplayEffect damage) : AAbilityBehaviour
    {
        public override void OnActivate(ActiveAbility ability)
        {
            if (!ability.TryCommit())
            {
                ability.Cancel();
                return;
            }

            ability.Target.Effects.TryApply(damage, ability.Owner, out _);

            ability.End();
        }
    }

    public sealed class ChargeBehavior : AAbilityBehaviour
    {
        public override void OnActivate(ActiveAbility ability)
        {
            //charge
        }
    }
    public sealed class ChargedAttackBehavior(GameplayEffect damage, int chargeTicks) : AAbilityBehaviour
    {
        public override void OnActivate(ActiveAbility ability)
        {
        }

        public override void OnTick(ActiveAbility ability)
        {
            if (ability.ElapsedTicks < chargeTicks)
                return;

            if (!ability.TryCommit())
            {
                ability.Cancel();
                return;
            }

            ability.Target.Effects.TryApply(damage, ability.Owner, out _);
            ability.End();
        }
    }
    [Test]
    public void AttackAbilityTest()
    {
        var player = new Enemy(100f);
        var enemy = new Enemy(100f);

        var damage = new GameplayEffect(
            EEffectDurationType.Instant,
            [new BaseComponent(Health, -20f)]
        );

        var attack = new GameplayAbility(() => new AttackBehavior(damage));

        var abilities = player.Gas.Abilities;
        var health = enemy.Gas.Attributes.Get(Health);

        Assert.That(abilities.TryActivate(attack, enemy.Gas, out _), Is.False);

        Assert.That(health.Value, Is.EqualTo(100f));

        Assert.That(abilities.Grant(attack), Is.True);

        Assert.That(abilities.TryActivate(attack, enemy.Gas, out var active), Is.True);
        Assert.That(health.Value, Is.EqualTo(80f));

        Assert.That(active, Is.Not.Null);
        Assert.That(active!.IsActive, Is.False);
        Assert.That(abilities.IsActive(attack), Is.False);
        Assert.That(abilities.TryActivate(attack, enemy.Gas, out _), Is.True);
        Assert.That(health.Value, Is.EqualTo(60f));
        abilities.Revoke(attack);
        Assert.That(abilities.TryActivate(attack, enemy.Gas, out _), Is.False);
    }
    [Test]
    public void ChargeAbilityTest()
    {
        var player = new Enemy(100f);
        var abilities = player.Gas.Abilities;

        var charge = new GameplayAbility(() => new ChargeBehavior());

        abilities.Grant(charge);

        Assert.That(abilities.TryActivate(charge, player.Gas, out var active), Is.True);
        Assert.That(active!.IsActive, Is.True);

        Assert.That(abilities.TryActivate(charge, player.Gas, out _), Is.False);

        active.Cancel();

        Assert.That(active.IsActive, Is.False);
        Assert.That(abilities.TryActivate(charge, player.Gas, out _), Is.True);
        abilities.Revoke(charge);
        Assert.That(abilities.IsActive(charge), Is.False);
        Assert.That(abilities.Has(charge), Is.False);
    }
    [Test]
    public void AbilityCommitTest()
    {
        var player = new Enemy(100f);
        var enemy = new Enemy(100f);

        player.Gas.Attributes.Register(Mana, new Attribute<FormulaFloat, float>(50f, default));
        var cooldownTag = new GameplayTag("Cooldown.Attack");

        var commit = new GameplayEffect(
            EEffectDurationType.Duration,
            [
                new CostComponent(Mana, 20f),
                new TagComponent(cooldownTag),
                new TagRequirementComponent(blocked: [cooldownTag])
            ],
            durationTicks: 5
        );

        var damage = new GameplayEffect(
            EEffectDurationType.Instant,
            [new BaseComponent(Health, -30f)]
        );

        var attack = new GameplayAbility(() => new AttackBehavior(damage),
            commitEffect: commit
        );

        var abilities = player.Gas.Abilities;
        var mana = player.Gas.Attributes.Get(Mana);
        var health = enemy.Gas.Attributes.Get(Health);

        abilities.Grant(attack);

        Assert.That(abilities.TryActivate(attack, enemy.Gas, out _), Is.True);

        Assert.That(mana.Value, Is.EqualTo(30f));
        Assert.That(health.Value, Is.EqualTo(70f));
        Assert.That(player.Gas.Tags.Has(cooldownTag), Is.True);

        Assert.That(abilities.TryActivate(attack, enemy.Gas, out _), Is.False);

        Assert.That(mana.Value, Is.EqualTo(30f));
        Assert.That(health.Value, Is.EqualTo(70f));

        for (var i = 0; i < 5; i++)
            player.Gas.Effects.Tick();

        Assert.That(player.Gas.Tags.Has(cooldownTag), Is.False);

        Assert.That(abilities.TryActivate(attack, enemy.Gas, out _), Is.True);

        Assert.That(mana.Value, Is.EqualTo(10f));
        Assert.That(health.Value, Is.EqualTo(40f));

        for (var i = 0; i < 5; i++)
            player.Gas.Effects.Tick();

        Assert.That(abilities.TryActivate(attack, enemy.Gas, out _), Is.False);

        Assert.That(mana.Value, Is.EqualTo(10f));
        Assert.That(health.Value, Is.EqualTo(40f));
        Assert.That(player.Gas.Tags.Has(cooldownTag), Is.False);
    }
    [Test]
    public void StunCancelAbilityTest()
    {
        var player = new Enemy(100f);
        var enemy = new Enemy(100f);

        var attackTag = new Tag("Ability.Attack");
        var ccTag = new Tag("State.CC");
        var stunnedTag = new Tag("State.CC.Stunned");

        var charge = new GameplayAbility(
            () => new ChargeBehavior(),
            abilityTags: [new Tag("Ability.Attack.Charge")],
            blockedTags: [ccTag]
        );

        var stun = new GameplayEffect(
            EEffectDurationType.Duration,
            [
                new TagComponent(stunnedTag),
                new CancelAbilityComponent(attackTag)
            ],
            durationTicks: 5
        );

        var gas = player.Gas;
        var abilities = gas.Abilities;

        abilities.Grant(charge);

        Assert.That(abilities.TryActivate(charge, enemy.Gas, out var active), Is.True);

        Assert.That(active!.IsActive, Is.True);

        Assert.That(gas.Effects.TryApply(stun, enemy.Gas, out _), Is.True);

        Assert.That(active.IsActive, Is.False);
        Assert.That(abilities.IsActive(charge), Is.False);
        Assert.That(gas.Tags.Has(stunnedTag), Is.True);

        Assert.That(abilities.TryActivate(charge, enemy.Gas, out _), Is.False);

        for (var i = 0; i < 5; i++)
            gas.Effects.Tick();

        Assert.That(gas.Tags.Has(stunnedTag), Is.False);
        Assert.That(abilities.TryActivate(charge, enemy.Gas, out _), Is.True);
    }
    [Test]
    public void AbilityBlockTest()
    {
        var player = new Enemy(100f);
        var enemy = new Enemy(100f);

        var normalTag = new Tag("Ability.Attack.Normal");

        var normal = new GameplayAbility(
            () => new ChargeBehavior(),
            abilityTags: [normalTag]
        );

        var heavy = new GameplayAbility(
            () => new ChargeBehavior(),
            abilityTags: [new Tag("Ability.Attack.Heavy")],
            blockAbilitiesWithTags: [normalTag]
        );

        var abilities = player.Gas.Abilities;

        abilities.Grant(normal);
        abilities.Grant(heavy);

        Assert.That(abilities.TryActivate(heavy, enemy.Gas, out var active), Is.True);

        Assert.That(abilities.TryActivate(normal, enemy.Gas, out _), Is.False);

        active!.End();
        Assert.That(abilities.TryActivate(normal, enemy.Gas, out _), Is.True);
    }
    [Test]
    public void ChargedAttackTest()
    {
        var player = new Enemy(100f);
        var enemy = new Enemy(100f);

        var damage = new GameplayEffect(EEffectDurationType.Instant, [new BaseComponent(Health, -30f)]);
        var charge = new GameplayAbility(() => new ChargedAttackBehavior(damage, 3));

        var abilities = player.Gas.Abilities;
        var health = enemy.Gas.Attributes.Get(Health);

        abilities.Grant(charge);

        Assert.That(abilities.TryActivate(charge, enemy.Gas, out var active), Is.True);

        Assert.That(active!.IsActive, Is.True);
        Assert.That(active.ElapsedTicks, Is.Zero);
        Assert.That(health.Value, Is.EqualTo(100f));

        abilities.Tick();

        Assert.That(active.ElapsedTicks, Is.EqualTo(1));
        Assert.That(health.Value, Is.EqualTo(100f));

        abilities.Tick();

        Assert.That(active.ElapsedTicks, Is.EqualTo(2));
        Assert.That(health.Value, Is.EqualTo(100f));

        abilities.Tick();

        Assert.That(active.ElapsedTicks, Is.EqualTo(3));
        Assert.That(health.Value, Is.EqualTo(70f));
        Assert.That(active.IsCommitted, Is.True);
        Assert.That(active.IsActive, Is.False);

        abilities.Tick();

        Assert.That(health.Value, Is.EqualTo(70f));
        Assert.That(active.ElapsedTicks, Is.EqualTo(3));
    }
}