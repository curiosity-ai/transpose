using System.Reflection;

namespace System
{
    [Transpose.Convention(Member = Transpose.ConventionMember.Field | Transpose.ConventionMember.Method, Notation = Transpose.Notation.CamelCase)]
    [Transpose.External]
    public sealed class AppDomain
    {
        private extern AppDomain();

        public extern Assembly[] GetAssemblies();

        public static extern AppDomain CurrentDomain
        {
            [Transpose.Template("System.AppDomain")]
            get;
        }
    }
}