using System;
using System.Collections.Generic;

namespace ObFramework.Integrated.GAS.GameplayTag
{
    public sealed class TagContainer
    {
        private const int MaxCachedParents = 128;
        private readonly Dictionary<GameplayTag, int> _tags = new();
        private readonly Dictionary<GameplayTag, int> _matchingTags = new();
        private Dictionary<GameplayTag, GameplayTag>? _parents;

        public void Add(GameplayTag tag)
        {
            if (!tag.IsValid) throw new ArgumentException("Invalid tag.");

            var count = GetCount(tag);
            _tags[tag] = count + 1;
            if (count == 0)
                UpdateHierarchy(tag, 1);
        }

        public bool Remove(GameplayTag tag)
        {
            if (!_tags.TryGetValue(tag, out var count))
                return false;

            if (count == 1)
            {
                _tags.Remove(tag);
                UpdateHierarchy(tag, -1);
            }
            else
            {
                _tags[tag] = count - 1;
            }

            return true;
        }

        public int GetCount(GameplayTag tag) => _tags.GetValueOrDefault(tag, 0);
        public bool HasExact(GameplayTag tag) => _tags.ContainsKey(tag);
        public bool Has(GameplayTag tag) => _matchingTags.ContainsKey(tag);

        public bool HasAny(IEnumerable<GameplayTag> tags)
        {
            foreach (var tag in tags)
            {
                if (Has(tag))
                    return true;
            }

            return false;
        }

        public bool HasAll(IEnumerable<GameplayTag> tags)
        {
            foreach (var tag in tags)
            {
                if (!Has(tag))
                    return false;
            }

            return true;
        }

        public void Clear()
        {
            _tags.Clear();
            _matchingTags.Clear();
            _parents = null;
        }

        private void UpdateHierarchy(GameplayTag tag, int delta)
        {
            while (true)
            {
                UpdateCount(tag, delta);
                if (_parents != null && _parents.TryGetValue(tag, out var parent))
                {
                    tag = parent;
                    continue;
                }

                var index = tag.Name.LastIndexOf('.');
                if (index < 0) return;

                parent = new GameplayTag(tag.Name[..index]);
                _parents ??= new Dictionary<GameplayTag, GameplayTag>();
                if (_parents.Count < MaxCachedParents)
                    _parents.Add(tag, parent);
                tag = parent;
            }
        }

        private void UpdateCount(
            GameplayTag tag, int delta)
        {
            var count = _matchingTags.GetValueOrDefault(tag, 0) + delta;

            if (count == 0)
                _matchingTags.Remove(tag);
            else
                _matchingTags[tag] = count;
        }
    }
}
