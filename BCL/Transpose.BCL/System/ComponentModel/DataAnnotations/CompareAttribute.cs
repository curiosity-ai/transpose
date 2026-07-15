namespace System.ComponentModel.DataAnnotations
{
    [Transpose.Convention(Member = Transpose.ConventionMember.Field | Transpose.ConventionMember.Method, Notation = Transpose.Notation.CamelCase)]
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    [Transpose.External]
    [Transpose.NonScriptable]
    public class CompareAttribute : ValidationAttribute
    {
        public extern CompareAttribute(string otherProperty);

        public extern string OtherProperty { get; private set; }

        public extern string OtherPropertyDisplayName { get; internal set; }
    }
}
