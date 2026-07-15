// Original: Transpose/Transpose/Attributes/TemplateAttribute.cs
using System;

namespace Transpose
{
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Field | AttributeTargets.Constructor)]
    public sealed class TemplateAttribute : Attribute
    {
        internal TemplateAttribute()
        {
        }

        public TemplateAttribute(string format)
        {
        }

        public TemplateAttribute(string format, string nonExpandedFormat)
        {
        }

        public string Fn
        {
            get; set;
        }
    }
}
