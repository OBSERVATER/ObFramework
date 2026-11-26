namespace ObFramework.Integrated.GAS.Attributes.Core
{
    public abstract class AAttributeKey
    {
        public string Name { get; }

        protected AAttributeKey(string name)
        {
            Name = name;
        }
    }

    public sealed class AttributeKey<T> : AAttributeKey where T : struct
    {
        public AttributeKey(string name) : base(name)
        {
        }
    }
}