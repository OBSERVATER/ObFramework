using System.Text;
using ObFramework.Core;
using ObFramework.Extension.Integrated;
using ObFramework.Integrated.Clock;
using ObFramework.Integrated.Config;
using ObFramework.Integrated.Resource;

using ObFramework.Integrated.GAS;
using ObFramework.Integrated.GAS.Config;
using ObFramework.Integrated.GAS.Attributes.Core;
using ObFramework.Integrated.GAS.GameplayCue;

using Tag = ObFramework.Integrated.GAS.GameplayTag.GameplayTag;

namespace ObFramework.Test.Core.Plugins.GAS;

[TestFixture]
public class GasIntegrationTest
{
    private static readonly AttributeKey<float> Health = new("Health");
    private static readonly AttributeKey<float> Mana = new("Mana");

    private sealed class Catalog : IConfigCatalog
    {
        public IReadOnlyList<ConfigEntry> Entries { get; } =
        [
            new(typeof(EffectConfig), "effects"),
            new(typeof(AbilityConfig), "abilities")
        ];
    }

    private sealed class TestAssetAdapter : IAssetAdapter<int>, IRawFileAdapter<int>
    {
        public readonly Dictionary<string, string> Files = new();

        private int _handle;

        public AssetLoadResult<T, int> Read<T>(AssetKey request) where T : class
        {
            throw new NotSupportedException();
        }

        public Task<AssetLoadResult<T, int>> ReadAsync<T>(AssetKey request, CancellationToken ct = default) where T : class
        {
            throw new NotSupportedException();
        }

        public Task<AssetLoadResult<byte[], int>> ReadRawFileAsync(string location)
        {
            var bytes = Encoding.UTF8.GetBytes(Files[location]);
            return Task.FromResult(new AssetLoadResult<byte[], int>(bytes, ++_handle));
        }

        public void Release(int handle)
        {
        }
    }

