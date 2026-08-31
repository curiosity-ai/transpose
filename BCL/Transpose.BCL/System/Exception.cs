using System.Collections.Generic;

namespace System
{
    [Transpose.Convention(Member = Transpose.ConventionMember.Field | Transpose.ConventionMember.Method, Notation = Transpose.Notation.CamelCase)]
    [Transpose.External]
    [Transpose.Reflectable]
    public class Exception : Transpose.ITransposeClass
    {
        /// <summary>
        /// Gets a collection of key/value pairs that provide additional user-defined information about the exception.
        /// </summary>
        public virtual extern IDictionary<object, object> Data
        {
            get;
        }

        /// <summary>
        /// Gets a message that describes the current exception.
        /// </summary>
        /// <remarks>
        /// Read through a helper for the same reason as <see cref="StackTrace"/>: a value caught by
        /// <c>catch (Exception)</c> may be a real <see cref="Exception"/>, a raw JavaScript error
        /// (both carry a <c>message</c>) or a bare value someone threw - a string, an object - which
        /// has no <c>message</c> field, so reading the member directly reported no message at all.
        /// </remarks>
        public virtual extern string Message
        {
            [Transpose.Template("TransposeR.message({this})")]
            get;
        }

        /// <summary>
        /// Gets the Exception instance that caused the current exception.
        /// </summary>
        public virtual extern Exception InnerException
        {
            get;
        }

        /// <summary>
        /// Retrieves the lowest exception (inner most) for the given Exception.
        /// This will traverse exceptions using the innerException property.
        /// </summary>
        /// <returns>The first exception thrown in a chain of exceptions. If the InnerException property of the current exception is a null reference</returns>
        public virtual extern Exception GetBaseException();

        /// <summary>
        /// Gets a string representation of the immediate frames on the call stack.
        /// </summary>
        /// <remarks>
        /// Read through a helper rather than the plain member: a value caught by
        /// <c>catch (Exception)</c> is either a real <see cref="Exception"/> — which carries the
        /// <c>errorStack</c> captured in its constructor — or a raw JavaScript error thrown by interop
        /// or a rejected promise, which has a native <c>stack</c> and no <c>errorStack</c>. C# matches
        /// both, so reading the member directly returned undefined for the raw-error case.
        /// </remarks>
        public virtual extern string StackTrace
        {
            [Transpose.Template("TransposeR.stackTrace({this})")]
            get;
        }

        public extern int HResult
        {
            get;
            protected set;
        }

        public extern Exception();

        public extern Exception(string message);

        public extern Exception(string message, Exception innerException);
    }
}