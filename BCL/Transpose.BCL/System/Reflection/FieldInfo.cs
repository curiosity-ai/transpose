using System.ComponentModel;

namespace System.Reflection
{
    [Transpose.Convention(Member = Transpose.ConventionMember.Field | Transpose.ConventionMember.Method, Notation = Transpose.Notation.CamelCase)]
    [Transpose.External]
    [Transpose.Unbox(true)]
    public abstract partial class FieldInfo : MemberInfo
    {
        [Transpose.Name("rt")]
        public extern Type FieldType
        {
            get;
            private set;
        }

        [Transpose.Convention(Transpose.Notation.CamelCase)]
        public extern bool IsInitOnly
        {
            [Transpose.Template("({this}.ro || false)")]
            get;
        }

        [Transpose.Template("Transpose.Reflection.fieldAccess({this}, {obj})")]
        public extern object GetValue(object obj);

        [Transpose.Template("Transpose.Reflection.fieldAccess({this}, {obj}, {value})")]
        public extern void SetValue(object obj, object value);

        /// <summary>
        /// Script name of the field
        /// </summary>
        [Transpose.Name("sn")]
        public extern string ScriptName
        {
            get;
            private set;
        }

        [Transpose.NonScriptable, EditorBrowsable(EditorBrowsableState.Never)]
        public static extern FieldInfo GetFieldFromHandle(RuntimeFieldHandle h);

        [Transpose.NonScriptable, EditorBrowsable(EditorBrowsableState.Never)]
        public static extern FieldInfo GetFieldFromHandle(RuntimeFieldHandle h, RuntimeTypeHandle x);

        internal extern FieldInfo();
    }
}