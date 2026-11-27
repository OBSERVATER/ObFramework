using System;
using ObFramework.Integrated.GAS.Attributes.Core;

namespace ObFramework.Integrated.GAS.GameplayEffect.Components
{
    public sealed class BaseComponent : AEffectComponent
    {
        private readonly AttributeKey<float> _key;
        private readonly float _value;
        private readonly bool _periodic;
        public BaseComponent(AttributeKey<float> key, float value, bool periodic = false)
        {
            _key = key;
            _value = value;
            _periodic = periodic;
        }
        public override void Validate(GameplayEffect effect)
        {
            if (_periodic && effect.PeriodTicks <= 0)
                throw new ArgumentException("Periodic component requires a period.");
        }
        public override void OnApply(EffectContext context)
        {
            if (!_periodic)
            {
                Modify(context,_value);
            }
        }
        public override void OnPeriodic(EffectContext context, int stacks)
        {
            if (_periodic)
            {
                Modify(context,_value * stacks);
            }
        }
        private void Modify(EffectContext context, float stacks)
        {
            var attribute = context.Target.Attributes.Get(_key);
            attribute.SetBase(attribute.Base + stacks);
        }
    }
}