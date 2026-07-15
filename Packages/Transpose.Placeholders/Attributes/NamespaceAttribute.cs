// Original: Transpose/Transpose/Attributes/NamespaceAttribute.cs
using System;

namespace Transpose
{
    [AttributeUsage(AttributeTargets.Enum | AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface)]
    public sealed class NamespaceAttribute : Attribute
    {
        public NamespaceAttribute(bool includeNamespace)
        {
        }

        public NamespaceAttribute(string ns)
        {
        }
    }
}
