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
            private readonly Dictionary<T, LinkedListNode<T?>> _registered = new();
            private readonly LinkedList<T?> _active = new();
            private readonly List<LinkedListNode<T?>> _pending = new();
            private bool _dispatching;

            public void Add(T instance)
            {
                if (_registered.ContainsKey(instance))
                    return;
                var registration = new LinkedListNode<T?>(instance);
                _registered.Add(instance, registration);
                if (_dispatching)
                    _pending.Add(registration);
                else
                    _active.AddLast(registration);
            }

            public void Remove(T instance)
            {
                if (!_registered.Remove(instance, out var registration))
                    return;
                if (!_dispatching)
                {
                    _active.Remove(registration);
                    return;
                }

                registration.Value = null;
                if (registration.List != null)
                    _pending.Add(registration);
            }

            public void Dispatch(Action<T> tick, Func<bool>? canContinue)
            {
                if (_dispatching)
                    return;

                _dispatching = true;
                try
                {
                    for (var registration = _active.First; registration != null; registration = registration.Next)
                    {
                        if (canContinue != null && !canContinue())
                            break;
                        if (registration.Value is T instance)
                            tick(instance);
                    }
                }
                finally
                {
                    foreach (var registration in _pending)
                    {
                        if (registration.List != null)
                            _active.Remove(registration);
                        else if (registration.Value != null)
                            _active.AddLast(registration);
                    }
                    _pending.Clear();
                    _dispatching = false;
                }
            }
        }
    }
}
