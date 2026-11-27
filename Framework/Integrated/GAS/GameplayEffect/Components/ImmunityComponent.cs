using System;
using Tag = ObFramework.Integrated.GAS.GameplayTag.GameplayTag;

namespace ObFramework.Integrated.GAS.GameplayEffect.Components
{
    /// <summary>
    /// 阻止具有指定 EffectTag 的效果
    /// </summary>
    public sealed class ImmunityComponent : AEffectComponent
    {
        private readonly Tag _blockedTag;

        public ImmunityComponent(Tag blockedTag)
        {
            _blockedTag = blockedTag;
        }

        public override void Validate(GameplayEffect effect)
        {
            if (!_blockedTag.IsValid)
                throw new ArgumentException("Invalid immunity tag.");

            if (effect.DurationType == EEffectDurationType.Instant)
                throw new ArgumentException("Immunity requires an active effect.");
        }

        public override bool BlockIncoming(GameplayEffect incoming, EffectContext incomingContext) 
            => incoming.HasTag(_blockedTag);
    }
}