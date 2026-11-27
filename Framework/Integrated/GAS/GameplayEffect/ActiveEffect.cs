namespace ObFramework.Integrated.GAS.GameplayEffect
{
    /// <summary>
    /// 生效的效果实例
    /// </summary>
    public sealed class ActiveEffect
    {
        public GameplayEffect Definition { get; }
        public EffectContext Context { get; }
        public int Stacks { get; internal set; } = 1;
        public int RemainingTicks { get; internal set; }
        public int RemainingPeriodTicks { get; internal set; }
        
        internal ActiveEffect(GameplayEffect definition, EffectContext context)
        {
            Definition = definition;
            Context = context;
            RemainingTicks = definition.DurationTicks;
            RemainingPeriodTicks = definition.PeriodTicks;
        }
    }
}