// Original: H5/H5/Attributes/NamespaceAttribute.cs
using System;

namespace H5
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
