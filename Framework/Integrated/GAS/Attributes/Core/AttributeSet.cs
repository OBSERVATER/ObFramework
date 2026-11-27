using System;
using System.Collections.Generic;

namespace ObFramework.Integrated.GAS.Attributes.Core
{
    public sealed class AttributeSet : IDisposable
    {
        private readonly Dictionary<AAttributeKey, IDisposable> _attributes = new();

        public void Register<T>(AttributeKey<T> key, IAttribute<T> attribute) where T : struct
        {
            _attributes.Add(key, attribute);
        }

        public IAttribute<T> Get<T>(AttributeKey<T> key) where T : struct
        {
            return (IAttribute<T>)_attributes[key];
        }

        public bool Has(AAttributeKey key)
        {
            return _attributes.ContainsKey(key);
        }

        public bool Remove(AAttributeKey key)
        {
            if (!_attributes.Remove(key, out var attribute))
                return false;

            attribute.Dispose();
            return true;
        }

        public void Dispose()
        {
            foreach (var attribute in _attributes.Values)
                attribute.Dispose();

            _attributes.Clear();
        }
        
        public void Clear()
        {
            _attributes.Clear();
        }
    }
}