// Original: H5/H5/Attributes/NameAttribute.cs
using System;

namespace H5
{
    [AttributeUsage(AttributeTargets.Enum | AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Method | AttributeTargets.Interface | AttributeTargets.Field | AttributeTargets.Delegate | AttributeTargets.Property | AttributeTargets.Parameter | AttributeTargets.Constructor)]
    public sealed class NameAttribute : Attribute
    {
        public NameAttribute(string value)
        {
        }
    }
}
