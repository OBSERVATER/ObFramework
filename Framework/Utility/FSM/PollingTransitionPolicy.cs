using System;

namespace ObFramework.Utility.FSM
{
    public readonly struct PollingTransitionPolicy<T>
    {
        public T PreviousId { get; }
        public T NextId { get; }
        public Func<bool> TransitionCond { get; }

        public PollingTransitionPolicy(T previousId, T nextId, Func<bool> transitionCond)
        {
            PreviousId = previousId;
            NextId = nextId;
            TransitionCond = transitionCond;
        }
    }
}
