using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using ObFramework.Integrated.GAS.Attributes.Core;
using ObFramework.Integrated.GAS.GameplayAbility;
using ObFramework.Integrated.GAS.GameplayEffect;
using ObFramework.Integrated.GAS.GameplayEffect.Components;

using Tag = ObFramework.Integrated.GAS.GameplayTag.GameplayTag;
using Effect = ObFramework.Integrated.GAS.GameplayEffect.GameplayEffect;
using Ability = ObFramework.Integrated.GAS.GameplayAbility.GameplayAbility;

namespace ObFramework.Integrated.GAS.Config
{
    public sealed class GASConfigLibrary
    {
        public delegate Func<AAbilityBehaviour> BehaviorBuilder(JObject parameters, Func<int, Effect> getEffect);
        private readonly Dictionary<string, AttributeKey<float>> _attributes = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Func<JObject, AEffectComponent>> _componentFactories = new(StringComparer.Ordinal);
        private readonly Dictionary<string, BehaviorBuilder> _behaviorFactories = new(StringComparer.Ordinal);

        private Dictionary<int, Effect> _effects = new();
        private Dictionary<int, Ability> _abilities = new();

        private bool _loaded;

        public GASConfigLibrary()
        {
            RegisterBuiltInComponents();

            RegisterBehavior("ApplyEffect", (parameters, getEffect) =>
            {
                var effect = getEffect(Read<int>(parameters, "EffectId"));
                return () => new ApplyEffectBehavior(effect);
            });
        }

        // Attribute
        public void RegisterAttribute(AttributeKey<float> key)
        {
            EnsureNotLoaded();

            _attributes.Add(key.Name, key);
        }

        public AttributeKey<float> GetAttribute(string name) =>
            _attributes.TryGetValue(name, out var key) ? key : 
                throw new KeyNotFoundException($"GAS attribute '{name}' is not registered.");

        // Component
        public void RegisterComponent(string type, Func<JObject, AEffectComponent> factory)
        {
            EnsureNotLoaded();
            _componentFactories.Add(type, factory);
        }

        // Ability Behavior
        public void RegisterBehavior(string type, BehaviorBuilder builder)
        {
            EnsureNotLoaded();
            _behaviorFactories.Add(type, builder);
        }

        // Config
        public void Load(IEnumerable<EffectConfig> effects, IEnumerable<AbilityConfig> abilities)
        {
            EnsureNotLoaded();

            var effectMap = new Dictionary<int, Effect>();
            var abilityMap = new Dictionary<int, Ability>();

            // 先构建所有 Effect
            foreach (var config in effects)
            {
                ValidateId(config.Id);
                effectMap.Add(config.Id, BuildEffect(config));
            }

            // 再构建 Ability
            foreach (var config in abilities)
            {
                ValidateId(config.Id);
                abilityMap.Add(config.Id, BuildAbility(config, ResolveEffect));
            }

            // 全部构建成功才提交
            _effects = effectMap;
            _abilities = abilityMap;

            _loaded = true;
            return;

            Effect ResolveEffect(int id)
            {
                return effectMap.TryGetValue(id, out var effect) ? effect : 
                    throw new KeyNotFoundException($"GAS effect '{id}' does not exist.");
            }
        }

        public Effect GetEffect(int id) =>
            _effects.TryGetValue(id, out var effect) ? effect : 
                throw new KeyNotFoundException($"GAS effect '{id}' does not exist.");

        public Ability GetAbility(int id) => 
            _abilities.TryGetValue(id, out var ability) ? ability : 
                throw new KeyNotFoundException($"GAS ability '{id}' does not exist.");

        private Effect BuildEffect(EffectConfig config)
        {
            if (!Enum.IsDefined(typeof(EEffectDurationType), config.DurationType))
            {
                throw new ArgumentException($"Invalid duration type in effect {config.Id}.");
            }

            var components = new AEffectComponent[config.Components.Length];

            for (int i = 0; i < components.Length; i++)
            {
                var data = config.Components[i];
                string type = Read<string>(data, "Type");

                if (!_componentFactories.TryGetValue(type, out var factory))
                {
                    throw new InvalidOperationException($"Unknown component '{type}' in effect {config.Id}.");
                }

                components[i] = factory(data);
            }

            return new Effect(
                config.DurationType,
                components,
                config.DurationTicks,
                config.PeriodTicks,
                config.ExecuteOnApplication,
                config.MaxStacks,
                ToTags(config.EffectTags));
        }

