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
            public static FormulaFloat Default() => CachedDefault;

            private static readonly FormulaFloat CachedDefault = BuildDefault();

            private static FormulaFloat BuildDefault()
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
            public static FormulaFloat Wow() => CachedWow;

            private static readonly FormulaFloat CachedWow = BuildWow();

            private static FormulaFloat BuildWow()
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
            public static FormulaDouble Default() => CachedDefault;

            private static readonly FormulaDouble CachedDefault = BuildDefault();

            private static FormulaDouble BuildDefault()
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
            public static FormulaDouble Wow() => CachedWow;

            private static readonly FormulaDouble CachedWow = BuildWow();

            private static FormulaDouble BuildWow()
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