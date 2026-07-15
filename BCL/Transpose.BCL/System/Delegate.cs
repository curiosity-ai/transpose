using System.Reflection;

namespace System
{
    [Transpose.Convention(Member = Transpose.ConventionMember.Field | Transpose.ConventionMember.Method, Notation = Transpose.Notation.CamelCase)]
    [Transpose.External]
    [Transpose.IgnoreCast]
    [Transpose.Name("Function")]
    public class Delegate
    {
        public extern int Length
        {
            [Transpose.Template("{this}.length")]
            get;
        }

        protected extern Delegate(object target, string method);

        protected extern Delegate(Type target, string method);

        protected extern Delegate();

        public virtual extern object Apply(object thisArg);

        public virtual extern object Apply();

        public virtual extern object Apply(object thisArg, Array args);

        public virtual extern object Call(object thisArg, params object[] args);

        public virtual extern object Call(object thisArg);

        public virtual extern object Call();

        [Transpose.Template("{this}.apply(null, {args})")]
        public virtual extern object DynamicInvoke(params object[] args);

        [Transpose.Template("Transpose.fn.combine({0}, {1})")]
        public static extern Delegate Combine(Delegate a, Delegate b);

        [Transpose.Template("Transpose.fn.remove({0}, {1})")]
        public static extern Delegate Remove(Delegate source, Delegate value);

        [Transpose.Template("Transpose.staticEquals({a}, {b})")]
        public static extern bool operator ==(Delegate a, Delegate b);

        [Transpose.Template("!Transpose.staticEquals({a}, {b})")]
        public static extern bool operator !=(Delegate a, Delegate b);

        [Transpose.Template("Transpose.Reflection.createDelegate({method}, {firstArgument})")]
        public static extern Delegate CreateDelegate(Type type, object firstArgument, MethodInfo method);

        [Transpose.Template("Transpose.fn.getInvocationList({this})")]
        public extern Delegate[] GetInvocationList();
    }

    [Transpose.Convention(Member = Transpose.ConventionMember.Field | Transpose.ConventionMember.Method, Notation = Transpose.Notation.CamelCase)]
    [Transpose.External]
    [Transpose.IgnoreCast]
    [Transpose.Name("Function")]
    public class MulticastDelegate : Delegate
    {
        protected extern MulticastDelegate();

        protected extern MulticastDelegate(object target, string method);

        protected extern MulticastDelegate(Type target, string method);

        [Transpose.Template("Transpose.staticEquals({a}, {b})")]
        public static extern bool operator ==(MulticastDelegate a, MulticastDelegate b);

        [Transpose.Template("!Transpose.staticEquals({a}, {b})")]
        public static extern bool operator !=(MulticastDelegate a, MulticastDelegate b);
    }
}