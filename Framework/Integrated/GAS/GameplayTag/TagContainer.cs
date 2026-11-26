using System;
using System.Collections.Generic;

namespace ObFramework.Integrated.GAS.GameplayTag
{
    public sealed class TagContainer
    {
        private readonly Dictionary<GameplayTag, int> _tags = new();
        private readonly Dictionary<GameplayTag, int> _matchingTags = new();

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
        }

        private void UpdateHierarchy(GameplayTag tag, int delta)
        {
            UpdateCount(tag, delta);

            var name = tag.Name;
            var index = name.LastIndexOf('.');

            while (index >= 0)
            {
                name = name[..index];

                UpdateCount(new GameplayTag(name), delta);

                index = name.LastIndexOf('.');
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