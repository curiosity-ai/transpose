// Original: H5/H5/Attributes/PriorityAttribute.cs
using System;

namespace H5
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Enum | AttributeTargets.Delegate | AttributeTargets.Interface, AllowMultiple = true)]
    public class PriorityAttribute : Attribute
    {
        public PriorityAttribute(int priority)
        {
        }
    }
}
