using ObFramework.Integrated.GAS.Attributes.Core;

namespace ObFramework.Integrated.GAS.Attributes.Creational
{
    public ref struct FormulaBuilderFloat
    {
        private FormulaBuilderData<float> _data;

        internal static FormulaBuilderFloat Create(int size) =>
            new() { _data = new FormulaBuilderData<float>(size) };

        public FormulaFloat Build()
        {
            if (_data == null)
                return default;

            _data.Build(out var instructions, out var constants, out var slots, out var maxDepth);
            return new FormulaFloat(instructions, constants, slots, maxDepth);
        }

        public void LoadBase() => _data.Emit(OpCode.LoadBase);
        public void LoadSlot(int slotIndex) => _data.LoadSlot(slotIndex);
        public void LoadConstant(float value) => _data.LoadConstant(value);

        public void Add() => _data.Emit(OpCode.Add);
        public void Subtract() => _data.Emit(OpCode.Subtract);
        public void Multiply() => _data.Emit(OpCode.Multiply);
        public void Divide() => _data.Emit(OpCode.Divide);
        public void Min() => _data.Emit(OpCode.Min);
        public void Max() => _data.Emit(OpCode.Max);

        /// <summary>Clamp the value using operands pushed in value, minimum, maximum order.</summary>
        public void Clamp() => _data.Emit(OpCode.Clamp);
        public void Negate() => _data.Emit(OpCode.Negate);

        public void Dispose()
        {
            _data?.Dispose();
            this = default;
        }
    }
}
