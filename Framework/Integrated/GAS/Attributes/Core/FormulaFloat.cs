using System;
using System.Runtime.CompilerServices;

namespace ObFramework.Integrated.GAS.Attributes.Core
{
    public readonly struct FormulaFloat : IFormula<float>
    {
        private readonly byte[] _bytecode;
        private readonly int _maxStackDepth;

        internal FormulaFloat(byte[] bytecode, int slotCount, int maxStackDepth)
        {
            _bytecode      = bytecode;
            SlotCount      = slotCount;
            _maxStackDepth = maxStackDepth;
        }

        public int SlotCount { get; }

        public float Calculate(float baseValue, ReadOnlySpan<float> slots)
        {
            if (_bytecode == null || _bytecode.Length == 0)
                return baseValue;

            Span<float> stack    = stackalloc float[_maxStackDepth];
            var         stackPtr = 0;

            ReadOnlySpan<byte> code = _bytecode;
            var                pc   = 0;

            while (pc < code.Length)
            {
                var opCode = (OpCode)code[pc++];

                switch (opCode)
                {
                    case OpCode.LoadBase:
                        stack[stackPtr++] = baseValue;
                        break;

                    case OpCode.LoadSlot:
                    {
                        var slotIndex = ReadInt32(code, ref pc);
                        var value     = slotIndex < slots.Length ? slots[slotIndex] : default;
                        stack[stackPtr++] = value;
                        break;
                    }

                    case OpCode.LoadConstant:
                    {
                        var value = ReadSingle(code, ref pc);
                        stack[stackPtr++] = value;
                        break;
                    }

                    case OpCode.Add:
                    {
                        var b = stack[--stackPtr];
                        var a = stack[--stackPtr];
                        stack[stackPtr++] = a + b;
                        break;
                    }

                    case OpCode.Subtract:
                    {
                        var b = stack[--stackPtr];
                        var a = stack[--stackPtr];
                        stack[stackPtr++] = a - b;
                        break;
                    }

                    case OpCode.Multiply:
                    {
                        var b = stack[--stackPtr];
                        var a = stack[--stackPtr];
                        stack[stackPtr++] = a * b;
                        break;
                    }

                    case OpCode.Divide:
                    {
                        var b = stack[--stackPtr];
                        var a = stack[--stackPtr];
                        stack[stackPtr++] = a / b;
                        break;
                    }
                }
            }

            return stackPtr > 0 ? stack[stackPtr - 1] : baseValue;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int ReadInt32(ReadOnlySpan<byte> code, ref int pc)
        {
            var value =
                code[pc] | (code[pc + 1] << 8) | (code[pc + 2] << 16) | (code[pc + 3] << 24);
            pc += 4;
            return value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float ReadSingle(ReadOnlySpan<byte> code, ref int pc) =>
            BitConverter.Int32BitsToSingle(ReadInt32(code, ref pc));
    }
}