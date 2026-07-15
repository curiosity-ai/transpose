using System.Collections.Generic;
using System.ComponentModel;
using System.Reflection;

namespace System.Linq.Expressions
{
    [Transpose.Convention(Member = Transpose.ConventionMember.Field | Transpose.ConventionMember.Method, Notation = Transpose.Notation.CamelCase)]
    [Transpose.External]
    [Transpose.Name("System.Object")]
    public abstract class Expression
    {
        [Transpose.Name("ntype")]
        public extern ExpressionType NodeType { get; private set; }

        [Transpose.Name("t")]
        public extern Type Type { get; private set; }

        [Transpose.Template("{ ntype: {nodeType}, t: {type} }")]
        protected extern Expression(ExpressionType nodeType, Type type);

        internal extern Expression();

        [Transpose.Template("{ ntype: {binaryType}, t: {right}.t, left: {left}, right: {right} }")]
        public static extern BinaryExpression MakeBinary(ExpressionType binaryType, Expression left, Expression right);

        [Transpose.Template("{ ntype: {binaryType}, t: {method}.rt, left: {left}, right: {right}, method: {method} }")]
        public static extern BinaryExpression MakeBinary(ExpressionType binaryType, Expression left, Expression right, MethodInfo method);

        //public static BinaryExpression MakeBinary(ExpressionType binaryType, Expression left, Expression right, bool liftToNull, MethodInfo method, LambdaExpression conversion) { return null; }

        [Transpose.Template("{ ntype: 46, t: {right}.t, left: {left}, right: {right} }")]
        public static extern BinaryExpression Assign(Expression left, Expression right);

        [Transpose.NonScriptable, EditorBrowsable(EditorBrowsableState.Never)]
        public static extern BinaryExpression Equal(Expression left, Expression right);

        [Transpose.NonScriptable, EditorBrowsable(EditorBrowsableState.Never)]
        public static extern BinaryExpression Equal(Expression left, Expression right, bool liftToNull, MethodInfo method);

        [Transpose.Template("{ ntype: 13, t: {type}, left: {left}, right: {right} }")]
        public static extern BinaryExpression Equal(Expression left, Expression right, Type type);

        [Transpose.Template("{ ntype: 13, t: {method}.rt, left: {left}, right: {right}, method: {method} }")]
        public static extern BinaryExpression Equal(Expression left, Expression right, MethodInfo method);

        [Transpose.NonScriptable, EditorBrowsable(EditorBrowsableState.Never)]
        public static extern BinaryExpression NotEqual(Expression left, Expression right);

        [Transpose.NonScriptable, EditorBrowsable(EditorBrowsableState.Never)]
        public static extern BinaryExpression NotEqual(Expression left, Expression right, bool liftToNull, MethodInfo method);

        [Transpose.Template("{ ntype: 35, t: {type}, left: {left}, right: {right} }")]
        public static extern BinaryExpression NotEqual(Expression left, Expression right, Type type);

        [Transpose.Template("{ ntype: 35, t: {method}.rt, left: {left}, right: {right}, method: {method} }")]
        public static extern BinaryExpression NotEqual(Expression left, Expression right, MethodInfo method);

        [Transpose.NonScriptable, EditorBrowsable(EditorBrowsableState.Never)]
        public static extern BinaryExpression GreaterThan(Expression left, Expression right);

        [Transpose.NonScriptable, EditorBrowsable(EditorBrowsableState.Never)]
        public static extern BinaryExpression GreaterThan(Expression left, Expression right, bool liftToNull, MethodInfo method);

        [Transpose.Template("{ ntype: 15, t: {type}, left: {left}, right: {right} }")]
        public static extern BinaryExpression GreaterThan(Expression left, Expression right, Type type);

        [Transpose.Template("{ ntype: 15, t: {method}.rt, left: {left}, right: {right}, method: {method} }")]
        public static extern BinaryExpression GreaterThan(Expression left, Expression right, MethodInfo method);

        [Transpose.NonScriptable, EditorBrowsable(EditorBrowsableState.Never)]
        public static extern BinaryExpression LessThan(Expression left, Expression right);

        [Transpose.NonScriptable, EditorBrowsable(EditorBrowsableState.Never)]
        public static extern BinaryExpression LessThan(Expression left, Expression right, bool liftToNull, MethodInfo method);

        [Transpose.Template("{ ntype: 20, t: {type}, left: {left}, right: {right} }")]
        public static extern BinaryExpression LessThan(Expression left, Expression right, Type type);

        [Transpose.Template("{ ntype: 20, t: {method}.rt, left: {left}, right: {right}, method: {method} }")]
        public static extern BinaryExpression LessThan(Expression left, Expression right, MethodInfo method);

        [Transpose.NonScriptable, EditorBrowsable(EditorBrowsableState.Never)]
        public static extern BinaryExpression GreaterThanOrEqual(Expression left, Expression right);

        [Transpose.NonScriptable, EditorBrowsable(EditorBrowsableState.Never)]
        public static extern BinaryExpression GreaterThanOrEqual(Expression left, Expression right, bool liftToNull, MethodInfo method);

        [Transpose.Template("{ ntype: 16, t: {type}, left: {left}, right: {right} }")]
        public static extern BinaryExpression GreaterThanOrEqual(Expression left, Expression right, Type type);

        [Transpose.Template("{ ntype: 16, t: {method}.rt, left: {left}, right: {right}, method: {method} }")]
        public static extern BinaryExpression GreaterThanOrEqual(Expression left, Expression right, MethodInfo method);

        [Transpose.NonScriptable, EditorBrowsable(EditorBrowsableState.Never)]
        public static extern BinaryExpression LessThanOrEqual(Expression left, Expression right);

        [Transpose.NonScriptable, EditorBrowsable(EditorBrowsableState.Never)]
        public static extern BinaryExpression LessThanOrEqual(Expression left, Expression right, bool liftToNull, MethodInfo method);

        [Transpose.Template("{ ntype: 21, t: {type}, left: {left}, right: {right} }")]
        public static extern BinaryExpression LessThanOrEqual(Expression left, Expression right, Type type);

        [Transpose.Template("{ ntype: 21, t: {method}.rt, left: {left}, right: {right}, method: {method} }")]
        public static extern BinaryExpression LessThanOrEqual(Expression left, Expression right, MethodInfo method);

        [Transpose.NonScriptable, EditorBrowsable(EditorBrowsableState.Never)]
        public static extern BinaryExpression AndAlso(Expression left, Expression right);

        [Transpose.NonScriptable, EditorBrowsable(EditorBrowsableState.Never)]
        public static extern BinaryExpression AndAlso(Expression left, Expression right, MethodInfo method);

        [Transpose.Template("{ ntype: 3, t: {type}, left: {left}, right: {right} }")]
        public static extern BinaryExpression AndAlso(Expression left, Expression right, Type type);

        [Transpose.NonScriptable, EditorBrowsable(EditorBrowsableState.Never)]
        public static extern BinaryExpression OrElse(Expression left, Expression right);

        [Transpose.NonScriptable, EditorBrowsable(EditorBrowsableState.Never)]
        public static extern BinaryExpression OrElse(Expression left, Expression right, MethodInfo method);

        [Transpose.Template("{ ntype: 37, t: {type}, left: {left}, right: {right} }")]
        public static extern BinaryExpression OrElse(Expression left, Expression right, Type type);

        [Transpose.NonScriptable, EditorBrowsable(EditorBrowsableState.Never)]
        public static extern BinaryExpression Coalesce(Expression left, Expression right);

        [Transpose.Template("{ ntype: 7, t: {type}, left: {left}, right: {right} }")]
        public static extern BinaryExpression Coalesce(Expression left, Expression right, Type type);

        //public static BinaryExpression Coalesce(Expression left, Expression right, LambdaExpression conversion) { return null; }

        [Transpose.NonScriptable, EditorBrowsable(EditorBrowsableState.Never)]
        public static extern BinaryExpression Add(Expression left, Expression right);

        [Transpose.Template("{ ntype: 0, t: {type}, left: {left}, right: {right} }")]
        public static extern BinaryExpression Add(Expression left, Expression right, Type type);

