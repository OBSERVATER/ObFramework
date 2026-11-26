using System;

namespace ObFramework.Integrated.GAS.Attributes.Core
{
    public readonly struct FormulaFloat : IFormula<float>
    {
        private readonly Instruction[] _instructions;
        private readonly float[] _constants;
        private readonly int _maxStackDepth;

        internal FormulaFloat(Instruction[] instructions, float[] constants, int slotCount, int maxStackDepth)
        {
            _instructions = instructions;
            _constants = constants;
            SlotCount = slotCount;
            _maxStackDepth = maxStackDepth;
        }

        public int SlotCount { get; }

        public float Calculate(float baseValue, ReadOnlySpan<float> slots)
        {
            if (_instructions == null || _instructions.Length == 0)
                return baseValue;

            Span<float> stack = stackalloc float[_maxStackDepth];
            int depth = 0;
            foreach (var instruction in _instructions)
            {
                switch (instruction.Code)
                {
                    case OpCode.LoadBase:
                        stack[depth++] = baseValue;
                        break;
                    case OpCode.LoadSlot:
                        var slot = instruction.Operand;
                        stack[depth++] = slot < slots.Length ? slots[slot] : 0;
                        break;
                    case OpCode.LoadConstant:
                        stack[depth++] = _constants[instruction.Operand];
                        break;
                    case OpCode.Add:
                        depth--;
                        stack[depth - 1] += stack[depth];
                        break;
                    case OpCode.Subtract:
                        depth--;
                        stack[depth - 1] -= stack[depth];
                        break;
                    case OpCode.Multiply:
                        depth--;
                        stack[depth - 1] *= stack[depth];
                        break;
                    case OpCode.Divide:
                        depth--;
                        stack[depth - 1] /= stack[depth];
                        break;
                    case OpCode.Min:
                        depth--;
                        stack[depth - 1] = Math.Min(stack[depth - 1], stack[depth]);
                        break;
                    case OpCode.Max:
                        depth--;
                        stack[depth - 1] = Math.Max(stack[depth - 1], stack[depth]);
                        break;
                    case OpCode.Clamp:
                        depth -= 2;
                        var value = stack[depth - 1];
                        var minimum = stack[depth];
                        var maximum = stack[depth + 1];
                        if (minimum > maximum || value < minimum)
                            stack[depth - 1] = minimum;
                        else if (value > maximum)
                            stack[depth - 1] = maximum;
                        break;
                    case OpCode.Negate:
                        stack[depth - 1] = -stack[depth - 1];
                        break;
                }
            }

            return stack[0];
        }
    }
}
