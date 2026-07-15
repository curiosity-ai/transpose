// Original: H5/H5/Attributes/VirtualAttribute.cs
using System;

namespace H5
{
    [AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Enum | AttributeTargets.Interface | AttributeTargets.Delegate, AllowMultiple = false)]
    public sealed class VirtualAttribute : Attribute
    {
        public VirtualAttribute()
        {
        }

        public VirtualAttribute(VirtualTarget target)
        {
        }
    }

    public enum VirtualTarget
    {
        All = 0,
        Class = 1,
        Interface = 2
    }
}
