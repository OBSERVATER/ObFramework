using System;

namespace ObFramework.Integrated.ViewBinding
{
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class BindViewAttribute : Attribute
    {
        public BindViewAttribute(Type viewType, Type contractType) { }
    }
}