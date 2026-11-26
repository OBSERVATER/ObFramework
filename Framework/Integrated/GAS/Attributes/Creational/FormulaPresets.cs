using ObFramework.Integrated.GAS.Attributes.Core;

namespace ObFramework.Integrated.GAS.Attributes.Creational
{
    public static partial class FormulaPresets
    {
        public static partial class Float
        {
            /// <summary>
            ///     (Base + Slot0) * Slot1.
            /// </summary>
            public static FormulaFloat Default()
            {
                using var builder = Formulas.CreateFloat();
                builder.LoadBase();
                builder.LoadSlot(0);
                builder.Add();
                builder.LoadSlot(1);
                builder.Multiply();
                return builder.Build();
            }

            /// <summary>
            ///     ((Base + Slot0) * Slot1 + Slot2) * Slot3.
            /// </summary>
            public static FormulaFloat Wow()
            {
                using var builder = Formulas.CreateFloat();
                builder.LoadBase();
                builder.LoadSlot(0);
                builder.Add();
                builder.LoadSlot(1);
                builder.Multiply();
                builder.LoadSlot(2);
                builder.Add();
                builder.LoadSlot(3);
                builder.Multiply();
                return builder.Build();
            }
        }

        public static partial class Double
        {
            /// <summary>
            ///     (Base + Slot0) * Slot1.
            /// </summary>
            public static FormulaDouble Default()
            {
                using var builder = Formulas.CreateDouble();
                builder.LoadBase();
                builder.LoadSlot(0);
                builder.Add();
                builder.LoadSlot(1);
                builder.Multiply();
                return builder.Build();
            }

            /// <summary>
            ///     ((Base + Slot0) * Slot1 + Slot2) * Slot3.
            /// </summary>
            public static FormulaDouble Wow()
            {
                using var builder = Formulas.CreateDouble();
                builder.LoadBase();
                builder.LoadSlot(0);
                builder.Add();
                builder.LoadSlot(1);
                builder.Multiply();
                builder.LoadSlot(2);
                builder.Add();
                builder.LoadSlot(3);
                builder.Multiply();
                return builder.Build();
            }
        }
    }
}