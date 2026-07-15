// Original: H5/H5/Attributes/ObjectLiteralAttribute.cs
using System;

namespace H5
{
    [AttributeUsage(AttributeTargets.Enum | AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface)]
    public sealed class ObjectLiteralAttribute : Attribute
    {
        public ObjectLiteralAttribute()
        {
        }

        public ObjectLiteralAttribute(ObjectInitializationMode initializationMode)
        {
        }

        public ObjectLiteralAttribute(ObjectCreateMode createMode)
        {
        }

        public ObjectLiteralAttribute(ObjectInitializationMode initializationMode, ObjectCreateMode createMode)
        {
        }
    }

    public enum ObjectInitializationMode
    {
        DefaultValue = 2,
        Initializer = 1,
        Ignore = 0
    }

    public enum ObjectCreateMode
    {
        Constructor = 1,
        Plain = 0
    }
}
