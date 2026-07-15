namespace System
{
    [Transpose.External]
    [Transpose.IgnoreCast]
    [Transpose.Constructor("{ }")]
    public class Object
    {
        public virtual extern object this[string name]
        {
            [Transpose.External]
            get;
            [Transpose.External]
            set;
        }

        [Transpose.Convention(Transpose.Notation.CamelCase)]
        [Transpose.Template("Transpose.toString({this})")]
        public virtual extern string ToString();

        [Transpose.Convention(Transpose.Notation.CamelCase)]
        public virtual extern string ToLocaleString();

        [Transpose.Convention(Transpose.Notation.CamelCase)]
        public virtual extern object ValueOf();

        [Transpose.Convention(Transpose.Notation.CamelCase)]
        public virtual extern bool HasOwnProperty(object v);

        [Transpose.Convention(Transpose.Notation.CamelCase)]
        public virtual extern bool IsPrototypeOf(object v);

        [Transpose.Convention(Transpose.Notation.CamelCase)]
        public virtual extern bool PropertyIsEnumerable(object v);

        [Transpose.Convention(Transpose.Notation.CamelCase)]
        [Transpose.Template("<self>{this:type}")]
        public extern Type GetType();

        // Returns a new object instance that is a memberwise copy of this
        // object.  This is always a shallow copy of the instance. The method is protected
        // so that other object may only call this method on themselves.  It is entended to
        // support the ICloneable interface.
        //
        // TODO: NotSupported
        //[System.Security.SecuritySafeCritical]  // auto-generated
        //[ResourceExposure(ResourceScope.None)]
        //[MethodImplAttribute(MethodImplOptions.InternalCall)]
        [Transpose.Template("Transpose.clone({this})")]
        protected extern object MemberwiseClone();

        [Transpose.Convention(Transpose.Notation.CamelCase)]
        [Transpose.Template("Transpose.referenceEquals({a}, {b})")]
        public static extern bool ReferenceEquals(object a, object b);

        [Transpose.Convention(Transpose.Notation.CamelCase)]
        [Transpose.Template("Transpose.equals({this}, {o})")]
        public virtual extern bool Equals(object o);

        [Transpose.Convention(Transpose.Notation.CamelCase)]
        [Transpose.Template("Transpose.equals({a}, {b})")]
        public static extern bool Equals(object a, object b);

        [Transpose.Convention(Transpose.Notation.CamelCase)]
        [Transpose.Template("Transpose.getHashCode({this})")]
        public virtual extern int GetHashCode();

        [Transpose.Convention(Transpose.Notation.CamelCase)]
        [Transpose.Template("Object.getOwnPropertyNames({obj})")]
        [Transpose.Unbox(true)]
        public static extern string[] GetOwnPropertyNames(object obj);

        [Transpose.Convention(Transpose.Notation.CamelCase)]
        [Transpose.Template("{T}.prototype")]
        public static extern dynamic GetPrototype<T>();

        public readonly Type Constructor;

#pragma warning disable 169
        private readonly Type ctor;
#pragma warning restore 169

        [Transpose.Template("{this}")]
        public virtual extern dynamic ToDynamic();
    }

    [Transpose.External]
    public static class ObjectExtensions
    {
        [Transpose.Template("{0}")]
        [Transpose.Unbox(true)]
        public static extern T As<T>(this object obj);

        [Transpose.Template("Transpose.cast({obj}, {T})")]
        public static extern T Cast<T>(this object obj);

        [Transpose.Template("Transpose.as({obj}, {T})")]
        public static extern T TryCast<T>(this object obj) where T : class;

        [Transpose.Template("Transpose.is({obj}, {T})")]
        public static extern bool Is<T>(this object obj);
    }
}