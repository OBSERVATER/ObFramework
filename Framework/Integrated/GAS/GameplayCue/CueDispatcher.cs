using System;
using System.Collections.Generic;
using Tag = ObFramework.Integrated.GAS.GameplayTag.GameplayTag;

namespace ObFramework.Integrated.GAS.GameplayCue
{
    public sealed class CueDispatcher
    {
        private readonly AbilitySystem _owner;

        private readonly Dictionary<Tag, int> _persistent = new();

        public event Action<CueEvent>? Emitted;

        public CueDispatcher(AbilitySystem owner)
        {
            _owner = owner;
        }

        public void Execute(Tag tag, AbilitySystem source, float? magnitude = null)
        {
            if (!tag.IsValid)
                throw new ArgumentException("Invalid cue tag.");

            Emit(tag, ECueEventType.Executed, source, magnitude);
        }

        internal void Add(Tag tag, AbilitySystem source)
        {
            var count = _persistent.GetValueOrDefault(tag, 0);

            _persistent[tag] = count + 1;

            if (count == 0)
                Emit(tag, ECueEventType.Added, source);
        }

        internal void Remove(Tag tag, AbilitySystem source)
        {
            if (!_persistent.TryGetValue(tag, out var count))
                return;

            if (count > 1)
            {
                _persistent[tag] = count - 1;
                return;
            }

            _persistent.Remove(tag);

            Emit(tag, ECueEventType.Removed, source);
        }

        private void Emit(Tag tag, ECueEventType type, AbilitySystem source, float? magnitude = null)
        {
            Emitted?.Invoke(new CueEvent(tag, type, source, _owner, magnitude));
        }
    }
}