using System;

namespace ObFramework.Integrated.GAS.Attributes
{
    /// <summary>
    ///  Op codes.
    /// </summary>
    public enum OpCode : byte
    {
        /// <summary>Push base value.</summary>
        LoadBase = 0,

        /// <summary>Push slot value.</summary>
        LoadSlot = 1,

        /// <summary>Push constant.</summary>
        LoadConstant = 2,

        /// <summary>Pop two values and push their sum.</summary>
        Add = 3,

        /// <summary>Pop two values and push their product.</summary>
        Multiply = 4,

        /// <summary>Pop two values and push their difference.</summary>
        Subtract = 5,

        /// <summary>Pop two values and push their quotient.</summary>
        Divide = 6
    }

    public interface IFormula<T> where T : struct
    {
        int SlotCount { get; }
        T Calculate(T baseValue, ReadOnlySpan<T> slots);
    }
}