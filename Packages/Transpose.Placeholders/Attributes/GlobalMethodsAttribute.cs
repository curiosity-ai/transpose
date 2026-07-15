// Original: Transpose/Transpose/Attributes/GlobalMethodsAttribute.cs
using System;

namespace Transpose
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
