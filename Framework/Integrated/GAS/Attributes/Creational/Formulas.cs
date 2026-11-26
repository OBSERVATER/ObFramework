namespace ObFramework.Integrated.GAS.Attributes.Creational
{
    public static partial class Formulas
    {
        private const int DefaultSize = 64;

        public static FormulaBuilderFloat CreateFloat(int size = DefaultSize) => FormulaBuilderFloat.Create(size);
        public static FormulaBuilderDouble CreateDouble(int size = DefaultSize) => FormulaBuilderDouble.Create(size);
    }
}