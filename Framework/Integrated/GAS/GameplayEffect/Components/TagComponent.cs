using System;
using Tag = ObFramework.Integrated.GAS.GameplayTag.GameplayTag;

namespace ObFramework.Integrated.GAS.GameplayEffect.Components
{
    public sealed class TagComponent : AEffectComponent
    {
        private readonly Tag _tag;

        public TagComponent(Tag tag)
        {
            _tag = tag;
        }

        public override void Validate(GameplayEffect effect)
        {
            if (!_tag.IsValid)
                throw new ArgumentException("Invalid tag.");

            if (effect.DurationType == EEffectDurationType.Instant)
                throw new ArgumentException("TagComponent requires an active effect.");
        }

        public override void OnApply(EffectContext context)
        {
            context.Target.Tags.Add(_tag);
        }

        public override void OnRemove(EffectContext context)
        {
            context.Target.Tags.Remove(_tag);
        }
    }
}