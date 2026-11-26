namespace ObFramework.Integrated.GAS.Attributes.Core
{
    public abstract class AttributeKey
    {
        public string Name { get; }

        protected AttributeKey(string name)
        {
            Name = name;
        }
    }

    public sealed class AttributeKey<T> : AttributeKey where T : struct
    {
        public AttributeKey(string name) : base(name)
        {
        }
    }
}