using System;
using Tag = ObFramework.Integrated.GAS.GameplayTag.GameplayTag;

namespace ObFramework.Integrated.GAS.GameplayEffect.Components
{
    /// <summary>
    /// 检查目标是否满足施加条件
    /// </summary>
    public sealed class TagRequirementComponent : AEffectComponent
    {
        private readonly Tag[] _required;
        private readonly Tag[] _blocked;

        public TagRequirementComponent(Tag[]? required = null, Tag[]? blocked = null)
        {
            _required = required == null ? Array.Empty<Tag>() : (Tag[])required.Clone();
            _blocked = blocked == null ? Array.Empty<Tag>() : (Tag[])blocked.Clone();

            foreach (var tag in _required)
                if (!tag.IsValid)
                    throw new ArgumentException("Invalid required tag.");

            foreach (var tag in _blocked)
                if (!tag.IsValid)
                    throw new ArgumentException("Invalid blocked tag.");
        }

        public override bool CanApply(EffectContext context)
        {
            var tags = context.Target.Tags;
            return tags.HasAll(_required) && !tags.HasAny(_blocked);
        }
    }
}