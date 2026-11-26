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

        protected AGameplayEffect(
            EEffectDurationType durationType,
            int durationTicks = 0,
            int periodTicks = 0,
            bool executeOnApplication = false)
        {
            if (durationType == EEffectDurationType.Duration && durationTicks <= 0)
                throw new ArgumentOutOfRangeException(nameof(durationTicks));

            if (periodTicks < 0 || durationType == EEffectDurationType.Instant && periodTicks > 0)
                throw new ArgumentOutOfRangeException(nameof(periodTicks));

            DurationType = durationType;
            DurationTicks = durationTicks;
            PeriodTicks = periodTicks;
            ExecuteOnApplication = executeOnApplication;
        }
        /// <summary>
        /// 添加效果时
        /// </summary>
        public abstract void OnApply(EffectContext context);
        /// <summary>
        /// 每隔一定tick触发
        /// </summary>
        public virtual void OnPeriodic(EffectContext context) { }
        /// <summary>
        /// 移除效果时
        /// </summary>
        public virtual void OnRemove(EffectContext context) { }
    }
}