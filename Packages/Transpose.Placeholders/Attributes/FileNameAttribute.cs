// Original: Transpose/Transpose/Attributes/FileNameAttribute.cs
using System;

namespace Transpose
{
    [AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class | AttributeTargets.Enum | AttributeTargets.Struct | AttributeTargets.Interface)]
    public sealed class FileNameAttribute : Attribute
    {
        public FileNameAttribute(string filename)
        {
        }
    }
}
