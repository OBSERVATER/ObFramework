using System;
using System.Collections.Generic;
using Tag = ObFramework.Integrated.GAS.GameplayTag.GameplayTag;

namespace ObFramework.Integrated.GAS.GameplayAbility
{
    public sealed class AbilityManager
    {
        private readonly AbilitySystem _owner;
        private readonly HashSet<GameplayAbility> _granted = new();
        private readonly Dictionary<GameplayAbility, ActiveAbility> _active = new();
        private readonly List<ActiveAbility> _tickBuffer = new();
        
        private bool _ticking;
        public AbilityManager(AbilitySystem owner)
        {
            _owner = owner;
        }

        public bool Grant(GameplayAbility definition) => _granted.Add(definition);
        public bool Revoke(GameplayAbility definition)
        {
            if (!_granted.Remove(definition))
                return false;

            if (_active.TryGetValue(definition, out var active))
                active.Cancel();

            return true;
        }

        public bool Has(GameplayAbility definition) => _granted.Contains(definition);
        public bool IsActive(GameplayAbility definition) => _active.ContainsKey(definition);

        public bool TryActivate(GameplayAbility definition, AbilitySystem target, out ActiveAbility? active)
        {
            active = null;

            if (!_granted.Contains(definition))
                return false;

            if (_active.ContainsKey(definition))
                return false;

            if (!definition.MeetsTagRequirements(_owner))
                return false;

            foreach (var running in _active.Values)
            {
                if (running.Definition.Blocks(definition))
                    return false;
            }

            if (definition.CommitEffect is { } effect &&
                !_owner.Effects.CanApply(effect, _owner))
            {
                return false;
            }

            var behavior = definition.CreateBehavior();

            if (!behavior.CanActivate(_owner, target))
                return false;

            var instance = new ActiveAbility(this, definition, behavior, _owner, target);
            _active.Add(definition, instance);

            try
            {
                behavior.OnActivate(instance);
            }
            catch
            {
                instance.Cancel();
                throw;
            }

            active = instance;
            return true;
        }

        internal void End(ActiveAbility ability, bool cancelled)
        {
            if (!ability.IsActive)
                return;

            if (!_active.TryGetValue(ability.Definition, out var current) || !ReferenceEquals(current, ability))
                return;

            _active.Remove(ability.Definition);
            ability.IsActive = false;
            ability.Behavior.OnEnd(ability, cancelled);
        }
        public void CancelByTag(Tag tag)
        {
            var matches = new List<ActiveAbility>();

            foreach (var active in _active.Values)
            {
                if (active.Definition.HasTag(tag))
                    matches.Add(active);
            }

            foreach (var active in matches)
                active.Cancel();
        }
        public void Tick()
        {
            if (_ticking)
                throw new InvalidOperationException("Ability tick cannot be reentered.");

            _ticking = true;

            try
            {
                _tickBuffer.AddRange(_active.Values);

                foreach (var active in _tickBuffer)
                {
                    if (!active.IsActive)
                        continue;

                    active.ElapsedTicks++;

                    try
                    {
                        active.Behavior.OnTick(active);
                    }
                    catch
                    {
                        active.Cancel();
                        throw;
                    }
                }
            }
            finally
            {
                _tickBuffer.Clear();
                _ticking = false;
            }
        }
    }
}