using System;
using Tag = ObFramework.Integrated.GAS.GameplayTag.GameplayTag;

namespace ObFramework.Integrated.GAS.GameplayEffect.Components
{
    public sealed class CueComponent : AEffectComponent
    {
        private readonly Tag _tag;
        private readonly ECueTrigger _trigger;

        public CueComponent(Tag tag, ECueTrigger trigger = ECueTrigger.OnApply)
        {
            _tag = tag;
            _trigger = trigger;
        }

        public override void Validate(GameplayEffect effect)
        {
            if (!_tag.IsValid)
                throw new ArgumentException("Invalid cue tag.");

            if (_trigger == ECueTrigger.Persistent && effect.DurationType == EEffectDurationType.Instant)
            {
                throw new ArgumentException("Persistent cue requires an active effect.");
            }

            if (_trigger == ECueTrigger.Periodic && effect.PeriodTicks <= 0)
            {
                throw new ArgumentException("Periodic cue requires a period.");
            }
        }

        public override void OnApply(EffectContext context)
        {
            switch (_trigger)
            {
                case ECueTrigger.OnApply:
                    context.Target.Cues.Execute(_tag, context.Source);
                    break;

                case ECueTrigger.Persistent:
                    context.Target.Cues.Add(_tag, context.Source);
                    break;
            }
        }

        public override void OnPeriodic(EffectContext context, int stacks)
        {
            if (_trigger == ECueTrigger.Periodic)
            {
                context.Target.Cues.Execute(_tag, context.Source);
            }
        }

        public override void OnRemove(EffectContext context)
        {
            if (_trigger == ECueTrigger.Persistent)
            {
                context.Target.Cues.Remove(_tag, context.Source);
            }
        }
    }
}