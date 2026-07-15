// Original: Transpose/Transpose/Attributes/AdapterAttribute.cs
using System;

namespace Transpose
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public abstract class AdapterAttribute : Attribute
    {
    }
}
