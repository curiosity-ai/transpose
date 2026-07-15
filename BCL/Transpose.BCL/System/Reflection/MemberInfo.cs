namespace System.Reflection
{
    [Transpose.Convention(Member = Transpose.ConventionMember.Field | Transpose.ConventionMember.Method, Notation = Transpose.Notation.CamelCase)]
    [Transpose.External]
    [Transpose.Name("System.Object")]
    public class MemberInfo
    {
        [Transpose.Name("t")]
        public extern MemberTypes MemberType
        {
            get;
        }

        [Transpose.Name("n")]
        public extern string Name
        {
            get;
        }

        [Transpose.Name("td")]
        public extern Type DeclaringType
        {
            get;
        }

        public extern bool IsStatic
        {
            [Transpose.Template("({this}.is || false)")]
            get;
        }

        public extern bool IsOverride
        {
            [Transpose.Template("({this}.ov || false)")]
            get;
        }

        public extern bool IsVirtual
        {
            [Transpose.Template("({this}.v || false)")]
            get;
        }

        public extern bool IsAbstract
        {
            [Transpose.Template("({this}.ab || false)")]
            get;
        }

        public extern bool IsSealed
        {
            [Transpose.Template("({this}.sl || false)")]
            get;
        }

        public extern bool IsSpecialName
        {
            [Transpose.Template("({this}.sy || false)")]
            get;
        }

        public extern bool IsFamily
        {
            [Transpose.Template("({this}.a === 3)")]
            get;
        }

        public extern bool IsFamilyOrAssembly
        {
            [Transpose.Template("({this}.a === 5)")]
            get;
        }

        public extern bool IsFamilyAndAssembly
        {
            [Transpose.Template("({this}.a === 6)")]
            get;
        }

        public extern bool IsPrivate
        {
            [Transpose.Template("({this}.a === 1)")]
            get;
        }

        public extern bool IsPublic
        {
            [Transpose.Template("({this}.a === 2)")]
            get;
        }

        public extern bool IsAssembly
        {
            [Transpose.Template("({this}.a === 4)")]
            get;
        }

        /// <summary>
        /// Returns an array of all custom attributes applied to this member.
        /// </summary>
        /// <param name="inherit">Ignored for members. Base members will never be considered.</param>
        /// <returns>An array that contains all the custom attributes applied to this member, or an array with zero elements if no attributes are defined. </returns>
        [Transpose.Template("System.Attribute.getCustomAttributes({this}, false, {inherit})")]
        public extern object[] GetCustomAttributes(bool inherit);

        /// <summary>
        /// Returns an array of custom attributes applied to this member and identified by <see cref="T:System.Type"/>.
        /// </summary>
        /// <param name="attributeType">The type of attribute to search for. Only attributes that are assignable to this type are returned. </param>
        /// <param name="inherit">Ignored for members. Base members will never be considered.</param>
        /// <returns>An array that contains all the custom attributes applied to this member, or an array with zero elements if no attributes are defined.</returns>
        [Transpose.Template("System.Attribute.getCustomAttributes({this}, {attributeType}, {inherit})")]
        public extern object[] GetCustomAttributes(Type attributeType, bool inherit);

        /// <summary>
        /// Returns an array of all custom attributes applied to this member.
        /// </summary>
        /// <returns>An array that contains all the custom attributes applied to this member, or an array with zero elements if no attributes are defined. </returns>
        [Transpose.Template("System.Attribute.getCustomAttributes({this}, false)")]
        public extern object[] GetCustomAttributes();

        /// <summary>
        /// Returns an array of custom attributes applied to this member and identified by <see cref="T:System.Type"/>.
        /// </summary>
        /// <param name="attributeType">The type of attribute to search for. Only attributes that are assignable to this type are returned. </param>
        /// <returns>An array that contains all the custom attributes applied to this member, or an array with zero elements if no attributes are defined.</returns>
        [Transpose.Template("System.Attribute.getCustomAttributes({this}, {attributeType})")]
        public extern object[] GetCustomAttributes(Type attributeType);

        [Transpose.Template("System.Attribute.isDefined({this}, {attributeType}, {inherit})")]
        public extern bool IsDefined(Type attributeType, bool inherit);

        public extern bool ContainsGenericParameters
        {
            [Transpose.Template("Transpose.Reflection.containsGenericParameters({this})")]
            get;
        }

        internal extern MemberInfo();
    }
}