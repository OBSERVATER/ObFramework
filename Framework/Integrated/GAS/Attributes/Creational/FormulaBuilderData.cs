using System;
using System.Buffers;
using ObFramework.Integrated.GAS.Attributes.Core;

namespace ObFramework.Integrated.GAS.Attributes.Creational
{
    internal sealed class FormulaBuilderData<T> where T : unmanaged
    {
        private Instruction[]? _instructions;
        private T[]? _constants;
        private int _instructionCount;
        private int _constantCount;

        internal FormulaBuilderData(int size)
        {
            _instructions = ArrayPool<Instruction>.Shared.Rent(size);
        }

        internal void Emit(OpCode code) => Append(new Instruction(code));

        private void Append(Instruction instruction)
        {
            if (_instructions == null)
                throw new ObjectDisposedException(nameof(FormulaBuilderData<T>));

            EnsureCapacity(ref _instructions, _instructionCount);
            _instructions![_instructionCount++] = instruction;
        }

        private static void EnsureCapacity<TItem>(ref TItem[]? buffer, int count) where TItem : unmanaged
        {
            if (buffer != null && count < buffer.Length)
                return;

            var pool = ArrayPool<TItem>.Shared;
            var next = pool.Rent(checked(count + Math.Max(count, 1)));
            if (buffer != null)
            {
                buffer.AsSpan(0, count).CopyTo(next);
                pool.Return(buffer);
            }
            buffer = next;
        }

        internal void LoadSlot(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex == int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(slotIndex));

            Append(new Instruction(OpCode.LoadSlot, slotIndex));
        }

        internal void LoadConstant(T value)
        {
            if (_instructions == null)
                throw new ObjectDisposedException(nameof(FormulaBuilderData<T>));

            EnsureCapacity(ref _constants, _constantCount);
            Append(new Instruction(OpCode.LoadConstant, _constantCount));
            _constants![_constantCount++] = value;
        }

        internal void Build(out Instruction[] instructions, out T[] constants, out int slots, out int maxDepth)
        {
            int depth = 0;
            slots = 0;
            maxDepth = 0;
            var used = _instructions.AsSpan(0, _instructionCount);
            foreach (var instruction in used)
            {
                switch (instruction.Code)
                {
                    case OpCode.LoadSlot:
                        slots = Math.Max(slots, instruction.Operand + 1);
                        goto case OpCode.LoadBase;
                    case OpCode.LoadBase:
                    case OpCode.LoadConstant:
                        maxDepth = Math.Max(maxDepth, ++depth);
                        break;
                    case OpCode.Add:
                    case OpCode.Subtract:
                    case OpCode.Multiply:
                    case OpCode.Divide:
                    case OpCode.Min:
                    case OpCode.Max:
                        if (depth < 2)
                            throw new InvalidOperationException("Binary operation requires two operands.");
                        depth--;
                        break;
                    case OpCode.Clamp:
                        if (depth < 3)
                            throw new InvalidOperationException("Clamp requires a value, minimum and maximum.");
                        depth -= 2;
                        break;
                    case OpCode.Negate:
                        if (depth < 1)
                            throw new InvalidOperationException("Negate requires one operand.");
                        break;
                    default:
                        throw new InvalidOperationException("Unsupported formula instruction.");
                }
            }

            if (!used.IsEmpty && depth != 1)
                throw new InvalidOperationException("Formula must produce one result.");

            instructions = used.ToArray();
            constants = _constants.AsSpan(0, _constantCount).ToArray();
        }

        internal void Dispose()
        {
            if (_instructions != null)
                ArrayPool<Instruction>.Shared.Return(_instructions);
            if (_constants != null)
                ArrayPool<T>.Shared.Return(_constants);
            _instructions = null;
            _constants = null;
            _instructionCount = 0;
            _constantCount = 0;
        }
    }
}
