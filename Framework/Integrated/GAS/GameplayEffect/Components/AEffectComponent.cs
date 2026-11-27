namespace ObFramework.Integrated.GAS.GameplayEffect.Components
{
    public abstract class AEffectComponent
    {
        public virtual void Validate(GameplayEffect effect) { }
        public virtual void OnApply(EffectContext context) { }
        public virtual void OnRemove(EffectContext context) { }
        public virtual void OnPeriodic(EffectContext context, int stacks) { }
        public virtual void OnStackChanged(EffectContext context, int previous, int current) { }
        /// <summary>
        /// 检查自己能否被施加
        /// </summary>
        public virtual bool CanApply(EffectContext context)
        {
            return true;
        }
        /// <summary>
        /// 检查自己生效期间是否阻止其他效果进入
        /// </summary>
        public virtual bool BlockIncoming(GameplayEffect eff, EffectContext incomingContext)
        {
            return false;
        }
    }
}