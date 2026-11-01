using System;
using System.Collections.Generic;

namespace ObFramework.Core
{
    public interface IReadonlyBindableProperty<T>
    {
        T Value { get; }
        void Bind(Action<T> onEvent, bool trigger = false);
        void UnBind(Action<T> onEvent);
    }
    public interface IBindableProperty<T> : IReadonlyBindableProperty<T>
    {
        new T Value { get; set; }
        void SetWithoutEvent(T val);
    }
    public class BindableProperty<T> : IBindableProperty<T>
    {
        protected T _val;
        private event Action<T> _onChanged;
        public static Func<T, T, bool> Comparer { get; set; } = EqualityComparer<T>.Default.Equals;
        public T Value
        {
            get => Get();
            set
            {
                if (Comparer(value, Get()))
                {
                    return;
                }
                Set(value);
                _onChanged?.Invoke(Get());
            }
        }

        public BindableProperty(T value = default) => _val = value;
        public BindableProperty<T> WithComparer(Func<T, T, bool> comparer)
        {
            Comparer = comparer;
            return this;
        }
        public void Bind(Action<T> onEvent, bool trigger = false)
        {
            if (onEvent == null)
            {
                throw new ArgumentNullException();
            }
            if (trigger)
            {
                onEvent?.Invoke(Value);
            }
            _onChanged += onEvent;
        }
        protected virtual T Get() => _val;
        protected virtual void Set(T val) => _val = val;
        public void SetWithoutEvent(T val) => Set(val);

        public void UnBind(Action<T> onEvent)
        {
            _onChanged -= onEvent;
        }
    }
}
