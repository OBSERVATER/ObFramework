namespace ObFramework.Integrated.GAS.GameplayEffect
{
    /// <summary>
    /// 生效的效果实例
    /// </summary>
    public sealed class ActiveEffect
    {
        public AGameplayEffect Definition { get; }
        public EffectContext Context { get; }

        public int RemainingTicks { get; internal set; }

        internal ActiveEffect(AGameplayEffect definition, EffectContext context)
        {
            Definition = definition;
            Context = context;
            RemainingTicks = definition.DurationTicks;
        }
    }
}