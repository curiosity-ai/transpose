namespace System.Reflection
{
    [Transpose.Convention(Member = Transpose.ConventionMember.Field | Transpose.ConventionMember.Method, Notation = Transpose.Notation.CamelCase)]
    [Transpose.External]
    [Transpose.Unbox(true)]
    public abstract partial class ConstructorInfo : MethodBase
    {
        [Transpose.Template("Transpose.Reflection.invokeCI({this}, {arguments:array})")]
        public extern object Invoke(params object[] arguments);

        /// <summary>
        /// Script name of the constructor. Null for the unnamed constructor and for constructors with special implementations
        /// </summary>
        [Transpose.Name("sn")]
        public extern string ScriptName
        {
            get;
            private set;
        }

        /// <summary>
        /// True if the constructor is a normal method that returns the created instance and should be invoked without the 'new' operator
        /// </summary>
        public extern bool IsStaticMethod
        {
            [Transpose.Template("({this}.sm || false)")]
            get;
            [Transpose.Template("{this}.sm = {value}")]
            private set;
        }

        /// <summary>
        /// For constructors with a special implementation (eg. [Transpose.Template]), contains a delegate that can be invoked to create an instance.
        /// </summary>
        [Transpose.Name("def")]
        public extern Delegate SpecialImplementation
        {
            get;
            private set;
        }

        /// <summary>
        /// Whether the [ExpandParams] attribute was specified on the constructor.
        /// </summary>
        public extern bool IsExpandParams {[Transpose.Template("{this}.exp || false")] get;[Transpose.Template("{this}.exp = {value}")] private set; }

        internal extern ConstructorInfo();
    }
}