using System;
using System.Buffers;
using ObFramework.Integrated.GAS.Attributes.Core;

namespace ObFramework.Integrated.GAS.Attributes.Creational
{
    public ref struct FormulaBuilderFloat
    {
        private static readonly ArrayPool<byte> Pool = ArrayPool<byte>.Shared;

        private byte[] _buffer;
        private int _index;

        internal static FormulaBuilderFloat Create(int size) =>
            new()
            {
                _buffer = Pool.Rent(size),
                _index  = 0
            };

        public FormulaFloat Build()
        {
            var length   = _index;
            var bytecode = new byte[length];
            _buffer.AsSpan(0, length).CopyTo(bytecode);

            Analyze(bytecode, out var slotCount, out var maxStackDepth);
            return new FormulaFloat(bytecode, slotCount, maxStackDepth);
        }

        public void LoadBase() => WriteOpCode(OpCode.LoadBase);

        public void LoadSlot(int slotIndex)
        {
            if (slotIndex < 0) throw new ArgumentOutOfRangeException(nameof(slotIndex));
            WriteOpCode(OpCode.LoadSlot);
            WriteInt32(slotIndex);
        }

        public void LoadConstant(float value)
        {
            WriteOpCode(OpCode.LoadConstant);
            WriteSingle(value);
        }

        public void Add() => WriteOpCode(OpCode.Add);

        public void Subtract() => WriteOpCode(OpCode.Subtract);

        public void Multiply() => WriteOpCode(OpCode.Multiply);

        public void Divide() => WriteOpCode(OpCode.Divide);

        internal void WriteOpCode(OpCode op)
        {
            EnsureCapacity(1);
            _buffer[_index++] = (byte)op;
        }

        internal void WriteInt32(int value)
        {
            EnsureCapacity(4);
            _buffer[_index + 0] =  (byte)value;
            _buffer[_index + 1] =  (byte)(value >> 8);
            _buffer[_index + 2] =  (byte)(value >> 16);
            _buffer[_index + 3] =  (byte)(value >> 24);
            _index              += 4;
        }

        internal void WriteSingle(float value) => WriteInt32(BitConverter.SingleToInt32Bits(value));

        private void EnsureCapacity(int sizeHint)
        {
            var required = _index + sizeHint;
            if (required <= _buffer.Length)
                return;
            Grow(required);
        }

        private void Grow(int minCapacity)
        {
            var newSize = Math.Max(_buffer.Length * 2, minCapacity);

            var oldBuffer = _buffer;
            var newBuffer = Pool.Rent(newSize);
            oldBuffer.AsSpan(0, _index).CopyTo(newBuffer);
            Pool.Return(oldBuffer, true);
            _buffer = newBuffer;
        }

        private static void Analyze(byte[] bytecode, out int slotCount, out int maxStackDepth)
        {
            var pc           = 0;
            var stackDepth   = 0;
            var maxDepth     = 0;
            var maxSlotIndex = -1;

            while (pc < bytecode.Length)
            {
                var opCode = (OpCode)bytecode[pc++];
                switch (opCode)
                {
                    case OpCode.LoadBase:
                        stackDepth++;
                        maxDepth = Math.Max(maxDepth, stackDepth);
                        break;

                    case OpCode.LoadSlot:
                        if (pc + 4 > bytecode.Length)
                            throw new InvalidOperationException("Invalid formula bytecode.");
                        var slotIndex = ReadInt32(bytecode, pc);
                        pc += 4;
                        if (slotIndex < 0)
                            throw new InvalidOperationException("Slot index cannot be negative.");
                        if (slotIndex > maxSlotIndex)
                            maxSlotIndex = slotIndex;
                        stackDepth++;
                        maxDepth = Math.Max(maxDepth, stackDepth);
                        break;

                    case OpCode.LoadConstant:
                        if (pc + 4 > bytecode.Length)
                            throw new InvalidOperationException("Invalid formula bytecode.");
                        pc += 4;
                        stackDepth++;
                        maxDepth = Math.Max(maxDepth, stackDepth);
                        break;

                    case OpCode.Add:
                    case OpCode.Subtract:
                    case OpCode.Multiply:
                    case OpCode.Divide:
                        if (stackDepth < 2)
                            throw new InvalidOperationException("Invalid formula bytecode.");
                        stackDepth -= 1;
                        break;

                    default:
                        throw new InvalidOperationException("Invalid formula bytecode.");
                }
            }

            if (bytecode.Length != 0 && stackDepth != 1)
                throw new InvalidOperationException("Invalid formula bytecode.");

            slotCount     = maxSlotIndex + 1;
            maxStackDepth = maxDepth;
        }

        private static int ReadInt32(byte[] buffer, int offset) =>
            buffer[offset] | (buffer[offset + 1] << 8) | (buffer[offset + 2] << 16) | (buffer[offset + 3] << 24);

        public void Dispose()
        {
            var toReturn = _buffer;
            this = default;

            if (toReturn != null)
                Pool.Return(toReturn, true);
        }
    }
}