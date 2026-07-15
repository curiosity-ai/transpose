// Original: H5/H5/Attributes/ExternalAttribute.cs
using System;

namespace H5
{
    [AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Enum | AttributeTargets.Delegate | AttributeTargets.Interface | AttributeTargets.Method | AttributeTargets.Property, AllowMultiple = true)]
    public sealed class ExternalAttribute : Attribute
    {
    }
}