        [Transpose.Template("{ ntype: 0, t: {method}.rt, left: {left}, right: {right}, method: {method} }")]
        public static extern BinaryExpression Add(Expression left, Expression right, MethodInfo method);

        [Transpose.Template("{ ntype: 63, t: {type}, left: {left}, right: {right} }")]
        public static extern BinaryExpression AddAssign(Expression left, Expression right, Type type);

        [Transpose.Template("{ ntype: 63, t: {method}.rt, left: {left}, right: {right}, method: {method} }")]
        public static extern BinaryExpression AddAssign(Expression left, Expression right, MethodInfo method);

        //public static BinaryExpression AddAssign(Expression left, Expression right, MethodInfo method, LambdaExpression conversion) { return null; }

        [Transpose.Template("{ ntype: 74, t: {type}, left: {left}, right: {right} }")]
        public static extern BinaryExpression AddAssignChecked(Expression left, Expression right, Type type);

        [Transpose.Template("{ ntype: 74, t: {method}.rt, left: {left}, right: {right}, method: {method} }")]
        public static extern BinaryExpression AddAssignChecked(Expression left, Expression right, MethodInfo method);

        //public static BinaryExpression AddAssignChecked(Expression left, Expression right, MethodInfo method, LambdaExpression conversion) { return null; }

        [Transpose.NonScriptable, EditorBrowsable(EditorBrowsableState.Never)]
        public static extern BinaryExpression AddChecked(Expression left, Expression right);

        [Transpose.Template("{ ntype: 1, t: {type}, left: {left}, right: {right} }")]
        public static extern BinaryExpression AddChecked(Expression left, Expression right, Type type);

        [Transpose.Template("{ ntype: 1, t: {method}.rt, left: {left}, right: {right}, method: {method} }")]
        public static extern BinaryExpression AddChecked(Expression left, Expression right, MethodInfo method);

        [Transpose.NonScriptable, EditorBrowsable(EditorBrowsableState.Never)]
        public static extern BinaryExpression Subtract(Expression left, Expression right);

        [Transpose.Template("{ ntype: 42, t: {type}, left: {left}, right: {right} }")]
        public static extern BinaryExpression Subtract(Expression left, Expression right, Type type);

        [Transpose.Template("{ ntype: 42, t: {method}.rt, left: {left}, right: {right}, method: {method} }")]
        public static extern BinaryExpression Subtract(Expression left, Expression right, MethodInfo method);

        [Transpose.Template("{ ntype: 73, t: {type}, left: {left}, right: {right} }")]
        public static extern BinaryExpression SubtractAssign(Expression left, Expression right, Type type);

        [Transpose.Template("{ ntype: 73, t: {method}.rt, left: {left}, right: {right}, method: {method} }")]
        public static extern BinaryExpression SubtractAssign(Expression left, Expression right, MethodInfo method);

        //public static BinaryExpression SubtractAssign(Expression left, Expression right, MethodInfo method, LambdaExpression conversion) { return null; }

        [Transpose.Template("{ ntype: 76, t: {type}, left: {left}, right: {right} }")]
        public static extern BinaryExpression SubtractAssignChecked(Expression left, Expression right, Type type);

        [Transpose.Template("{ ntype: 76, t: {method}.rt, left: {left}, right: {right}, method: {method} }")]
        public static extern BinaryExpression SubtractAssignChecked(Expression left, Expression right, MethodInfo method);

        //public static BinaryExpression SubtractAssignChecked(Expression left, Expression right, MethodInfo method, LambdaExpression conversion) { return null; }

        [Transpose.NonScriptable, EditorBrowsable(EditorBrowsableState.Never)]
        public static extern BinaryExpression SubtractChecked(Expression left, Expression right);

        [Transpose.Template("{ ntype: 43, t: {type}, left: {left}, right: {right} }")]
        public static extern BinaryExpression SubtractChecked(Expression left, Expression right, Type type);

        [Transpose.Template("{ ntype: 43, t: {method}.rt, left: {left}, right: {right}, method: {method} }")]
        public static extern BinaryExpression SubtractChecked(Expression left, Expression right, MethodInfo method);

        [Transpose.NonScriptable, EditorBrowsable(EditorBrowsableState.Never)]
        public static extern BinaryExpression Divide(Expression left, Expression right);

        [Transpose.Template("{ ntype: 12, t: {type}, left: {left}, right: {right} }")]
        public static extern BinaryExpression Divide(Expression left, Expression right, Type type);

        [Transpose.Template("{ ntype: 12, t: {method}.rt, left: {left}, right: {right}, method: {method} }")]
        public static extern BinaryExpression Divide(Expression left, Expression right, MethodInfo method);

        [Transpose.Template("{ ntype: 65, t: {type}, left: {left}, right: {right} }")]
        public static extern BinaryExpression DivideAssign(Expression left, Expression right, Type type);

        [Transpose.Template("{ ntype: 65, t: {method}.rt, left: {left}, right: {right}, method: {method} }")]
        public static extern BinaryExpression DivideAssign(Expression left, Expression right, MethodInfo method);

        //public static BinaryExpression DivideAssign(Expression left, Expression right, MethodInfo method, LambdaExpression conversion) { return null; }

        [Transpose.NonScriptable, EditorBrowsable(EditorBrowsableState.Never)]
        public static extern BinaryExpression Modulo(Expression left, Expression right);

        [Transpose.Template("{ ntype: 25, t: {type}, left: {left}, right: {right} }")]
        public static extern BinaryExpression Modulo(Expression left, Expression right, Type type);

        [Transpose.Template("{ ntype: 25, t: {method}.rt, left: {left}, right: {right}, method: {method} }")]
        public static extern BinaryExpression Modulo(Expression left, Expression right, MethodInfo method);

        [Transpose.Template("{ ntype: 68, t: {type}, left: {left}, right: {right} }")]
        public static extern BinaryExpression ModuloAssign(Expression left, Expression right, Type type);

        [Transpose.Template("{ ntype: 68, t: {method}.rt, left: {left}, right: {right}, method: {method} }")]
        public static extern BinaryExpression ModuloAssign(Expression left, Expression right, MethodInfo method);

        //public static BinaryExpression ModuloAssign(Expression left, Expression right, MethodInfo method, LambdaExpression conversion) { return null; }

        [Transpose.NonScriptable, EditorBrowsable(EditorBrowsableState.Never)]
        public static extern BinaryExpression Multiply(Expression left, Expression right);

        [Transpose.Template("{ ntype: 26, t: {type}, left: {left}, right: {right} }")]
        public static extern BinaryExpression Multiply(Expression left, Expression right, Type type);

        [Transpose.Template("{ ntype: 26, t: {method}.rt, left: {left}, right: {right}, method: {method} }")]
        public static extern BinaryExpression Multiply(Expression left, Expression right, MethodInfo method);

        [Transpose.Template("{ ntype: 69, t: {type}, left: {left}, right: {right} }")]
        public static extern BinaryExpression MultiplyAssign(Expression left, Expression right, Type type);

        [Transpose.Template("{ ntype: 69, t: {method}.rt, left: {left}, right: {right}, method: {method} }")]
        public static extern BinaryExpression MultiplyAssign(Expression left, Expression right, MethodInfo method);

        //public static BinaryExpression MultiplyAssign(Expression left, Expression right, MethodInfo method, LambdaExpression conversion) { return null; }

        [Transpose.Template("{ ntype: 75, t: {type}, left: {left}, right: {right} }")]
        public static extern BinaryExpression MultiplyAssignChecked(Expression left, Expression right, Type type);

        [Transpose.Template("{ ntype: 75, t: {method}.rt, left: {left}, right: {right}, method: {method} }")]
        public static extern BinaryExpression MultiplyAssignChecked(Expression left, Expression right, MethodInfo method);

        //public static BinaryExpression MultiplyAssignChecked(Expression left, Expression right, MethodInfo method, LambdaExpression conversion) { return null; }

        [Transpose.NonScriptable, EditorBrowsable(EditorBrowsableState.Never)]
        public static extern BinaryExpression MultiplyChecked(Expression left, Expression right);

