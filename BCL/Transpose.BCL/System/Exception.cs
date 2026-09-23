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
        public virtual extern string Message
        {
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
        /// Read through a helper rather than the plain member. A <c>catch</c> maps a JavaScript error
        /// onto a real <see cref="Exception"/> whose <c>errorStack</c> is the original error, but a raw
        /// JavaScript error can still reach an <see cref="Exception"/>-typed value where no catch ran
        /// (handed over by hand-written JavaScript); it has a native <c>stack</c> and no
        /// <c>errorStack</c>, and reading the member directly returned undefined for it.
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