// Original: Transpose/Transpose/Attributes/ExternalAttribute.cs
using System;

namespace Transpose
{
    [AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Enum | AttributeTargets.Delegate | AttributeTargets.Interface | AttributeTargets.Method | AttributeTargets.Property, AllowMultiple = true)]
    public sealed class ExternalAttribute : Attribute
    {
    }
}