        [Transpose.Template("{ ntype: 27, t: {type}, left: {left}, right: {right} }")]
        public static extern BinaryExpression MultiplyChecked(Expression left, Expression right, Type type);

        [Transpose.Template("{ ntype: 27, t: {method}.rt, left: {left}, right: {right}, method: {method} }")]
        public static extern BinaryExpression MultiplyChecked(Expression left, Expression right, MethodInfo method);

        [Transpose.NonScriptable, EditorBrowsable(EditorBrowsableState.Never)]
        public static extern BinaryExpression LeftShift(Expression left, Expression right);

        [Transpose.Template("{ ntype: 19, t: {type}, left: {left}, right: {right} }")]
        public static extern BinaryExpression LeftShift(Expression left, Expression right, Type type);

        [Transpose.Template("{ ntype: 19, t: {method}.rt, left: {left}, right: {right}, method: {method} }")]
        public static extern BinaryExpression LeftShift(Expression left, Expression right, MethodInfo method);

        [Transpose.Template("{ ntype: 67, t: {type}, left: {left}, right: {right} }")]
        public static extern BinaryExpression LeftShiftAssign(Expression left, Expression right, Type type);

        [Transpose.Template("{ ntype: 67, t: {method}.rt, left: {left}, right: {right}, method: {method} }")]
        public static extern BinaryExpression LeftShiftAssign(Expression left, Expression right, MethodInfo method);

        //public static BinaryExpression LeftShiftAssign(Expression left, Expression right, MethodInfo method, LambdaExpression conversion) { return null; }

        [Transpose.NonScriptable, EditorBrowsable(EditorBrowsableState.Never)]
        public static extern BinaryExpression RightShift(Expression left, Expression right);

        [Transpose.Template("{ ntype: 41, t: {type}, left: {left}, right: {right} }")]
        public static extern BinaryExpression RightShift(Expression left, Expression right, Type type);

        [Transpose.Template("{ ntype: 41, t: {method}.rt, left: {left}, right: {right}, method: {method} }")]
        public static extern BinaryExpression RightShift(Expression left, Expression right, MethodInfo method);

        [Transpose.Template("{ ntype: 72, t: {type}, left: {left}, right: {right} }")]
        public static extern BinaryExpression RightShiftAssign(Expression left, Expression right, Type type);

        [Transpose.Template("{ ntype: 72, t: {method}.rt, left: {left}, right: {right}, method: {method} }")]
        public static extern BinaryExpression RightShiftAssign(Expression left, Expression right, MethodInfo method);

        //public static BinaryExpression RightShiftAssign(Expression left, Expression right, MethodInfo method, LambdaExpression conversion) { return null; }

        [Transpose.NonScriptable, EditorBrowsable(EditorBrowsableState.Never)]
        public static extern BinaryExpression And(Expression left, Expression right);

        [Transpose.Template("{ ntype: 2, t: {type}, left: {left}, right: {right} }")]
        public static extern BinaryExpression And(Expression left, Expression right, Type type);

        [Transpose.Template("{ ntype: 2, t: {method}.rt, left: {left}, right: {right}, method: {method} }")]
        public static extern BinaryExpression And(Expression left, Expression right, MethodInfo method);

        [Transpose.Template("{ ntype: 64, t: {type}, left: {left}, right: {right} }")]
        public static extern BinaryExpression AndAssign(Expression left, Expression right, Type type);

        [Transpose.Template("{ ntype: 64, t: {method}.rt, left: {left}, right: {right}, method: {method} }")]
        public static extern BinaryExpression AndAssign(Expression left, Expression right, MethodInfo method);

        //public static BinaryExpression AndAssign(Expression left, Expression right, MethodInfo method, LambdaExpression conversion) { return null; }

        [Transpose.NonScriptable, EditorBrowsable(EditorBrowsableState.Never)]
        public static extern BinaryExpression Or(Expression left, Expression right);

        [Transpose.Template("{ ntype: 36, t: {type}, left: {left}, right: {right} }")]
        public static extern BinaryExpression Or(Expression left, Expression right, Type type);

        [Transpose.Template("{ ntype: 36, t: {method}.rt, left: {left}, right: {right}, method: {method} }")]
        public static extern BinaryExpression Or(Expression left, Expression right, MethodInfo method);

        [Transpose.Template("{ ntype: 70, t: {type}, left: {left}, right: {right} }")]
        public static extern BinaryExpression OrAssign(Expression left, Expression right, Type type);

        [Transpose.Template("{ ntype: 70, t: {method}.rt, left: {left}, right: {right}, method: {method} }")]
        public static extern BinaryExpression OrAssign(Expression left, Expression right, MethodInfo method);

        //public static BinaryExpression OrAssign(Expression left, Expression right, MethodInfo method, LambdaExpression conversion) { return null; }

        [Transpose.NonScriptable, EditorBrowsable(EditorBrowsableState.Never)]
        public static extern BinaryExpression ExclusiveOr(Expression left, Expression right);

        [Transpose.Template("{ ntype: 14, t: {type}, left: {left}, right: {right} }")]
        public static extern BinaryExpression ExclusiveOr(Expression left, Expression right, Type type);

        [Transpose.Template("{ ntype: 14, t: {method}.rt, left: {left}, right: {right}, method: {method} }")]
        public static extern BinaryExpression ExclusiveOr(Expression left, Expression right, MethodInfo method);

        [Transpose.Template("{ ntype: 66, t: {type}, left: {left}, right: {right} }")]
        public static extern BinaryExpression ExclusiveOrAssign(Expression left, Expression right, Type type);

        [Transpose.Template("{ ntype: 66, t: {method}.rt, left: {left}, right: {right}, method: {method} }")]
        public static extern BinaryExpression ExclusiveOrAssign(Expression left, Expression right, MethodInfo method);

        //public static BinaryExpression ExclusiveOrAssign(Expression left, Expression right, MethodInfo method, LambdaExpression conversion) { return null; }

        [Transpose.Template("{ ntype: 39, t: {type}, left: {left}, right: {right} }")]
        public static extern BinaryExpression Power(Expression left, Expression right, Type type);

        [Transpose.Template("{ ntype: 39, t: {method}.rt, left: {left}, right: {right}, method: {method} }")]
        public static extern BinaryExpression Power(Expression left, Expression right, MethodInfo method);

        [Transpose.Template("{ ntype: 71, t: {type}, left: {left}, right: {right} }")]
        public static extern BinaryExpression PowerAssign(Expression left, Expression right, Type type);

        [Transpose.Template("{ ntype: 71, t: {method}.rt, left: {left}, right: {right}, method: {method} }")]
        public static extern BinaryExpression PowerAssign(Expression left, Expression right, MethodInfo method);

        //public static BinaryExpression PowerAssign(Expression left, Expression right, MethodInfo method, LambdaExpression conversion) { return null; }

        [Transpose.NonScriptable, EditorBrowsable(EditorBrowsableState.Never)]
        public static extern BinaryExpression ArrayIndex(Expression array, Expression index);

        [Transpose.Template("{ ntype: 5, t: {type}, left: {array}, right: {index} }")]
        public static extern BinaryExpression ArrayIndex(Type type, Expression array, Expression index);

        [Transpose.NonScriptable, EditorBrowsable(EditorBrowsableState.Never)]
        public static extern MethodCallExpression ArrayIndex(Expression array, params Expression[] indexes);

        [Transpose.Template("{ ntype: 6, t: {type}, obj: {array}, method: { t: 8, td: System.Array.type({type}, {indexes:array}.length), n: \"Get\", rt: {type}, p: System.Array.init({indexes:array}.length, System.Int32, {type}, true), def: function () { return System.Array.$get.call(this, arguments); } }, args: Transpose.toList({indexes:array}) }")]
        public static extern MethodCallExpression ArrayIndex(Type type, Expression array, params Expression[] indexes);

