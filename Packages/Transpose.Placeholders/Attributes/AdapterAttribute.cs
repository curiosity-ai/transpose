// Original: H5/H5/Attributes/AdapterAttribute.cs
using System;

namespace H5
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public abstract class AdapterAttribute : Attribute
    {
    }
}
