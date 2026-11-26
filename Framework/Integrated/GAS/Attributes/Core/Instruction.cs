namespace ObFramework.Integrated.GAS.Attributes.Core
{
    internal readonly struct Instruction
    {
        internal readonly OpCode Code;
        internal readonly int Operand;

        internal Instruction(OpCode code, int operand = 0)
        {
            Code = code;
            Operand = operand;
        }
    }
}
