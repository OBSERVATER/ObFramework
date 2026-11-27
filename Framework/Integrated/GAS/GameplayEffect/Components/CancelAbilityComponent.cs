using Tag = ObFramework.Integrated.GAS.GameplayTag.GameplayTag;

namespace ObFramework.Integrated.GAS.GameplayEffect.Components
{
    /// <summary>
    /// 根据标签取消技能
    /// </summary>
    public sealed class CancelAbilityComponent : AEffectComponent
    {
        private readonly Tag _tag;

        public CancelAbilityComponent(Tag tag)
        {
            if (!tag.IsValid)
                throw new System.ArgumentException("Invalid tag.");

            _tag = tag;
        }

        public override void OnApply(EffectContext context)
        {
            context.Target.Abilities.CancelByTag(_tag);
        }
    }
}