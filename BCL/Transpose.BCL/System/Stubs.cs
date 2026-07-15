using System.ComponentModel;

namespace System
{
    [Transpose.External]
    public abstract class ValueType
    {
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    [Transpose.External]
    [Transpose.NonScriptable]
    public struct IntPtr
    {
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    [Transpose.External]
    [Transpose.NonScriptable]
    public struct UIntPtr
    {
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    [Transpose.External]
    [Transpose.NonScriptable]
    public class ParamArrayAttribute
    {
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    [Transpose.External]
    [Transpose.NonScriptable]
    public struct RuntimeTypeHandle
    {
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    [Transpose.External]
    [Transpose.NonScriptable]
    public struct RuntimeFieldHandle
    {
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    [Transpose.NonScriptable]
    public struct RuntimeMethodHandle
    {
    }
}