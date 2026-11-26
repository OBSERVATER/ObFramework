using System;
using System.Buffers;

namespace ObFramework.Integrated.GAS.Attributes.Core
{
    public interface IAttribute<T> : IDisposable where T : struct
    {
        T Base { get; }
        T Value { get; }
        int SlotCount { get; }

        void SetBase(T value);

        T GetSlot(int index);
        void SetSlot(int index, T value);
    }
    /// <summary>
    ///     Attribute.
    ///     <para>
    ///         <b>Uses ArrayPool; must be disposed.</b>
    ///     </para>
    /// </summary>
    public class Attribute<TFormula, TValue> : IAttribute<TValue>
        where TFormula : struct, IFormula<TValue>
        where TValue : struct
    {
        private TFormula _formula;
        private TValue _base;
        private TValue[] _slots;

        private TValue _cached;
        private bool _dirty;

        public int SlotCount => _formula.SlotCount;
        
        public Attribute(TValue @base, TFormula formula)
        {
            _base = @base;
            _formula = formula;
            _slots = ArrayPool<TValue>.Shared.Rent(_formula.SlotCount);
            Array.Clear(_slots, 0, _formula.SlotCount);
            _cached = default;
            _dirty = true;
        }

        public TValue Value
        {
            get
            {
                if (!_dirty) return _cached;

                _cached = _formula.Calculate(_base, _slots.AsSpan(0, _formula.SlotCount));
                _dirty = false;

                return _cached;
            }
        }


        #region Base

        public TValue Base => _base;

        public void SetBase(TValue value)
        {
            _base = value;
            _dirty = true;
        }

        #endregion

        #region Slot

        public TValue this[int index]
        {
            get => _slots.AsSpan(0, SlotCount)[index];
            set
            {
                _slots.AsSpan(0, SlotCount)[index] = value;
                _dirty = true;
            }
        }

        public TValue GetSlot(int slotIndex) => this[slotIndex];

        public void SetSlot(int slotIndex, TValue value) => this[slotIndex] = value;

        public void ClearSlot(int slotIndex) => this[slotIndex] = default;

        public void ClearSlots()
        {
            Array.Clear(_slots, 0, _formula.SlotCount);
            _dirty = true;
        }

        #endregion

        public void Dispose()
        {
            if (_slots == null) return;

            ArrayPool<TValue>.Shared.Return(_slots, clearArray: true);
            _slots = null;
        }
    }
}