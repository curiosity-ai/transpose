// Original: Transpose/Transpose/Attributes/ConstructorAttribute.cs
using System;

namespace Transpose
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
    public sealed class ConstructorAttribute : Attribute
    {
        public ConstructorAttribute(string value)
        {
        }
    }
}
