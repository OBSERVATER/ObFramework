namespace ObFramework.Integrated.GAS.GameplayAbility
{
    /// <summary>
    /// 正在执行的技能
    /// </summary>
    public sealed class ActiveAbility
    {
        private readonly AbilityManager _manager;

        public GameplayAbility Definition { get; }
        public AbilitySystem Owner { get; }
        public AbilitySystem Target { get; }
        /// <summary>
        /// 已推进的逻辑tick
        /// </summary>
        public long ElapsedTicks { get; internal set; }
        public bool IsCommitted { get; private set; }
        public bool IsActive { get; internal set; } = true;

        internal AAbilityBehaviour Behavior { get; }

        internal ActiveAbility(
            AbilityManager manager,
            GameplayAbility definition,
            AAbilityBehaviour behavior,
            AbilitySystem owner,
            AbilitySystem target)
        {
            _manager = manager;

            Definition = definition;
            Behavior = behavior;

            Owner = owner;
            Target = target;
        }
        public bool TryCommit()
        {
            if (!IsActive || IsCommitted)
                return false;

            var effect = Definition.CommitEffect;

            if (effect != null &&
                !Owner.Effects.TryApply(effect, Owner, out _))
            {
                return false;
            }

            IsCommitted = true;

            return true;
        }
        public void End()
        {
            _manager.End(this, false);
        }

        public void Cancel()
        {
            _manager.End(this, true);
        }
    }
}