    [Test]
    public async Task ConfiguredCombatTest()
    {
        var adapter = new TestAssetAdapter
        {
            Files =
            {
                ["effects"] = """
                              [
                                  {
                                      "Id": 1001,
                                      "DurationType": "Instant",
                                      "EffectTags": ["Effect.Damage"],
                                      "Components": [
                                          {
                                              "Type": "Base",
                                              "Attribute": "Health",
                                              "Value": -30
                                          },
                                          {
                                              "Type": "Cue",
                                              "Tag": "GameplayCue.Hit"
                                          }
                                      ]
                                  },
                                  {
                                      "Id": 1002,
                                      "DurationType": "Duration",
                                      "DurationTicks": 5,
                                      "Components": [
                                          {
                                              "Type": "Cost",
                                              "Attribute": "Mana",
                                              "Value": 20
                                          },
                                          {
                                              "Type": "Tag",
                                              "Tag": "Cooldown.Attack"
                                          },
                                          {
                                              "Type": "TagRequirement",
                                              "Blocked": ["Cooldown.Attack"]
                                          }
                                      ]
                                  },
                                  {
                                      "Id": 1003,
                                      "DurationType": "Duration",
                                      "DurationTicks": 5,
                                      "Components": [
                                          {
                                              "Type": "Tag",
                                              "Tag": "State.CC.Stunned"
                                          },
                                          {
                                              "Type": "CancelAbility",
                                              "Tag": "Ability.Attack"
                                          }
                                      ]
                                  },
                                  {
                                      "Id": 1004,
                                      "DurationType": "Duration",
                                      "DurationTicks": 1,
                                      "Components": [
                                          {
                                              "Type": "Immunity",
                                              "Tag": "Effect.Damage"
                                          }
                                      ]
                                  }
                              ]
                              """,
                ["abilities"] = """
                                [
                                    {
                                        "Id": 2001,
                                        "Behavior": "ApplyEffect",
                                        "Parameters": {
                                            "EffectId": 1001
                                        },
                                        "CommitEffectId": 1002,
                                        "AbilityTags": ["Ability.Attack.Normal"],
                                        "BlockedTags": ["State.CC", "State.Dead"]
                                    }
                                ]
                                """
            }
        };

        var host = new PluginHost(
            new GASPlugin(
                loadConfig: true,
                attributes: [Health, Mana]),
            new ClockPlugin(0.1f),
            new ConfigPlugin(new Catalog()),
            ResourcePlugin.Create(adapter)
        );

        try
        {
            await host.StartAsync();

            var plugin = host.Context.Get<GASPlugin>();
            var clock = host.Context.Get<ClockPlugin>();

            using var player = new AbilitySystem();
            using var enemy = new AbilitySystem();

            player.Attributes.Register(Mana, new Attribute<FormulaFloat, float>(50f, default));
            enemy.Attributes.Register(Health, new Attribute<FormulaFloat, float>(100f, default));

            plugin.Register(player);
            plugin.Register(enemy);

            var events = new List<CueEvent>();
            enemy.Cues.Emitted += events.Add;
            var attack = plugin.GetAbility(2001);

            // 验证共享定义与 Key 映射
            Assert.That(plugin.GetAbility(2001), Is.SameAs(attack));
            Assert.That(plugin.GetAttribute("Health"), Is.SameAs(Health));
            player.Abilities.Grant(attack);

            using (Assert.EnterMultipleScope())
            {
                // 第一次攻击
                Assert.That(player.TryActivateAbility(attack, enemy, out _), Is.True);
                Assert.That(enemy.GetValue(Health), Is.EqualTo(70f));
                Assert.That(player.GetValue(Mana), Is.EqualTo(30f));
                Assert.That(events, Has.Count.EqualTo(1));
                Assert.That(events[0].Type, Is.EqualTo(ECueEventType.Executed));
                // 冷却期间不能释放
                Assert.That(player.TryActivateAbility(attack, enemy, out _), Is.False);
                // 施加眩晕
                Assert.That(enemy.TryApplyEffect(player, plugin.GetEffect(1003), out _), Is.True);
            }

            // 仅通过 Clock 推进时间
            clock.Advance(5);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(player.Tags.Has(new Tag("State.CC")), Is.False);
                Assert.That(player.Tags.Has(new Tag("Cooldown.Attack")), Is.False);
                // 目标获得伤害免疫
                Assert.That(enemy.TryApplyEffect(plugin.GetEffect(1004), out _), Is.True);
                Assert.That(player.TryApplyEffect(enemy, plugin.GetEffect(1001), out _), Is.False);
                Assert.That(enemy.GetValue(Health), Is.EqualTo(70f));
                Assert.That(events, Has.Count.EqualTo(1));
            }

            // 免疫到期
            clock.Advance(1);

            using (Assert.EnterMultipleScope())
            {
                // 第二次攻击
                Assert.That(player.TryActivateAbility(attack, enemy, out _), Is.True);
                Assert.That(enemy.GetValue(Health), Is.EqualTo(40f));
                Assert.That(player.GetValue(Mana), Is.EqualTo(10f));
                Assert.That(events, Has.Count.EqualTo(2));
            }

            clock.Advance(5);

            using (Assert.EnterMultipleScope())
            {
                // 法力不足
                Assert.That(player.TryActivateAbility(attack, enemy, out _), Is.False);
                Assert.That(enemy.GetValue(Health), Is.EqualTo(40f));
                Assert.That(player.GetValue(Mana), Is.EqualTo(10f));
                Assert.That(events, Has.Count.EqualTo(2));
            }

            // 清理
            plugin.UnRegister(player);
            plugin.UnRegister(enemy);

            player.Reset();
            enemy.Reset();

            Assert.That(player.Abilities.Has(attack), Is.False);
        }
        finally
        {
            await host.StopAsync();
        }
    }
}