        private Ability BuildAbility(AbilityConfig config, Func<int, Effect> getEffect)
        {
            if (!_behaviorFactories.TryGetValue(config.Behavior, out var builder))
            {
                throw new InvalidOperationException($"Unknown behavior '{config.Behavior}' in ability {config.Id}.");
            }

            var commitEffect = config.CommitEffectId.HasValue ? getEffect(config.CommitEffectId.Value) : null;
            var factory = builder(config.Parameters, getEffect);

            return new Ability(
                factory,
                commitEffect,
                ToTags(config.AbilityTags),
                ToTags(config.RequiredTags),
                ToTags(config.BlockedTags),
                ToTags(config.BlockAbilitiesWithTags));
        }

        private void EnsureNotLoaded()
        {
            if (_loaded)
                throw new InvalidOperationException("GAS configuration is already loaded.");
        }

        private static void ValidateId(int id)
        {
            if (id <= 0)
                throw new ArgumentOutOfRangeException(nameof(id));
        }

        private static Tag[] ToTags(string[] names)
        {
            var tags = new Tag[names.Length];

            for (int i = 0; i < names.Length; i++)
                tags[i] = new Tag(names[i]);

            return tags;
        }

        private static T Read<T>(JObject data, string name)
        {
            var token = data[name];

            if (token == null || token.Type == JTokenType.Null)
                throw new ArgumentException($"Missing GAS parameter '{name}'.");

            var value = token.ToObject<T>();

            return value ?? throw new ArgumentException($"Invalid GAS parameter '{name}'.");
        }

        private static T ReadOrDefault<T>(JObject data, string name, T fallback) 
            => data[name] == null ? fallback : Read<T>(data, name);

        private static float ReadFloat(JObject data, string name)
        {
            float value = Read<float>(data, name);

            if (float.IsNaN(value) || float.IsInfinity(value))
                throw new ArgumentException($"Invalid GAS number '{name}'.");

            return value;
        }

        private void RegisterBuiltInComponents()
        {
            RegisterComponent("Base", data =>
                new BaseComponent(
                    GetAttribute(Read<string>(data, "Attribute")),
                    ReadFloat(data, "Value"),
                    ReadOrDefault(data, "Periodic", false)));

            RegisterComponent("Slot", data =>
            {
                int slot = Read<int>(data, "Slot");

                if (slot < 0)
                    throw new ArgumentOutOfRangeException("Slot");

                return new SlotComponent(
                    GetAttribute(Read<string>(data, "Attribute")),
                    slot,
                    ReadFloat(data, "Value"));
            });

            RegisterComponent("Tag", data =>
                new TagComponent(new Tag(Read<string>(data, "Tag"))));

            RegisterComponent("Cost", data =>
                new CostComponent(
                    GetAttribute(Read<string>(data, "Attribute")),
                    ReadFloat(data, "Value")));

            RegisterComponent("TagRequirement", data =>
                new TagRequirementComponent(
                    ToTags(ReadOrDefault(
                        data, "Required", Array.Empty<string>())),
                    ToTags(ReadOrDefault(
                        data, "Blocked", Array.Empty<string>()))));

            RegisterComponent("Immunity", data =>
                new ImmunityComponent(new Tag(Read<string>(data, "Tag"))));

            RegisterComponent("CancelAbility", data =>
                new CancelAbilityComponent(new Tag(Read<string>(data, "Tag"))));

            RegisterComponent("Cue", data =>
            {
                var trigger = ReadOrDefault(data, "Trigger", ECueTrigger.OnApply);

                if (!Enum.IsDefined(typeof(ECueTrigger), trigger))
                    throw new ArgumentException("Invalid cue trigger.");

                return new CueComponent(new Tag(Read<string>(data, "Tag")), trigger);
            });
        }
        
        internal void Unload()
        {
            _effects.Clear();
            _abilities.Clear();

            _loaded = false;
        }
    }
}