        [Transpose.Template("(function (a, b, c) { return { ntype: 6, t: a, obj: b, method: { t: 8, td: System.Array.type({type}, c.Count), n: \"Get\", rt: a, p: System.Array.init(c.Count, System.Int32, {type}, true), def: function () { return System.Array.$get.call(this, arguments); } }, args: c }; })({type}, {array}, Transpose.toList({indexes}))")]
        public static extern MethodCallExpression ArrayIndex(Type type, Expression array, IEnumerable<Expression> indexes);

        [Transpose.Template("{ ntype: 47, t: {expressions:array}[{expressions:array}.length - 1].t, expressions: Transpose.toList({expressions:array}) }")]
        public static extern BlockExpression Block(params Expression[] expressions);

        [Transpose.Template("(function (a) { return { ntype: 47, t: a.getItem(a.Count - 1).t, expressions: a }; })(Transpose.toList({expressions}))")]
        public static extern BlockExpression Block(IEnumerable<Expression> expressions);

        [Transpose.Template("{ ntype: 47, t: {type}, expressions: Transpose.toList({expressions:array}) }")]
        public static extern BlockExpression Block(Type type, params Expression[] expressions);

        [Transpose.Template("{ ntype: 47, t: {type}, expressions: Transpose.toList({expressions}) }")]
        public static extern BlockExpression Block(Type type, IEnumerable<Expression> expressions);

        [Transpose.Template("{ ntype: 47, t: {expressions:array}[{expressions:array}.length - 1].t, variables: Transpose.toList({variables}), expressions: Transpose.toList({expressions:array}) }")]
        public static extern BlockExpression Block(IEnumerable<ParameterExpression> variables, params Expression[] expressions);

        [Transpose.Template("(function (a, b) { return { ntype: 47, t: b.getItem(b.Count - 1).t, variables: a, expressions: b }; })(Transpose.toList({variables}), Transpose.toList({expressions}))")]
        public static extern BlockExpression Block(IEnumerable<ParameterExpression> variables, IEnumerable<Expression> expressions);

        [Transpose.Template("{ ntype: 47, t: {type}, variables: Transpose.toList({variables}), expressions: Transpose.toList({expressions:array}) }")]
        public static extern BlockExpression Block(Type type, IEnumerable<ParameterExpression> variables, params Expression[] expressions);

        [Transpose.Template("{ ntype: 47, t: {type}, variables: Transpose.toList({variables}), expressions: Transpose.toList({expressions}) }")]
        public static extern BlockExpression Block(Type type, IEnumerable<ParameterExpression> variables, IEnumerable<Expression> expressions);

        [Transpose.Template("{ ntype: 47, t: {expressions:array}[{expressions:array}.length - 1].t, variables: Transpose.toList({variables}), expressions: Transpose.toList({expressions:array}) }")]
        public static extern BlockExpression Block(ParameterExpression[] variables, params Expression[] expressions);

        [Transpose.Template("(function (a, b) { return { ntype: 47, t: b.getItem(b.Count - 1).t, variables: a, expressions: b }; })(Transpose.toList({variables}), Transpose.toList({expressions}))")]
        public static extern BlockExpression Block(ParameterExpression[] variables, IEnumerable<Expression> expressions);

        [Transpose.Template("{ ntype: 47, t: {type}, variables: Transpose.toList({variables}), expressions: Transpose.toList({expressions:array}) }")]
        public static extern BlockExpression Block(Type type, ParameterExpression[] variables, params Expression[] expressions);

        [Transpose.Template("{ ntype: 47, t: {type}, variables: Transpose.toList({variables}), expressions: Transpose.toList({expressions}) }")]
        public static extern BlockExpression Block(Type type, ParameterExpression[] variables, IEnumerable<Expression> expressions);

        [Transpose.Template("{ test: {type}, body: {body} }")]
        public static extern CatchBlock Catch(Type type, Expression body);

        [Transpose.Template("{ test: {variable}.t, variable: {variable}, body: {body} }")]
        public static extern CatchBlock Catch(ParameterExpression variable, Expression body);

        [Transpose.Template("{ test: {type}, body: {body}, filter: {filter} }")]
        public static extern CatchBlock Catch(Type type, Expression body, Expression filter);

        [Transpose.Template("{ test: {variable}.t, variable: {variable}, body: {body}, filter: {filter} }")]
        public static extern CatchBlock Catch(ParameterExpression variable, Expression body, Expression filter);

        [Transpose.Template("{ test: {type} || {variable}.t, variable: {variable}, body: {body}, filter: {filter} }")]
        public static extern CatchBlock MakeCatchBlock(Type type, ParameterExpression variable, Expression body, Expression filter);

        [Transpose.NonScriptable, EditorBrowsable(EditorBrowsableState.Never)]
        public static extern ConditionalExpression Condition(Expression test, Expression ifTrue, Expression ifFalse);

        [Transpose.Template("{ ntype: 8, t: {type}, test: {test}, ifTrue: {ifTrue}, ifFalse: {ifFalse} }")]
        public static extern ConditionalExpression Condition(Expression test, Expression ifTrue, Expression ifFalse, Type type);

        [Transpose.Template("{ ntype: 8, t: System.Object, test: {test}, ifTrue: {ifTrue}, ifFalse: { ntype: 51, t: System.Object } }")]
        public static extern ConditionalExpression IfThen(Expression test, Expression ifTrue);

        [Transpose.Template("{ ntype: 8, t: System.Object, test: {test}, ifTrue: {ifTrue}, ifFalse: {ifFalse} }")]
        public static extern ConditionalExpression IfThenElse(Expression test, Expression ifTrue, Expression ifFalse);

        [Transpose.NonScriptable, EditorBrowsable(EditorBrowsableState.Never)]
        public static extern ConstantExpression Constant(object value);

        [Transpose.Template("{ ntype: 9, t: {type}, value: {value} }")]
        public static extern ConstantExpression Constant(object value, Type type);

        [Transpose.Template("{ ntype: 9, t: {T}, value: {value} }")]
        public static extern ConstantExpression Constant<T>(T value);

        [Transpose.Template("{ ntype: 51, t: System.Object }")]
        public static extern DefaultExpression Empty();

        [Transpose.Template("{ ntype: 51, t: {type} }")]
        public static extern DefaultExpression Default(Type type);

        //public static DynamicExpression MakeDynamic(Type delegateType, CallSiteBinder binder, params Expression[] arguments) { return null; }
        //public static DynamicExpression MakeDynamic(Type delegateType, CallSiteBinder binder, IEnumerable<Expression> arguments) { return null; }
        //public static DynamicExpression MakeDynamic(Type delegateType, CallSiteBinder binder, Expression arg0) { return null; }
        //public static DynamicExpression MakeDynamic(Type delegateType, CallSiteBinder binder, Expression arg0, Expression arg1) { return null; }
        //public static DynamicExpression MakeDynamic(Type delegateType, CallSiteBinder binder, Expression arg0, Expression arg1, Expression arg2) { return null; }
        //public static DynamicExpression MakeDynamic(Type delegateType, CallSiteBinder binder, Expression arg0, Expression arg1, Expression arg2, Expression arg3) { return null; }
        //public static DynamicExpression Dynamic(CallSiteBinder binder, Type returnType, params Expression[] arguments) { return null; }
        //public static DynamicExpression Dynamic(CallSiteBinder binder, Type returnType, Expression arg0) { return null; }
        //public static DynamicExpression Dynamic(CallSiteBinder binder, Type returnType, Expression arg0, Expression arg1) { return null; }
        //public static DynamicExpression Dynamic(CallSiteBinder binder, Type returnType, Expression arg0, Expression arg1, Expression arg2) { return null; }
        //public static DynamicExpression Dynamic(CallSiteBinder binder, Type returnType, Expression arg0, Expression arg1, Expression arg2, Expression arg3) { return null; }
        //public static DynamicExpression Dynamic(CallSiteBinder binder, Type returnType, IEnumerable<Expression> arguments) { return null; }

        [Transpose.Template("{ ntype: 50, t: System.Object, dtype: 0, expression: {expression}, member: {member} }")]
        public static extern DynamicMemberExpression DynamicMember(Expression expression, string member);

