using System;

namespace Transpose
{
    [NonScriptable]
    [AttributeUsage( AttributeTargets.Delegate | AttributeTargets.Method, AllowMultiple = false)]
    public sealed class ToAwaitAttribute : Attribute
    {
    }
}