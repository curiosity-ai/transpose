using System;

namespace Transpose
{
    [NonScriptable]
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Assembly | AttributeTargets.Class | AttributeTargets.Interface)]
    public sealed class FieldAttribute : Attribute
    {
    }
}