        [Transpose.Template("{ ntype: 50, t: {type}, dtype: 0, expression: {expression}, member: {member} }")]
        public static extern DynamicMemberExpression DynamicMember(Type type, Expression expression, string member);

        [Transpose.Template("{ ntype: 50, t: System.Object, dtype: 1, expression: {expression}, arguments: Transpose.toList({arguments:array}) }")]
        public static extern DynamicInvocationExpression DynamicInvocation(Expression expression, params Expression[] arguments);

        [Transpose.Template("{ ntype: 50, t: System.Object, dtype: 1, expression: {expression}, arguments: Transpose.toList({arguments}) }")]
        public static extern DynamicInvocationExpression DynamicInvocation(Expression expression, IEnumerable<Expression> arguments);

        [Transpose.Template("{ ntype: 50, t: {type}, dtype: 1, expression: {expression}, arguments: Transpose.toList({arguments:array}) }")]
        public static extern DynamicInvocationExpression DynamicInvocation(Type type, Expression expression, params Expression[] arguments);

        [Transpose.Template("{ ntype: 50, t: {type}, dtype: 1, expression: {expression}, arguments: Transpose.toList({arguments}) }")]
        public static extern DynamicInvocationExpression DynamicInvocation(Type type, Expression expression, IEnumerable<Expression> arguments);

        [Transpose.Template("{ ntype: 50, t: System.Object, dtype: 2, expression: {expression}, argument: {argument} }")]
        public static extern DynamicIndexExpression DynamicIndex(Expression expression, Expression argument);

        [Transpose.Template("{ ntype: 50, t: {type}, dtype: 2, expression: {expression}, argument: {argument} }")]
        public static extern DynamicIndexExpression DynamicIndex(Type type, Expression expression, Expression argument);

        [Transpose.Template("{ addMethod: {addMethod}, arguments: Transpose.toList({arguments:array}) }")]
        public static extern ElementInit ElementInit(MethodInfo addMethod, params Expression[] arguments);

        [Transpose.Template("{ addMethod: {addMethod}, arguments: Transpose.toList({arguments}) }")]
        public static extern ElementInit ElementInit(MethodInfo addMethod, IEnumerable<Expression> arguments);

        [Transpose.Template("{ ntype: 53, t: System.Object, kind: 2, target: {target} }")]
        public static extern GotoExpression Break(LabelTarget target);

        [Transpose.Template("{ ntype: 53, t: {type}, kind: 2, target: {target} }")]
        public static extern GotoExpression Break(LabelTarget target, Type type);

        [Transpose.Template("{ ntype: 53, t: System.Object, kind: 2, target: {target}, value: {value} }")]
        public static extern GotoExpression Break(LabelTarget target, Expression value);

        [Transpose.Template("{ ntype: 53, t: {type}, kind: 2, target: {target}, value: {value} }")]
        public static extern GotoExpression Break(LabelTarget target, Expression value, Type type);

        [Transpose.Template("{ ntype: 53, t: System.Object, kind: 3, target: {target} }")]
        public static extern GotoExpression Continue(LabelTarget target);

        [Transpose.Template("{ ntype: 53, t: {type}, kind: 3, target: {target} }")]
        public static extern GotoExpression Continue(LabelTarget target, Type type);

        [Transpose.Template("{ ntype: 53, t: System.Object, kind: 1, target: {target} }")]
        public static extern GotoExpression Return(LabelTarget target);

        [Transpose.Template("{ ntype: 53, t: {type}, kind: 1, target: {target} }")]
        public static extern GotoExpression Return(LabelTarget target, Type type);

        [Transpose.Template("{ ntype: 53, t: System.Object, kind: 1, target: {target}, value: {value} }")]
        public static extern GotoExpression Return(LabelTarget target, Expression value);

        [Transpose.Template("{ ntype: 53, t: {type}, kind: 1, target: {target}, value: {value} }")]
        public static extern GotoExpression Return(LabelTarget target, Expression value, Type type);

        [Transpose.Template("{ ntype: 53, t: System.Object, kind: 0, target: {target} }")]
        public static extern GotoExpression Goto(LabelTarget target);

        [Transpose.Template("{ ntype: 53, t: {type}, kind: 0, target: {target} }")]
        public static extern GotoExpression Goto(LabelTarget target, Type type);

        [Transpose.Template("{ ntype: 53, t: System.Object, kind: 0, target: {target}, value: {value} }")]
        public static extern GotoExpression Goto(LabelTarget target, Expression value);

        [Transpose.Template("{ ntype: 53, t: {type}, kind: 0, target: {target}, value: {value} }")]
        public static extern GotoExpression Goto(LabelTarget target, Expression value, Type type);

        [Transpose.Template("{ ntype: 53, t: {type}, kind: {kind}, target: {target}, value: {value} }")]
        public static extern GotoExpression MakeGoto(GotoExpressionKind kind, LabelTarget target, Expression value, Type type);

        //public static IndexExpression MakeIndex(Expression instance, PropertyInfo indexer, IEnumerable<Expression> arguments) { return null; }
        [Transpose.Template("{ ntype: 55, t: {type}, obj: {array}, arguments: Transpose.toList({indexes:array}) }")]
        public static extern IndexExpression ArrayAccess(Type type, Expression array, params Expression[] indexes);

        [Transpose.Template("{ ntype: 55, t: {type}, obj: {array}, arguments: Transpose.toList({indexes}) }")]
        public static extern IndexExpression ArrayAccess(Type type, Expression array, IEnumerable<Expression> indexes);

        //public static IndexExpression Property(Expression instance, string propertyName, params Expression[] arguments) { return null; }
        [Transpose.Template("{ ntype: 55, t: {indexer}.rt, obj: {instance}, indexer: {indexer}, arguments: Transpose.toList({arguments:array}) }")]
        public static extern IndexExpression Property(Expression instance, PropertyInfo indexer, params Expression[] arguments);

        [Transpose.Template("{ ntype: 55, t: {indexer}.rt, obj: {instance}, indexer: {indexer}, arguments: Transpose.toList({arguments}) }")]
        public static extern IndexExpression Property(Expression instance, PropertyInfo indexer, IEnumerable<Expression> arguments);

        [Transpose.NonScriptable, EditorBrowsable(EditorBrowsableState.Never)]
        public static extern InvocationExpression Invoke(Expression expression, params Expression[] arguments);

        [Transpose.Template("{ ntype: 17, t: {type}, expression: {expression}, args: Transpose.toList({arguments:array}) }")]
        public static extern InvocationExpression Invoke(Type type, Expression expression, params Expression[] arguments);

        [Transpose.Template("{ ntype: 17, t: {type}, expression: {expression}, args: Transpose.toList({arguments}) }")]
        public static extern InvocationExpression Invoke(Type type, Expression expression, IEnumerable<Expression> arguments);

        [Transpose.Template("{ ntype: 56, t: {target}.t, target: {target} }")]
        public static extern LabelExpression Label(LabelTarget target);

        [Transpose.Template("{ ntype: 56, t: {target}.t, target: {target}, dv: {defaultValue} }")]
        public static extern LabelExpression Label(LabelTarget target, Expression defaultValue);

        [Transpose.Template("{ t: System.Object }")]
        public static extern LabelTarget Label();

        [Transpose.Template("{ t: System.Object, n: {name} }")]
        public static extern LabelTarget Label(string name);

        [Transpose.Template("{ t: {type} }")]
        public static extern LabelTarget Label(Type type);

        [Transpose.Template("{ t: {type}, n: {name} }")]
        public static extern LabelTarget Label(Type type, string name);

        [Transpose.Template("{ ntype: 18, t: Function, rt: {body}.t, body: {body}, p: Transpose.toList({parameters}) }")]
        public static extern Expression<TDelegate> Lambda<TDelegate>(Expression body, IEnumerable<ParameterExpression> parameters);

        [Transpose.Template("{ ntype: 18, t: Function, rt: {body}.t, body: {body}, p: Transpose.toList({parameters:array}) }")]
        public static extern Expression<TDelegate> Lambda<TDelegate>(Expression body, params ParameterExpression[] parameters);

