namespace ObFramework.Utility.FSM
{
    public interface IState
    {
        /// <summary>
        /// 目标状态的进入许可检查
        /// </summary>
        bool Condition();
        void Enter();
        void Exit();
        void Update(float dt);
        void PhysicsUpdate(float dt);
    }

    public abstract class AState<TStateId, TOwner> : IState
    {
        protected TOwner Owner;
        protected FSM<TStateId> FSM;

        protected AState(TOwner owner, FSM<TStateId> fSM)
        {
            Owner = owner;
            FSM = fSM;
        }
        void IState.Enter() => OnEnter();
        void IState.Exit() => OnExit();
        bool IState.Condition() => OnCondition();
        void IState.Update(float dt) => OnUpdate(dt);
        void IState.PhysicsUpdate(float dt) => OnPhysicsUpdate(dt);
        private void OnEnter()
        {
        }

        protected virtual void OnExit()
        {
        }
        protected virtual bool OnCondition() => true;


        protected virtual void OnUpdate(float dt)
        {
        }

        protected virtual void OnPhysicsUpdate(float dt)
        {
        }
    }
}
