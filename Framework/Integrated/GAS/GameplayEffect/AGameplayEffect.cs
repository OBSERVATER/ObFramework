using System;

namespace ObFramework.Integrated.GAS.GameplayEffect
{
    /// <summary>
    /// 效果抽象类
    /// </summary>
    public abstract class AGameplayEffect
    {
        public EEffectDurationType DurationType { get; }
        public int DurationTicks { get; }
        public int PeriodTicks { get; }
        public bool ExecuteOnApplication { get; }
        public int MaxStacks { get; }
        
        protected AGameplayEffect(
            EEffectDurationType durationType,
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

            DurationType = durationType;
            DurationTicks = durationTicks;
            PeriodTicks = periodTicks;
            ExecuteOnApplication = executeOnApplication;
            MaxStacks = maxStacks;
        }
        /// <summary>
        /// 添加效果时
        /// </summary>
        public abstract void OnApply(EffectContext context);
        /// <summary>
        /// 每隔一定tick触发
        /// </summary>
        public virtual void OnPeriodic(EffectContext context) { }
        public virtual void OnPeriodic(EffectContext context, int stacks)
        {
            OnPeriodic(context);
        }
        /// <summary>
        /// 移除效果时
        /// </summary>
        public virtual void OnRemove(EffectContext context) { }
        /// <summary>
        /// buff层数改变时
        /// </summary>
        public virtual void OnStackChanged(EffectContext context, int previous, int now) { }
    }
}