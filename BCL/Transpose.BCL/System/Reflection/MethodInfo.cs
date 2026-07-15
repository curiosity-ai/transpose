namespace System.Reflection
{
    [Transpose.Convention(Member = Transpose.ConventionMember.Field | Transpose.ConventionMember.Method, Notation = Transpose.Notation.CamelCase)]
    [Transpose.External]
    [Transpose.Unbox(true)]
    public class MethodInfo : MethodBase
    {
        [Transpose.Name("rt")]
        public extern Type ReturnType
        {
            get;
            private set;
        }

        /// <summary>
        /// Returns an array of all custom attributes applied to this member.
        /// </summary>
        /// <param name="inherit">Ignored for members. Base members will never be considered.</param>
        /// <returns>An array that contains all the custom attributes applied to this member, or an array with zero elements if no attributes are defined. </returns>
        [Transpose.Template("({this}.rta || [])")]
        public extern object[] GetReturnTypeCustomAttributes(bool inherit);

        /// <summary>
        /// Returns an array of custom attributes applied to this member and identified by <see cref="T:System.Type"/>.
        /// </summary>
        /// <param name="attributeType">The type of attribute to search for. Only attributes that are assignable to this type are returned. </param>
        /// <param name="inherit">Ignored for members. Base members will never be considered.</param>
        /// <returns>An array that contains all the custom attributes applied to this member, or an array with zero elements if no attributes are defined.</returns>
        [Transpose.Template("({this}.rta || []).filter(function (a) { return Transpose.is(a, {attributeType}); })")]
        public extern object[] GetReturnTypeCustomAttributes(Type attributeType, bool inherit);

        /// <summary>
        /// Returns an array of all custom attributes applied to this member.
        /// </summary>
        /// <returns>An array that contains all the custom attributes applied to this member, or an array with zero elements if no attributes are defined. </returns>
        [Transpose.Template("({this}.rta || [])")]
        public extern object[] GetReturnTypeCustomAttributes();

        /// <summary>
        /// Returns an array of custom attributes applied to this member and identified by <see cref="T:System.Type"/>.
        /// </summary>
        /// <param name="attributeType">The type of attribute to search for. Only attributes that are assignable to this type are returned. </param>
        /// <returns>An array that contains all the custom attributes applied to this member, or an array with zero elements if no attributes are defined.</returns>
        [Transpose.Template("({this}.rta || []).filter(function (a) { return Transpose.is(a, {attributeType}); })")]
        public extern object[] GetReturnTypeCustomAttributes(Type attributeType);

        [Transpose.Template("Transpose.Reflection.midel({this})")]
        public extern Delegate CreateDelegate(Type delegateType);

        [Transpose.Template("Transpose.Reflection.midel({this}, {target})")]
        public extern Delegate CreateDelegate(Type delegateType, object target);

        [Transpose.Template("Transpose.Reflection.midel({this})")]
        public extern Delegate CreateDelegate();

        [Transpose.Template("Transpose.Reflection.midel({this}, {target})")]
        public extern Delegate CreateDelegate(object target);

        [Transpose.Template("Transpose.Reflection.midel({this}, null, {typeArguments})")]
        public extern Delegate CreateDelegate(Type[] typeArguments);

        [Transpose.Template("Transpose.Reflection.midel({this}, {target}, {typeArguments})")]
        public extern Delegate CreateDelegate(object target, Type[] typeArguments);

        public extern int TypeParameterCount
        {
            [Transpose.Template("({this}.tpc || 0)")]
            get;
            [Transpose.Template("X")]
            private set;
        }

        public extern bool IsGenericMethodDefinition
        {
            [Transpose.Template("Transpose.Reflection.isGenericMethodDefinition({this})")]
            get;
            [Transpose.Template("X")]
            private set;
        }

        public extern bool IsGenericMethod
        {
            [Transpose.Template("Transpose.Reflection.isGenericMethod({this})")]
            get;
            [Transpose.Template("X")]
            private set;
        }

        [Transpose.Template("Transpose.Reflection.midel({this}, {obj})({*arguments})", "Transpose.Reflection.midel({this}, {obj}).apply(null, {arguments:array})")]
        public extern object Invoke(object obj, params object[] arguments);

        [Transpose.Template("Transpose.Reflection.midel({this}, {obj}, {typeArguments})({*arguments})", "Transpose.Reflection.midel({this}, {obj}, {typeArguments}).apply(null, {arguments:array})")]
        public extern object Invoke(object obj, Type[] typeArguments, params object[] arguments);

        /// <summary>
        /// Script name of the method. Null if the method has a special implementation.
        /// </summary>
        [Transpose.Name("sn")]
        public extern string ScriptName
        {
            get;
            private set;
        }

        /// <summary>
        /// For methods with a special implementation (eg. [Transpose.Template]), contains a delegate that represents the method. Null for normal methods.
        /// </summary>
        [Transpose.Name("def")]
        public extern Delegate SpecialImplementation
        {
            get;
            private set;
        }

        /// <summary>
        /// Whether the [ExpandParams] attribute was specified on the method.
        /// </summary>
        public extern bool IsExpandParams
        {
            [Transpose.Template("{this}.exp || false")]
            get;

            [Transpose.Template("{this}.exp = {value}")]
            private set;
        }

        /// <summary>
        /// Returns an array of Type objects that represent the type arguments of a generic method or the type parameters of a generic method definition.
        /// </summary>
        /// <returns>An array of Type objects that represent the type arguments of a generic method or the type parameters of a generic method definition. Returns an empty array if the current method is not a generic method.</returns>
        [Transpose.Template("Transpose.Reflection.getMethodGenericArguments({this})")]
        public extern Type[] GetGenericArguments();

        [Transpose.Template("Transpose.Reflection.makeGenericMethod({this}, {typeArguments:array})")]
        public extern MethodInfo MakeGenericMethod(params Type[] typeArguments);

        [Transpose.Template("Transpose.Reflection.getGenericMethodDefinition({this})")]
        public extern System.Reflection.MethodInfo GetGenericMethodDefinition();

        internal extern MethodInfo();
    }
}