// Original: H5/H5/Attributes/NonScriptableAttribute.cs
using System;

namespace H5
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Enum | AttributeTargets.Interface | AttributeTargets.Constructor | AttributeTargets.Method | AttributeTargets.Property | AttributeTargets.Event | AttributeTargets.Field, Inherited = false, AllowMultiple = false)]
    public sealed class NonScriptableAttribute : Attribute
    {
    }
}
