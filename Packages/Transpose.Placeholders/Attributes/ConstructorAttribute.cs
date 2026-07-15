// Original: H5/H5/Attributes/ConstructorAttribute.cs
using System;

namespace H5
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
    public sealed class ConstructorAttribute : Attribute
    {
        public ConstructorAttribute(string value)
        {
        }
    }
}
