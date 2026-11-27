using System;
using Effect = ObFramework.Integrated.GAS.GameplayEffect.GameplayEffect;
using Tag = ObFramework.Integrated.GAS.GameplayTag.GameplayTag;

namespace ObFramework.Integrated.GAS.GameplayAbility
{
    /// <summary>
    /// 技能定义
    /// </summary>
    public sealed class GameplayAbility
    {
        private readonly Func<AAbilityBehaviour> _factory;

        private readonly Tag[] _abilityTags;
        private readonly Tag[] _requiredTags;
        private readonly Tag[] _blockedTags;
        private readonly Tag[] _blockAbilitiesWithTags;

        public Effect? CommitEffect { get; }

        public GameplayAbility(
            Func<AAbilityBehaviour> factory,
            Effect? commitEffect = null,
            Tag[]? abilityTags = null,
            Tag[]? requiredTags = null,
            Tag[]? blockedTags = null,
            Tag[]? blockAbilitiesWithTags = null)
        {
            _factory = factory ??
                throw new ArgumentNullException(nameof(factory));

            CommitEffect = commitEffect;

            _abilityTags = CopyTags(abilityTags);
            _requiredTags = CopyTags(requiredTags);
            _blockedTags = CopyTags(blockedTags);
            _blockAbilitiesWithTags = CopyTags(blockAbilitiesWithTags);
        }

        internal AAbilityBehaviour CreateBehavior() 
            => _factory() ?? throw new InvalidOperationException("Ability factory returned null.");

        public bool HasTag(Tag tag)
        {
            foreach (var owned in _abilityTags)
            {
                if (owned.Matches(tag))
                    return true;
            }

            return false;
        }

        internal bool MeetsTagRequirements(AbilitySystem owner)
        {
            return owner.Tags.HasAll(_requiredTags)
                && !owner.Tags.HasAny(_blockedTags);
        }

        internal bool Blocks(GameplayAbility incoming)
        {
            foreach (var tag in _blockAbilitiesWithTags)
            {
                if (incoming.HasTag(tag))
                    return true;
            }

            return false;
        }

        private static Tag[] CopyTags(Tag[]? tags)
        {
            if (tags == null)
                return Array.Empty<Tag>();

            var result = (Tag[])tags.Clone();

            foreach (var tag in result)
            {
                if (!tag.IsValid)
                    throw new ArgumentException("Invalid tag.");
            }

            return result;
        }
    }
}