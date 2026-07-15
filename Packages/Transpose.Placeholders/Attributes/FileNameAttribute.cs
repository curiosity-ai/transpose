// Original: H5/H5/Attributes/FileNameAttribute.cs
using System;

namespace H5
{
    [AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class | AttributeTargets.Enum | AttributeTargets.Struct | AttributeTargets.Interface)]
    public sealed class FileNameAttribute : Attribute
    {
        public FileNameAttribute(string filename)
        {
        }
    }
}
