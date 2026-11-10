using System;

namespace ObFramework.Adapter.Unity.ViewBinding
{
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class BindViewAttribute : Attribute
    {
        public BindViewAttribute(Type viewType, Type contractType) { }
    }
}
