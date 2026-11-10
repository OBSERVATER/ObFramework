using System;
using System.Collections.Generic;
// ReSharper disable ConvertIfStatementToSwitchStatement

namespace ObFramework.Core.FrameLoop
{
    public class FrameDispatcher
    {
        private readonly Phase<ITickable> _tickables = new();
        private readonly Phase<IFixedTickable> _fixedTickables = new();
        private readonly Phase<ILateTickable> _lateTickables = new();

        public void Register(IFrameTickable instance)
        {
            if (instance is ITickable tickable)
                _tickables.Add(tickable);
            if (instance is IFixedTickable fixedTickable)
                _fixedTickables.Add(fixedTickable);
            if (instance is ILateTickable lateTickable)
                _lateTickables.Add(lateTickable);
        }
        public void UnRegister(IFrameTickable instance)
        {
            if (instance is ITickable tickable)
                _tickables.Remove(tickable);
            if (instance is IFixedTickable fixedTickable)
                _fixedTickables.Remove(fixedTickable);
            if (instance is ILateTickable lateTickable)
                _lateTickables.Remove(lateTickable);
        }

        public void Dispatch() => Dispatch(null);
        public void DispatchFixed() => DispatchFixed(null);
        public void DispatchLate() => DispatchLate(null);

        public void Dispatch(Func<bool>? canContinue) => _tickables.Dispatch(t => t.Tick(), canContinue);
        public void DispatchFixed(Func<bool>? canContinue) => _fixedTickables.Dispatch(t => t.FixedTick(), canContinue);
        public void DispatchLate(Func<bool>? canContinue) => _lateTickables.Dispatch(t => t.LateTick(), canContinue);

        private sealed class Phase<T> where T : class
        {
            private readonly Dictionary<T, long> _registered = new();
            private readonly List<KeyValuePair<T, long>> _snapshot = new();
            private long _nextRegistration;
            private bool _dispatching;

            public void Add(T instance) => _registered.TryAdd(instance, ++_nextRegistration);
            public void Remove(T instance) => _registered.Remove(instance);

            public void Dispatch(Action<T> tick, Func<bool>? canContinue)
            {
                if (_dispatching)
                    return;

                _dispatching = true;
                try
                {
                    _snapshot.AddRange(_registered);
                    foreach (var entry in _snapshot)
                    {
                        if (canContinue != null && !canContinue())
                            break;
                        // A remove followed by an add must invalidate the original entry too.
                        if (_registered.TryGetValue(entry.Key, out var registration) && registration == entry.Value)
                            tick(entry.Key);
                    }
                }
                finally
                {
                    _snapshot.Clear();
                    _dispatching = false;
                }
            }
        }
    }
}
