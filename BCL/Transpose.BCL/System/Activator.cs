namespace System
{
    [Transpose.Convention(Member = Transpose.ConventionMember.Field | Transpose.ConventionMember.Method, Notation = Transpose.Notation.CamelCase)]
    [Transpose.External]
    [Transpose.Name("System.Object")]
    public static class Activator
    {
        [Transpose.Template("Transpose.createInstance({type}, {arguments:array})", "Transpose.Reflection.applyConstructor({type}, {arguments:array})")]
        public static extern object CreateInstance(Type type, params object[] arguments);

        [Transpose.Template("Transpose.createInstance({T}, {arguments:array})", "Transpose.Reflection.applyConstructor({T}, {arguments:array})")]
        public static extern T CreateInstance<T>(params object[] arguments);

        [Transpose.Template("Transpose.createInstance({type})")]
        public static extern object CreateInstance(Type type);

        [Transpose.Template("Transpose.createInstance({type}, {nonPublic})")]
        public static extern object CreateInstance(Type type, bool nonPublic);

        [Transpose.Template("Transpose.createInstance({T})")]
        public static extern T CreateInstance<T>();
    }
}