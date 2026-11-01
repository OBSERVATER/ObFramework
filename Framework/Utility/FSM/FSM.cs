using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace ObFramework.Utility.FSM
{
    public class FSM<T>
    {
        private readonly Dictionary<T, IState> _stateMap = new();
        private IState _curState;
        private T _stateId;
        private T _previousStateId;
        private readonly Dictionary<T, List<PollingTransitionPolicy<T>>> _transMap = new();
        public T CurStateId => _stateId;
        public IState CurState => _curState;
        public T PreviousStateId => _previousStateId;
        public event Action<T,T> OnStateChange;
        public void AddState(T stateId, IState state)
        {
            _stateMap.Add(stateId, state);
        }
        public void AddTransition([NotNull]PollingTransitionPolicy<T> policy)
        {
            if (!_transMap.TryGetValue(policy.PreviousId,out var list))
            {
                list = new List<PollingTransitionPolicy<T>>();
                _transMap.Add(policy.PreviousId, list);
            }
            list.Add(policy);
        }
        public void StartState(T state)
        {
            if (_stateMap.TryGetValue(state, out var s))
            {
                _previousStateId = state;
                _curState = s;
                _stateId = state;

                _curState.Enter();
            }
        }
        public bool ChangeState(T state) 
        {
            if (state.Equals(CurStateId))
            {
                return false;
            }
            if (_stateMap.TryGetValue(state, out var s))
            {
                if (s != null && s.Condition())
                {
                    _curState.Exit();

                    _previousStateId = CurStateId;
                    _curState = s;
                    _stateId = state;

                    _curState.Enter();
                    OnStateChange?.Invoke(PreviousStateId, CurStateId);
                    return true;
                }
            }
            return false;
        }
        public void Update(float dt)
        {
            CheckTransition();
            _curState?.Update(dt);
        }
        public void PhysicsUpdate(float dt) => _curState?.PhysicsUpdate(dt);
        private void CheckTransition()
        {
            foreach (var trans in _transMap[_stateId])
            {
                if (trans.TransitionCond.Invoke() && ChangeState(trans.NextId))
                {
                    break;
                }
            }
        }

        public void Clear()
        {
            _curState.Exit();
            _stateMap.Clear();
            _transMap.Clear();
            _stateId = default;
            _curState = null;
            _previousStateId = default;
            OnStateChange = null;
        }
    }
}
