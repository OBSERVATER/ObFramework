using System.Collections.Generic;
// ReSharper disable ConvertIfStatementToSwitchStatement

namespace ObFramework.Core.FrameLoop
{
    public class FrameDispatcher
    {
        private readonly HashSet<ITickable> _tickables = new();
        private readonly HashSet<IFixedTickable> _fixedTickables = new();
        private readonly HashSet<ILateTickable> _lateTickables = new();

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
        
        public void Dispatch()
        {
            foreach (var t in _tickables)
            {
                t.Tick();
            }
        }
        public void DispatchFixed()
        {
            foreach (var t in _fixedTickables)
            {
                t.FixedTick();
            }
        }
        public void DispatchLate()
        {
            foreach (var t in _lateTickables)
            {
                t.LateTick();
            }
        }
    }
}