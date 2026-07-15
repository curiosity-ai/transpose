namespace System.Reflection
{
    [Transpose.Convention(Member = Transpose.ConventionMember.Field | Transpose.ConventionMember.Method, Notation = Transpose.Notation.CamelCase)]
    [Transpose.External]
    [Transpose.Unbox(true)]
    public abstract class PropertyInfo : MemberInfo
    {
        [Transpose.Name("rt")]
        public extern Type PropertyType
        {
            get;
        }

        public extern Type[] IndexParameterTypes
        {
            [Transpose.Template("({this}.p || [])")]
            get;
        }

        [Transpose.Template("({this}.ipi || [])")]
        public extern ParameterInfo[] GetIndexParameters();

        public extern bool CanRead
        {
            [Transpose.Template("(!!{this}.g)")]
            get;
        }

        [Transpose.Convention(Transpose.Notation.CamelCase)]
        public extern bool IsIndexer
        {
            [Transpose.Template("({this}.i || false)")]
            get;
        }

        public extern bool CanWrite
        {
            [Transpose.Template("(!!{this}.s)")]
            get;
        }

        [Transpose.Name("g")]
        public extern MethodInfo GetMethod
        {
            get;
        }

        [Transpose.Name("s")]
        public extern MethodInfo SetMethod
        {
            get;
        }

        [Transpose.Template("Transpose.Reflection.midel({this}.g, {obj})()")]
        public extern object GetValue(object obj);

        [Transpose.Template("Transpose.Reflection.midel({this}.g, {obj}).apply(null, {index})")]
        public extern object GetValue(object obj, object[] index);

        [Transpose.Template("Transpose.Reflection.midel({this}.s, {obj:nobox})({value:nobox})")]
        public extern void SetValue(object obj, object value);

        [Transpose.Template("Transpose.Reflection.midel({this}.s, {obj:nobox}).apply(null, ({index:nobox} || []).concat({value:nobox}))")]
        public extern void SetValue(object obj, object value, object[] index);

        /// <summary>
        /// For properties implemented as fields, contains the name of the field. Null for properties implemented as get and set methods.
        /// </summary>
        [Transpose.Name("fn")]
        public extern string ScriptFieldName
        {
            get;
        }

        internal extern PropertyInfo();
    }
}