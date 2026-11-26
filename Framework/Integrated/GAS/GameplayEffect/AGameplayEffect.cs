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

        protected AGameplayEffect(EEffectDurationType durationType, int durationTicks = 0)
        {
            if (durationType == EEffectDurationType.Duration && durationTicks <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(durationTicks));
            }

            DurationType = durationType;
            DurationTicks = durationTicks;
        }
        public abstract void OnApply(EffectContext context);
        public virtual void OnRemove(EffectContext context) { }
    }
}