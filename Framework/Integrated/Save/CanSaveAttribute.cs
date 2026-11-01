using System;

namespace ObFramework.Integrated.Save
{
    [System.AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class CanSaveAttribute : Attribute
    {
        private readonly string Key;

        public CanSaveAttribute(string key)
        {
            Key = key;
        }
    }
}
