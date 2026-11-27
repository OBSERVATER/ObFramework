namespace ObFramework.Integrated.GAS.GameplayAbility
{
    public abstract class AAbilityBehaviour
    {
        public virtual bool CanActivate(AbilitySystem owner, AbilitySystem target)
        {
            return true;
        }
        public abstract void OnActivate(ActiveAbility ability);
        public virtual void OnTick(ActiveAbility ability) { }
        public virtual void OnEnd(ActiveAbility ability, bool cancelled)
        {
        }
    }
}