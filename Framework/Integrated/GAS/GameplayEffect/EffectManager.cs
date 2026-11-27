using System.Collections.Generic;

namespace ObFramework.Integrated.GAS.GameplayEffect
{
    public sealed class EffectManager
    {
        private AbilitySystem _owner;
        private readonly List<ActiveEffect> _effects = new();
        public EffectManager(AbilitySystem abilitySystem)
        {
            _owner = abilitySystem;
        }
        public ActiveEffect? Apply(GameplayEffect definition, AbilitySystem source)
        {
            if (definition.MaxStacks > 0)
            {
                foreach (var eff in _effects)
                {
                    if (!ReferenceEquals(eff.Definition, definition) || !ReferenceEquals(eff.Context.Source, source))
                        continue;

                    eff.RemainingTicks = definition.DurationTicks;
                    var previous = eff.Stacks;

                    if (previous < definition.MaxStacks)
                    {
                        eff.Stacks++;
                        definition.OnStackChanged(eff.Context,previous,eff.Stacks);
                    }

                    return eff;
                }
            }
            var context = new EffectContext(source, _owner);
            definition.OnApply(context);

            if (definition.DurationType == EEffectDurationType.Instant)
                return null;

            var active = new ActiveEffect(definition, context);
            _effects.Add(active);
            definition.OnStackChanged(context, 0, 1);
            if (definition is { PeriodTicks: > 0, ExecuteOnApplication: true })
            {
                definition.OnPeriodic(context,active.Stacks);
            }

            return active;
        }

        public bool Remove(ActiveEffect effect)
        {
            if (!_effects.Remove(effect))
                return false;
            
            var previous = effect.Stacks;
            effect.Stacks = 0;
            effect.Definition.OnStackChanged(effect.Context, previous, 0);
            effect.Definition.OnRemove(effect.Context);

            return true;
        }

        public void Tick()
        {
            for (int i = _effects.Count - 1; i >= 0; i--)
            {
                var effect = _effects[i];
                var definition = effect.Definition;

                if (definition.PeriodTicks > 0)
                {
                    effect.RemainingPeriodTicks--;

                    if (effect.RemainingPeriodTicks <= 0)
                    {
                        effect.RemainingPeriodTicks = definition.PeriodTicks;
                        definition.OnPeriodic(effect.Context,effect.Stacks);
                    }
                }

                if (definition.DurationType != EEffectDurationType.Duration) continue;
                effect.RemainingTicks--;
                if (effect.RemainingTicks <= 0) Remove(effect);
            }
        }

        public void Clear()
        {
            while (_effects.Count > 0)
            {
                Remove(_effects[^1]);
            }
        }
    }
}