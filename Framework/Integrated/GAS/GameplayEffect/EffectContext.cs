namespace ObFramework.Integrated.GAS.GameplayEffect
{
    /// <summary>
    /// 记录效果的来源和目标
    /// </summary>
    public readonly struct EffectContext
    {
        public AbilitySystem Source { get; }
        public AbilitySystem Target { get; }

        public EffectContext(AbilitySystem source, AbilitySystem target)
        {
            Source = source;
            Target = target;
        }
    }
}