        [Transpose.Template("{ ntype: 18, t: Function, rt: {body}.t, body: {body}, p: Transpose.toList({parameters}) }")]
        public static extern LambdaExpression Lambda(Expression body, IEnumerable<ParameterExpression> parameters);

        [Transpose.Template("{ ntype: 18, t: Function, rt: {body}.t, body: {body}, p: Transpose.toList({parameters:array}) }")]
        public static extern LambdaExpression Lambda(Expression body, params ParameterExpression[] parameters);

        //public static Type GetFuncType(params Type[] typeArgs) { return null; }
        //public static bool TryGetFuncType(Type[] typeArgs, out Type funcType) { funcType = null; return false; }
        //public static Type GetActionType(params Type[] typeArgs) { return null; }
        //public static bool TryGetActionType(Type[] typeArgs, out Type actionType) { actionType = null; return false; }
        //public static Type GetDelegateType(params Type[] typeArgs) { return null; }

        //public static ListInitExpression ListInit(NewExpression newExpression, params Expression[] initializers) { return null; }
        //public static ListInitExpression ListInit(NewExpression newExpression, IEnumerable<Expression> initializers) { return null; }
        [Transpose.Template("{ ntype: 22, t: {newExpression}.t, newExpression: {newExpression}, initializers: Transpose.toList({initializers:array}.map(function (i) { return { addMethod: {addMethod}, arguments: Transpose.toList([i]) }; })) }")]
        public static extern ListInitExpression ListInit(NewExpression newExpression, MethodInfo addMethod, params Expression[] initializers);

        [Transpose.Template("{ ntype: 22, t: {newExpression}.t, newExpression: {newExpression}, initializers: Transpose.toList(Transpose.toArray({initializers}).map(function (i) { return { addMethod: {addMethod}, arguments: Transpose.toList([i]) }; })) }")]
        public static extern ListInitExpression ListInit(NewExpression newExpression, MethodInfo addMethod, IEnumerable<Expression> initializers);

        [Transpose.Template("{ ntype: 22, t: {newExpression}.t, newExpression: {newExpression}, initializers: Transpose.toList({initializers:array}) }")]
        public static extern ListInitExpression ListInit(NewExpression newExpression, params ElementInit[] initializers);

        [Transpose.Template("{ ntype: 22, t: {newExpression}.t, newExpression: {newExpression}, initializers: Transpose.toList({initializers}) }")]
        public static extern ListInitExpression ListInit(NewExpression newExpression, IEnumerable<ElementInit> initializers);

        [Transpose.Template("{ ntype: 58, t: System.Object, body: {body} }")]
        public static extern LoopExpression Loop(Expression body);

        [Transpose.Template("{ ntype: 58, t: {break}.t, body: {body}, breakLabel: {break} }")]
        public static extern LoopExpression Loop(Expression body, LabelTarget @break);

        [Transpose.Template("{ ntype: 58, t: {break} ? {break}.t : System.Object, body: {body}, breakLabel: {break}, continueLabel: {continue} }")]
        public static extern LoopExpression Loop(Expression body, LabelTarget @break, LabelTarget @continue);

        [Transpose.Template("{ btype: 0, member: {member}, expression: {expression} }")]
        public static extern MemberAssignment Bind(MemberInfo member, Expression expression);

        [Transpose.NonScriptable, EditorBrowsable(EditorBrowsableState.Never)]
        public static extern MemberAssignment Bind(MethodInfo propertyAccessor, Expression expression);

        [Transpose.Template("{ ntype: 23, t: {field}.rt, expression: {expression}, member: {field} }")]
        public static extern MemberExpression Field(Expression expression, FieldInfo field);

        [Transpose.Template("{ ntype: 23, t: Transpose.Reflection.getMembers({expression}.t, 4, 284, {fieldName}).rt, expression: {expression}, member: Transpose.Reflection.getMembers({expression}.t, 4, 284, {fieldName}) }")]
        public static extern MemberExpression Field(Expression expression, string fieldName);

        [Transpose.Template("{ ntype: 23, t: {type}, expression: {expression}, member: Transpose.Reflection.getMembers({expression}.t, 4, 284, {fieldName}) }")]
        public static extern MemberExpression Field(Expression expression, Type type, string fieldName);

        [Transpose.Template("{ ntype: 23, t: Transpose.Reflection.getMembers({expression}.t, 16, 284, {propertyName}).rt, expression: {expression}, member: Transpose.Reflection.getMembers({expression}.t, 16, 284, {propertyName}) }")]
        public static extern MemberExpression Property(Expression expression, string propertyName);

        [Transpose.Template("{ ntype: 23, t: {type}, expression: {expression}, member: Transpose.Reflection.getMembers({expression}.t, 16, 284, {propertyName}) }")]
        public static extern MemberExpression Property(Expression expression, Type type, string propertyName);

        [Transpose.Template("{ ntype: 23, t: {property}.rt, expression: {expression}, member: {property} }")]
        public static extern MemberExpression Property(Expression expression, PropertyInfo property);

        [Transpose.NonScriptable, EditorBrowsable(EditorBrowsableState.Never)]
        public static extern MemberExpression Property(Expression expression, MethodInfo propertyAccessor);

        [Transpose.Template("{ ntype: 23, t: Transpose.Reflection.getMembers({expression}.t, 20, 284, {propertyOrFieldName}).rt, expression: {expression}, member: Transpose.Reflection.getMembers({expression}.t, 20, 284, {propertyOrFieldName}) }")]
        public static extern MemberExpression PropertyOrField(Expression expression, string propertyOrFieldName);

        [Transpose.Template("{ ntype: 23, t: {member}.rt, expression: {expression}, member: {member} }")]
        public static extern MemberExpression MakeMemberAccess(Expression expression, MemberInfo member);

        [Transpose.Template("{ ntype: 24, t: {newExpression}.t, newExpression: {newExpression}, bindings: Transpose.toList({bindings:array}) }")]
        public static extern MemberInitExpression MemberInit(NewExpression newExpression, params MemberBinding[] bindings);

        [Transpose.Template("{ ntype: 24, t: {newExpression}.t, newExpression: {newExpression}, bindings: Transpose.toList({bindings}) }")]
        public static extern MemberInitExpression MemberInit(NewExpression newExpression, IEnumerable<MemberBinding> bindings);

        [Transpose.Template("{ btype: 2, member: {member}, initializers: Transpose.toList({initializers:array}) }")]
        public static extern MemberListBinding ListBind(MemberInfo member, params ElementInit[] initializers);

        [Transpose.Template("{ btype: 2, member: {member}, initializers: Transpose.toList({initializers}) }")]
        public static extern MemberListBinding ListBind(MemberInfo member, IEnumerable<ElementInit> initializers);

        [Transpose.NonScriptable, EditorBrowsable(EditorBrowsableState.Never)]
        public static extern MemberListBinding ListBind(MethodInfo propertyAccessor, params ElementInit[] initializers);

        [Transpose.NonScriptable, EditorBrowsable(EditorBrowsableState.Never)]
        public static extern MemberListBinding ListBind(MethodInfo propertyAccessor, IEnumerable<ElementInit> initializers);

        [Transpose.Template("{ btype: 1, member: {member}, bindings: Transpose.toList({bindings:array}) }")]
        public static extern MemberMemberBinding MemberBind(MemberInfo member, params MemberBinding[] bindings);

        [Transpose.Template("{ btype: 1, member: {member}, bindings: Transpose.toList({bindings}) }")]
        public static extern MemberMemberBinding MemberBind(MemberInfo member, IEnumerable<MemberBinding> bindings);

        [Transpose.NonScriptable, EditorBrowsable(EditorBrowsableState.Never)]
        public static extern MemberMemberBinding MemberBind(MethodInfo propertyAccessor, params MemberBinding[] bindings);

        [Transpose.NonScriptable, EditorBrowsable(EditorBrowsableState.Never)]
        public static extern MemberMemberBinding MemberBind(MethodInfo propertyAccessor, IEnumerable<MemberBinding> bindings);

        [Transpose.Template("{ ntype: 6, t: {method}.rt, method: {method}, args: Transpose.toList({arguments:array}) }")]
        public static extern MethodCallExpression Call(MethodInfo method, params Expression[] arguments);

