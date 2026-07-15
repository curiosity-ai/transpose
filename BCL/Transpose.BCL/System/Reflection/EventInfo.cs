namespace System.Reflection
{
    [Transpose.Convention(Member = Transpose.ConventionMember.Field | Transpose.ConventionMember.Method, Notation = Transpose.Notation.CamelCase)]
    [Transpose.External]
    public abstract class EventInfo : MemberInfo
    {
        [Transpose.Name("ad")]
        public extern MethodInfo AddMethod
        {
            get;
            private set;
        }

        [Transpose.Name("r")]
        public extern MethodInfo RemoveMethod
        {
            get;
            private set;
        }

        [Transpose.Template("Transpose.Reflection.midel({this}.ad, {target})({handler})")]
        public extern void AddEventHandler(object target, Delegate handler);

        [Transpose.Template("Transpose.Reflection.midel({this}.r, {target})({handler})")]
        public extern void RemoveEventHandler(object target, Delegate handler);

        internal extern EventInfo();
    }
}