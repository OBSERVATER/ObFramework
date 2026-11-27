using System;
using ObFramework.Integrated.GAS.GameplayEffect.Components;

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
        
        public GameplayEffect(
            EEffectDurationType durationType,
            AEffectComponent[] components,
            int durationTicks = 0,
            int periodTicks = 0,
            bool executeOnApplication = false,
            int maxStacks = 0)
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
    }
}