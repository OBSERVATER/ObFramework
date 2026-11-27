using System;
using ObFramework.Integrated.GAS.GameplayEffect.Components;
using Tag = ObFramework.Integrated.GAS.GameplayTag.GameplayTag;

namespace ObFramework.Integrated.GAS.GameplayEffect
{
    /// <summary>
    /// 效果抽象类
    /// </summary>
    public sealed class GameplayEffect
    {
        public EEffectDurationType DurationType { get; }
        public int DurationTicks { get; }
        public int PeriodTicks { get; }
        public bool ExecuteOnApplication { get; }
        public int MaxStacks { get; }
        private readonly AEffectComponent[] _components;
        private readonly Tag[] _effectTags;
        public GameplayEffect(
            EEffectDurationType durationType,
            AEffectComponent[] components,
            int durationTicks = 0,
            int periodTicks = 0,
            bool executeOnApplication = false,
            int maxStacks = 0,
            Tag[]? effectTags = null)
        {
            if (durationType == EEffectDurationType.Duration && durationTicks <= 0)
                throw new ArgumentOutOfRangeException(nameof(durationTicks));

            if (periodTicks < 0 || durationType == EEffectDurationType.Instant && periodTicks > 0)
                throw new ArgumentOutOfRangeException(nameof(periodTicks));

            if (maxStacks < 0 || durationType == EEffectDurationType.Instant && maxStacks > 0)
                throw new ArgumentOutOfRangeException(nameof(maxStacks));

            if (executeOnApplication && periodTicks == 0)
                throw new ArgumentException("Periodic execution requires a period.");

            DurationType = durationType;
            DurationTicks = durationTicks;
            PeriodTicks = periodTicks;
            ExecuteOnApplication = executeOnApplication;
            MaxStacks = maxStacks;
            
            if (components == null)
                throw new ArgumentNullException(nameof(components));

            _components = (AEffectComponent[])components.Clone();
            _effectTags = effectTags == null ? Array.Empty<Tag>() : (Tag[])effectTags.Clone();

            foreach (var tag in _effectTags)
            {
                if (!tag.IsValid)
                    throw new ArgumentException("Invalid effect tag.");
            }
            foreach (var component in _components)
            {
                if (component == null)
                    throw new ArgumentException("Component cannot be null.");
                component.Validate(this);
            }
        }
        /// <summary>
        /// 添加效果时
        /// </summary>
        internal void OnApply(EffectContext context)
        {
            foreach (var component in _components)
            {
                component.OnApply(context);
            }
        }
        /// <summary>
        /// 每隔一定tick触发
        /// </summary>
        internal void OnPeriodic(EffectContext context, int stacks)
        {
            foreach (var component in _components)
            {
                component.OnPeriodic(context,stacks);
            }
        }
        /// <summary>
        /// 移除效果时
        /// </summary>
        internal void OnRemove(EffectContext context)
        {
            for (int i = _components.Length - 1; i >= 0; i--)
                _components[i].OnRemove(context);
        }
        /// <summary>
        /// buff层数改变时
        /// </summary>
        public void OnStackChanged(EffectContext context, int previous, int now)
        {
            foreach (var component in _components)
            {
                component.OnStackChanged(context,previous,now);
            }
        }
        public bool HasTag(Tag tag)
        {
            foreach (var owned in _effectTags)
            {
                if (owned.Matches(tag))
                    return true;
            }

            return false;
        }

        internal bool CanApply(EffectContext context)
        {
            foreach (var component in _components)
            {
                if (!component.CanApply(context))
                    return false;
            }

            return true;
        }

        internal bool BlocksIncoming(GameplayEffect incoming, EffectContext incomingContext)
        {
            foreach (var component in _components)
            {
                if (component.BlockIncoming(incoming, incomingContext))
                    return true;
            }

            return false;
        }
    }
}