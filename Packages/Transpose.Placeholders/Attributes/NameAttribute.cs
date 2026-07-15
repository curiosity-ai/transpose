// Original: Transpose/Transpose/Attributes/NameAttribute.cs
using System;

namespace Transpose
{
    [AttributeUsage(AttributeTargets.Enum | AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Method | AttributeTargets.Interface | AttributeTargets.Field | AttributeTargets.Delegate | AttributeTargets.Property | AttributeTargets.Parameter | AttributeTargets.Constructor)]
    public sealed class NameAttribute : Attribute
    {
        public NameAttribute(string value)
        {
        }
    }
}
