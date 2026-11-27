namespace ObFramework.Integrated.GAS.GameplayEffect.Components
{
    public abstract class AEffectComponent
    {
        public virtual void Validate(GameplayEffect effect) { }
        public virtual void OnApply(EffectContext context) { }
        public virtual void OnRemove(EffectContext context) { }
        public virtual void OnPeriodic(EffectContext context, int stacks) { }
        public virtual void OnStackChanged(EffectContext context, int previous, int current) { }
    }
}