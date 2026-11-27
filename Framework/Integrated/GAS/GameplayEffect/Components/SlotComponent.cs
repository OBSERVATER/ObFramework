using System;
using ObFramework.Integrated.GAS.Attributes.Core;

namespace ObFramework.Integrated.GAS.GameplayEffect.Components
{
    public sealed class SlotComponent : AEffectComponent
    {
        private readonly AttributeKey<float> _key;
        private readonly int _slot;
        private readonly float _value;

        public SlotComponent(AttributeKey<float> key, int slot, float value)
        {
            _key = key;
            _slot = slot;
            _value = value;
        }

        public override void Validate(GameplayEffect effect)
        {
            if (effect.DurationType == EEffectDurationType.Instant)
                throw new ArgumentException("SlotComponent requires an active effect.");
        }

        public override void OnStackChanged(EffectContext context, int previous, int current)
        {
            var attribute = context.Target.Attributes.Get(_key);
            float delta = (current - previous) * _value;
            attribute.SetSlot(_slot, attribute.GetSlot(_slot) + delta);
        }
    }
}