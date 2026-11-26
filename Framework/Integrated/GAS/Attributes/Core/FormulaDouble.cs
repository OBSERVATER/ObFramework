using System;
using System.Runtime.CompilerServices;

namespace ObFramework.Integrated.GAS.Attributes.Core
{
    public readonly struct FormulaDouble : IFormula<double>
    {
        private readonly byte[] _bytecode;
        private readonly int _maxStackDepth;

        internal FormulaDouble(byte[] bytecode, int slotCount, int maxStackDepth)
        {
            _bytecode      = bytecode;
            SlotCount      = slotCount;
            _maxStackDepth = maxStackDepth;
        }

        public int SlotCount { get; }

        public double Calculate(double baseValue, ReadOnlySpan<double> slots)
        {
            if (_bytecode == null || _bytecode.Length == 0)
                return baseValue;

            Span<double> stack    = stackalloc double[_maxStackDepth];
            var          stackPtr = 0;

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
                        var value = ReadDouble(code, ref pc);
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
            var value = code[pc] | (code[pc + 1] << 8) | (code[pc + 2] << 16) | (code[pc + 3] << 24);
            pc += 4;
            return value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static long ReadInt64(ReadOnlySpan<byte> code, ref int pc)
        {
            var value =
                code[pc] |
                ((long)code[pc + 1] << 8) |
                ((long)code[pc + 2] << 16) |
                ((long)code[pc + 3] << 24) |
                ((long)code[pc + 4] << 32) |
                ((long)code[pc + 5] << 40) |
                ((long)code[pc + 6] << 48) |
                ((long)code[pc + 7] << 56);
            pc += 8;
            return value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double ReadDouble(ReadOnlySpan<byte> code, ref int pc) =>
            BitConverter.Int64BitsToDouble(ReadInt64(code, ref pc));
    }
}