        [Transpose.Template("{ ntype: 6, t: {method}.rt, method: {method}, args: Transpose.toList({arguments}) }")]
        public static extern MethodCallExpression Call(MethodInfo method, IEnumerable<Expression> arguments);

        [Transpose.Template("{ ntype: 6, t: {method}.rt, obj: {instance}, method: {method}, args: Transpose.toList({arguments:array}) }")]
        public static extern MethodCallExpression Call(Expression instance, MethodInfo method, params Expression[] arguments);

        [Transpose.Template("{ ntype: 6, t: {method}.rt, obj: {instance}, method: {method}, args: Transpose.toList({arguments}) }")]
        public static extern MethodCallExpression Call(Expression instance, MethodInfo method, IEnumerable<Expression> arguments);

        [Transpose.Template("{ ntype: 32, t: System.Array.type({type}), expressions: Transpose.toList({initializers:array}) }")]
        public static extern NewArrayExpression NewArrayInit(Type type, params Expression[] initializers);

        [Transpose.Template("{ ntype: 32, t: System.Array.type({type}), expressions: Transpose.toList({initializers}) }")]
        public static extern NewArrayExpression NewArrayInit(Type type, IEnumerable<Expression> initializers);

        [Transpose.Template("{ ntype: 33, t: System.Array.type({type}, {bounds:array}.length), expressions: Transpose.toList({bounds:array}) }")]
        public static extern NewArrayExpression NewArrayBounds(Type type, params Expression[] bounds);

        [Transpose.Template("(function (l) { return { ntype: 33, t: System.Array.type({type}, l.Count), expressions: l };})(Transpose.toList({bounds}))")]
        public static extern NewArrayExpression NewArrayBounds(Type type, IEnumerable<Expression> bounds);

        [Transpose.Template("{ ntype: 31, t: {type}, constructor: Transpose.Reflection.getMembers({type}, 1, 284, null, []), arguments: Transpose.toList([]) }")]
        public static extern NewExpression New(Type type);

        [Transpose.Template("{ ntype: 31, t: {constructor}.td, constructor: {constructor}, arguments: Transpose.toList({arguments:array}) }")]
        public static extern NewExpression New(ConstructorInfo constructor, params Expression[] arguments);

        [Transpose.Template("{ ntype: 31, t: {constructor}.td, constructor: {constructor}, arguments: Transpose.toList({arguments}) }")]
        public static extern NewExpression New(ConstructorInfo constructor, IEnumerable<Expression> arguments);

        [Transpose.Template("{ ntype: 31, t: {constructor}.td, constructor: {constructor}, arguments: Transpose.toList({arguments}), m: Transpose.toList({members:array}) }")]
        public static extern NewExpression New(ConstructorInfo constructor, Expression[] arguments, params MemberInfo[] members);

        [Transpose.Template("{ ntype: 31, t: {constructor}.td, constructor: {constructor}, arguments: Transpose.toList({arguments}), m: Transpose.toList({members}) }")]
        public static extern NewExpression New(ConstructorInfo constructor, IEnumerable<Expression> arguments, IEnumerable<MemberInfo> members);

        [Transpose.Template("{ ntype: 31, t: {constructor}.td, constructor: {constructor}, arguments: Transpose.toList({arguments}), m: Transpose.toList({members:array}) }")]
        public static extern NewExpression New(ConstructorInfo constructor, IEnumerable<Expression> arguments, params MemberInfo[] members);

        [Transpose.Template("{ ntype: 38, t: {type} }")]
        public static extern ParameterExpression Parameter(Type type);

        [Transpose.Template("{ ntype: 38, t: {type}, n: {name} }")]
        public static extern ParameterExpression Parameter(Type type, string name);

        [Transpose.Template("{ ntype: 38, t: {type} }")]
        public static extern ParameterExpression Variable(Type type);

        [Transpose.Template("{ ntype: 38, t: {type}, n: {name} }")]
        public static extern ParameterExpression Variable(Type type, string name);

        //public static RuntimeVariablesExpression RuntimeVariables(params ParameterExpression[] variables) { return null; }
        //public static RuntimeVariablesExpression RuntimeVariables(IEnumerable<ParameterExpression> variables) { return null; }

        [Transpose.Template("{ body: {body}, testValues: Transpose.toList({testValues:array}) }")]
        public static extern SwitchCase SwitchCase(Expression body, params Expression[] testValues);

        [Transpose.Template("{ body: {body}, testValues: Transpose.toList({testValues}) }")]
        public static extern SwitchCase SwitchCase(Expression body, IEnumerable<Expression> testValues);

        [Transpose.Template("{ ntype: 59, t: {cases:array}[0].body.t, switchValue: {switchValue}, cases: Transpose.toList({cases:array}) }")]
        public static extern SwitchExpression Switch(Expression switchValue, params SwitchCase[] cases);

        [Transpose.Template("{ ntype: 59, t: {cases:array}[0].body.t, switchValue: {switchValue}, defaultBody: {defaultBody}, cases: Transpose.toList({cases:array}) }")]
        public static extern SwitchExpression Switch(Expression switchValue, Expression defaultBody, params SwitchCase[] cases);

        [Transpose.Template("{ ntype: 59, t: {cases:array}[0].body.t, switchValue: {switchValue}, defaultBody: {defaultBody}, comparison: {comparison}, cases: Transpose.toList({cases:array}) }")]
        public static extern SwitchExpression Switch(Expression switchValue, Expression defaultBody, MethodInfo comparison, params SwitchCase[] cases);

        [Transpose.Template("{ ntype: 59, t: {type}, switchValue: {switchValue}, defaultBody: {defaultBody}, comparison: {comparison}, cases: Transpose.toList({cases:array}) }")]
        public static extern SwitchExpression Switch(Type type, Expression switchValue, Expression defaultBody, MethodInfo comparison, params SwitchCase[] cases);

        [Transpose.Template("(function (a, b, c, d) { return { ntype: 59, t: d.getItem(0).body.t, switchValue: a, defaultBody: b, comparison: c, cases: d }; })({switchValue}, {defaultBody}, {comparison}, Transpose.toList({cases}))")]
        public static extern SwitchExpression Switch(Expression switchValue, Expression defaultBody, MethodInfo comparison, IEnumerable<SwitchCase> cases);

        [Transpose.Template("{ ntype: 59, t: {type}, switchValue: {switchValue}, defaultBody: {defaultBody}, comparison: {comparison}, cases: Transpose.toList({cases}) }")]
        public static extern SwitchExpression Switch(Type type, Expression switchValue, Expression defaultBody, MethodInfo comparison, IEnumerable<SwitchCase> cases);

        [Transpose.Template("{ ntype: 61, t: {body}.t, body: {body}, handlers: Transpose.toList([]), fault: {fault} }")]
        public static extern TryExpression TryFault(Expression body, Expression fault);

        [Transpose.Template("{ ntype: 61, t: {body}.t, body: {body}, handlers: Transpose.toList([]), finallyExpr: {finally} }")]
        public static extern TryExpression TryFinally(Expression body, Expression @finally);

        [Transpose.Template("{ ntype: 61, t: {body}.t, body: {body}, handlers: Transpose.toList({handlers:array}) }")]
        public static extern TryExpression TryCatch(Expression body, params CatchBlock[] handlers);

        [Transpose.Template("{ ntype: 61, t: {body}.t, body: {body}, finallyExpr: {finally}, handlers: Transpose.toList({handlers:array}) }")]
        public static extern TryExpression TryCatchFinally(Expression body, Expression @finally, params CatchBlock[] handlers);

        [Transpose.Template("{ ntype: 61, t: {type} || {body}.t, body: {body}, finallyExpr: {finally}, fault: {fault}, handlers: Transpose.toList({handlers} || []) }")]
        public static extern TryExpression MakeTry(Type type, Expression body, Expression @finally, Expression fault, IEnumerable<CatchBlock> handlers);

        [Transpose.Template("{ ntype: 45, t: System.Boolean, expression: {expression}, typeOperand: {type} }")]
        public static extern TypeBinaryExpression TypeIs(Expression expression, Type type);

