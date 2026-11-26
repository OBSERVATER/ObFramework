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
        public ActiveEffect? Apply(AGameplayEffect definition, AbilitySystem source)
        {
            var context = new EffectContext(source, _owner);
            definition.OnApply(context);

            if (definition.DurationType == EEffectDurationType.Instant)
                return null;

            var active = new ActiveEffect(definition, context);
            _effects.Add(active);

            if (definition is { PeriodTicks: > 0, ExecuteOnApplication: true })
            {
                definition.OnPeriodic(context);
            }

            return active;
        }

        public bool Remove(ActiveEffect effect)
        {
            if (!_effects.Remove(effect))
                return false;

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
                        definition.OnPeriodic(effect.Context);
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