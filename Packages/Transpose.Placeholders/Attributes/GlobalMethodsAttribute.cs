// Original: H5/H5/Attributes/GlobalMethodsAttribute.cs
using System;

namespace H5
{
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = true)]
    public sealed class GlobalMethodsAttribute : Attribute
    {
        public GlobalMethodsAttribute()
        {
        }

        public GlobalMethodsAttribute(bool scoped)
        {
        }
    }
}