        [Transpose.Template("{ ntype: 81, t: System.Boolean, expression: {expression}, typeOperand: {type} }")]
        public static extern TypeBinaryExpression TypeEqual(Expression expression, Type type);

        [Transpose.Template("{ ntype: {unaryType}, t: {type}, operand: {operand} }")]
        public static extern UnaryExpression MakeUnary(ExpressionType unaryType, Expression operand, Type type);

        [Transpose.Template("{ ntype: {unaryType}, t: {type} || {method}.rt, operand: {operand}, method: {method} }")]
        public static extern UnaryExpression MakeUnary(ExpressionType unaryType, Expression operand, Type type, MethodInfo method);

        [Transpose.NonScriptable, EditorBrowsable(EditorBrowsableState.Never)]
        public static extern UnaryExpression Negate(Expression expression);

        [Transpose.Template("{ ntype: 28, t: {type}, operand: {expression} }")]
        public static extern UnaryExpression Negate(Expression expression, Type type);

        [Transpose.Template("{ ntype: 28, t: {method}.rt, operand: {expression}, method: {method} }")]
        public static extern UnaryExpression Negate(Expression expression, MethodInfo method);

        [Transpose.NonScriptable, EditorBrowsable(EditorBrowsableState.Never)]
        public static extern UnaryExpression UnaryPlus(Expression expression);

        [Transpose.Template("{ ntype: 29, t: {type}, operand: {expression} }")]
        public static extern UnaryExpression UnaryPlus(Expression expression, Type type);

        [Transpose.Template("{ ntype: 29, t: {method}.rt, operand: {expression}, method: {method} }")]
        public static extern UnaryExpression UnaryPlus(Expression expression, MethodInfo method);

        [Transpose.NonScriptable, EditorBrowsable(EditorBrowsableState.Never)]
        public static extern UnaryExpression NegateChecked(Expression expression);

        [Transpose.Template("{ ntype: 30, t: {type}, operand: {expression} }")]
        public static extern UnaryExpression NegateChecked(Expression expression, Type type);

        [Transpose.Template("{ ntype: 30, t: {method}.rt, operand: {expression}, method: {method} }")]
        public static extern UnaryExpression NegateChecked(Expression expression, MethodInfo method);

        [Transpose.NonScriptable, EditorBrowsable(EditorBrowsableState.Never)]
        public static extern UnaryExpression Not(Expression expression);

        [Transpose.Template("{ ntype: 34, t: {type}, operand: {expression} }")]
        public static extern UnaryExpression Not(Expression expression, Type type);

        [Transpose.Template("{ ntype: 34, t: {method}.rt, operand: {expression}, method: {method} }")]
        public static extern UnaryExpression Not(Expression expression, MethodInfo method);

        [Transpose.Template("{ ntype: 84, t: {type}, operand: {expression} }")]
        public static extern UnaryExpression IsFalse(Expression expression, Type type);

        [Transpose.Template("{ ntype: 84, t: {method}.rt, operand: {expression}, method: {method} }")]
        public static extern UnaryExpression IsFalse(Expression expression, MethodInfo method);

        [Transpose.Template("{ ntype: 83, t: {type}, operand: {expression} }")]
        public static extern UnaryExpression IsTrue(Expression expression, Type type);

        [Transpose.Template("{ ntype: 83, t: {method}.rt, operand: {expression}, method: {method} }")]
        public static extern UnaryExpression IsTrue(Expression expression, MethodInfo method);

        [Transpose.NonScriptable, EditorBrowsable(EditorBrowsableState.Never)]
        public static extern UnaryExpression OnesComplement(Expression expression);

        [Transpose.Template("{ ntype: 82, t: {type}, operand: {expression} }")]
        public static extern UnaryExpression OnesComplement(Expression expression, Type type);

        [Transpose.Template("{ ntype: 82, t: {method}.rt, operand: {expression}, method: {method} }")]
        public static extern UnaryExpression OnesComplement(Expression expression, MethodInfo method);

        [Transpose.Template("{ ntype: 44, t: {type}, operand: {expression} }")]
        public static extern UnaryExpression TypeAs(Expression expression, Type type);

        [Transpose.Template("{ ntype: 62, t: {type}, operand: {expression} }")]
        public static extern UnaryExpression Unbox(Expression expression, Type type);

        [Transpose.Template("{ ntype: 10, t: {type}, operand: {expression} }")]
        public static extern UnaryExpression Convert(Expression expression, Type type);

        [Transpose.Template("{ ntype: 10, t: {type}, operand: {expression}, method: {method} }")]
        public static extern UnaryExpression Convert(Expression expression, Type type, MethodInfo method);

        [Transpose.Template("{ ntype: 11, t: {type}, operand: {expression} }")]
        public static extern UnaryExpression ConvertChecked(Expression expression, Type type);

        [Transpose.Template("{ ntype: 11, t: {type}, operand: {expression}, method: {method} }")]
        public static extern UnaryExpression ConvertChecked(Expression expression, Type type, MethodInfo method);

        [Transpose.Template("{ ntype: 4, t: System.Int32, operand: {array} }")]
        public static extern UnaryExpression ArrayLength(Expression array);

        [Transpose.Template("{ ntype: 40, t: System.Object, operand: {expression} }")]
        public static extern UnaryExpression Quote(Expression expression);

        [Transpose.Template("{ ntype: 60, t: System.Object }")]
        public static extern UnaryExpression Rethrow();

        [Transpose.Template("{ ntype: 60, t: {type} }")]
        public static extern UnaryExpression Rethrow(Type type);

        [Transpose.Template("{ ntype: 60, t: System.Object, operand: {value} }")]
        public static extern UnaryExpression Throw(Expression value);

        [Transpose.Template("{ ntype: 60, t: {type}, operand: {value} }")]
        public static extern UnaryExpression Throw(Expression value, Type type);

        [Transpose.Template("{ ntype: 54, t: {type}, operand: {expression} }")]
        public static extern UnaryExpression Increment(Expression expression, Type type);

        [Transpose.Template("{ ntype: 54, t: {method}.rt, operand: {expression}, method: {method} }")]
        public static extern UnaryExpression Increment(Expression expression, MethodInfo method);

        [Transpose.Template("{ ntype: 49, t: {type}, operand: {expression} }")]
        public static extern UnaryExpression Decrement(Expression expression, Type type);

        [Transpose.Template("{ ntype: 49, t: {method}.rt, operand: {expression}, method: {method} }")]
        public static extern UnaryExpression Decrement(Expression expression, MethodInfo method);

        [Transpose.Template("{ ntype: 77, t: {type}, operand: {expression} }")]
        public static extern UnaryExpression PreIncrementAssign(Expression expression, Type type);

        [Transpose.Template("{ ntype: 77, t: {method}.rt, operand: {expression}, method: {method} }")]
        public static extern UnaryExpression PreIncrementAssign(Expression expression, MethodInfo method);

        [Transpose.Template("{ ntype: 78, t: {type}, operand: {expression} }")]
        public static extern UnaryExpression PreDecrementAssign(Expression expression, Type type);

        [Transpose.Template("{ ntype: 78, t: {method}.rt, operand: {expression}, method: {method} }")]
        public static extern UnaryExpression PreDecrementAssign(Expression expression, MethodInfo method);

        [Transpose.Template("{ ntype: 79, t: {type}, operand: {expression} }")]
        public static extern UnaryExpression PostIncrementAssign(Expression expression, Type type);

        [Transpose.Template("{ ntype: 79, t: {method}.rt, operand: {expression}, method: {method} }")]
        public static extern UnaryExpression PostIncrementAssign(Expression expression, MethodInfo method);

        [Transpose.Template("{ ntype: 80, t: {type}, operand: {expression} }")]
        public static extern UnaryExpression PostDecrementAssign(Expression expression, Type type);

        [Transpose.Template("{ ntype: 80, t: {method}.rt, operand: {expression}, method: {method} }")]
        public static extern UnaryExpression PostDecrementAssign(Expression expression, MethodInfo method);
    }
}