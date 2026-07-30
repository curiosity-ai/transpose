using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Transpose.Translator;

public sealed partial class Emitter
{
    /// <summary>
    /// The range variables visible at the point being emitted, mapped to the JavaScript expression that
    /// reads each one. A query clause becomes a lambda of ONE parameter, so a query that introduces more
    /// than one range variable (a second <c>from</c>, a <c>let</c>, a <c>join</c>) has to carry them in a
    /// frame object — C#'s "transparent identifier" — and every later clause reads <c>$q0.x</c> rather
    /// than <c>x</c>. <see cref="EmitIdentifier"/> consults this map, so a range variable resolves
    /// correctly however deeply it is nested inside a clause (including inside a lambda of its own).
    /// </summary>
    private Dictionary<string, string>? _queryRanges;

    /// <summary>Counter behind the <c>$q&lt;n&gt;</c> frame parameter names; global so the frames of a
    /// query nested inside another query's clause cannot collide.</summary>
    private int _queryFrameId;

    /// <summary>
    /// One query clause's view of its range variables: the JS lambda parameter the clause receives, and
    /// the range-variable names reachable through it.
    /// </summary>
    private sealed class QueryScope
    {
        /// <summary>The JS lambda parameter name a clause is emitted against.</summary>
        public required string Parameter { get; init; }

        /// <summary>The range-variable names in scope, in declaration order.</summary>
        public required List<string> Variables { get; init; }

        /// <summary>True when <see cref="Parameter"/> is a frame object holding the variables as members,
        /// false when it IS the single range variable.</summary>
        public required bool Transparent { get; init; }

        /// <summary>The JS expression that reads range variable <paramref name="name"/>.</summary>
        public string Access(string name) =>
            Transparent ? $"{Parameter}.{NameMangler.JsIdentifier(name)}" : Parameter;

        public Dictionary<string, string> Map()
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var v in Variables) map[v] = Access(v);
            return map;
        }
    }

    /// <summary>
    /// Translates a LINQ query expression into the tps.js Enumerable chain
    /// (<c>System.Linq.Enumerable.from(src).where(fn).orderBy(fn).select(fn)</c>) — the same runtime API
    /// the method-syntax <c>[Template]</c>s target. All clauses are supported: from (including a second
    /// and subsequent one, which lowers to <c>selectMany</c>), let, join / join-into, where, orderby,
    /// select, group-by and an <c>into</c> continuation.
    /// </summary>
    private void EmitQuery(QueryExpressionSyntax query)
    {
        var outerRanges = _queryRanges;
        try
        {
            var from = query.FromClause;
            Action emit = () =>
            {
                _w.Write("System.Linq.Enumerable.from(");
                EmitExpression(from.Expression);
                _w.Write(")");
                // `from int x in objects` is an explicitly typed range variable, which C# defines as a
                // Cast<T> of the source.
                if (from.Type is not null && _model.GetTypeInfo(from.Type).Type is { } castTo)
                    _w.Write($".select(function (x) {{ return Transpose.cast(x, {TypeRef(castTo)}); }})");
            };

            var scope = new QueryScope
            {
                Parameter = NameMangler.JsIdentifier(from.Identifier.Text),
                Variables = [from.Identifier.Text],
                Transparent = false,
            };
            EmitQueryBody(emit, scope, query.Body);
        }
        finally
        {
            _queryRanges = outerRanges;
        }
    }

    private void EmitQueryBody(Action emitSource, QueryScope scope, QueryBodySyntax body)
    {
        var emit = emitSource;

        foreach (var clause in body.Clauses)
        {
            var inner = emit;
            var current = scope;
            switch (clause)
            {
                case WhereClauseSyntax where:
                    emit = () =>
                    {
                        inner();
                        _w.Write(".where("); OpenQueryLambda(current);
                        EmitExpression(where.Condition);
                        _w.Write("; })");
                    };
                    break;

                case OrderByClauseSyntax orderBy:
                    emit = () => EmitOrderBy(inner, current, orderBy);
                    break;

                case LetClauseSyntax let:
                    {
                        var next = ExtendScope(current, let.Identifier.Text);
                        emit = () =>
                        {
                            inner();
                            // let y = e  →  .select(frame => ({ …carried…, y: e }))
                            _w.Write($".select(({current.Parameter}) => (");
                            EmitQueryFrame(current, let.Identifier.Text,
                                () => WithRanges(current, () => EmitExpression(let.Expression)));
                            _w.Write("))");
                        };
                        scope = next;
                        break;
                    }

                case FromClauseSyntax nestedFrom:
                    {
                        var next = ExtendScope(current, nestedFrom.Identifier.Text);
                        var nestedParam = NameMangler.JsIdentifier(nestedFrom.Identifier.Text);
                        emit = () =>
                        {
                            inner();
                            // from y in ys  →  .selectMany(frame => ys, (frame, y) => ({ …carried…, y }))
                            // The collection expression may read the range variables already in scope,
                            // which is exactly what distinguishes it from a join's inner source.
                            _w.Write($".selectMany(({current.Parameter}) => {{ return ");
                            var castTo = nestedFrom.Type is null ? null : _model.GetTypeInfo(nestedFrom.Type).Type;
                            if (castTo is not null) _w.Write("System.Linq.Enumerable.from(");
                            WithRanges(current, () => EmitExpression(nestedFrom.Expression));
                            if (castTo is not null)
                                _w.Write($").select(function (x) {{ return Transpose.cast(x, {TypeRef(castTo)}); }})");
                            _w.Write($"; }}, ({current.Parameter}, {nestedParam}) => (");
                            EmitQueryFrame(current, nestedFrom.Identifier.Text, () => _w.Write(nestedParam));
                            _w.Write("))");
                        };
                        scope = next;
                        break;
                    }

                case JoinClauseSyntax join:
                    {
                        // `join y in ys on outer equals inner` introduces y; with `into g` it introduces g
                        // instead (y is visible only in the inner key expression).
                        var introduced = join.Into?.Identifier.Text ?? join.Identifier.Text;
                        var innerParam = NameMangler.JsIdentifier(join.Identifier.Text);
                        var resultParam = NameMangler.JsIdentifier(introduced);
                        var next = ExtendScope(current, introduced);
                        emit = () =>
                        {
                            inner();
                            _w.Write(join.Into is null ? ".join(" : ".groupJoin(");
                            // The inner source is evaluated in the ENCLOSING scope — C# does not allow it
                            // to read a range variable — so it is emitted with the ranges cleared.
                            WithRanges(null, () => EmitExpression(join.InExpression));
                            _w.Write(", "); OpenQueryLambda(current);
                            EmitExpression(join.LeftExpression);
                            _w.Write("; }, ");
                            // The inner key selector's only variable is the join's own identifier.
                            _w.Write($"({innerParam}) => {{ return ");
                            WithRanges(
                                new QueryScope { Parameter = innerParam, Variables = [join.Identifier.Text], Transparent = false },
                                () => EmitExpression(join.RightExpression));
                            _w.Write("; }, ");
                            _w.Write($"({current.Parameter}, {resultParam}) => (");
                            EmitQueryFrame(current, introduced, () => _w.Write(resultParam));
                            _w.Write("))");
                        };
                        scope = next;
                        break;
                    }

                default:
                    Unsupported(clause, $"query clause {clause.Kind()} (use method syntax)");
                    break;
            }
        }

        // Produce the select/group result sequence.
        var final = scope;
        Action result;
        switch (body.SelectOrGroup)
        {
            case SelectClauseSyntax select when !final.Transparent
                                                && select.Expression is IdentifierNameSyntax sid
                                                && sid.Identifier.Text == final.Variables[0]:
                result = emit; // identity projection — the sequence is already the result
                break;
            case SelectClauseSyntax select:
                {
                    var inner = emit;
                    result = () =>
                    {
                        inner();
                        _w.Write(".select("); OpenQueryLambda(final);
                        EmitExpression(select.Expression);
                        _w.Write("; })");
                    };
                    break;
                }
            case GroupClauseSyntax group:
                {
                    var inner = emit;
                    result = () =>
                    {
                        inner();
                        _w.Write(".groupBy("); OpenQueryLambda(final);
                        EmitExpression(group.ByExpression);
                        _w.Write("; }");
                        var groupsTheRangeVariable = !final.Transparent
                            && group.GroupExpression is IdentifierNameSyntax gid
                            && gid.Identifier.Text == final.Variables[0];
                        if (!groupsTheRangeVariable)
                        {
                            _w.Write(", "); OpenQueryLambda(final);
                            EmitExpression(group.GroupExpression);
                            _w.Write("; }");
                        }
                        _w.Write(")");
                    };
                    break;
                }
            default:
                Unsupported(body.SelectOrGroup, "query body");
                return;
        }

        // `into` continuation: the result becomes the source of a new query body, with a single fresh
        // range variable and no frame.
        if (body.Continuation is not null)
        {
            EmitQueryBody(result, new QueryScope
            {
                Parameter = NameMangler.JsIdentifier(body.Continuation.Identifier.Text),
                Variables = [body.Continuation.Identifier.Text],
                Transparent = false,
            }, body.Continuation.Body);
        }
        else
        {
            result();
        }
    }

    /// <summary>The scope after a clause introduces <paramref name="introduced"/>: every variable now
    /// lives in a fresh frame object, since a clause lambda takes only one parameter.</summary>
    private QueryScope ExtendScope(QueryScope scope, string introduced) => new()
    {
        Parameter = "$q" + _queryFrameId++,
        Variables = [.. scope.Variables, introduced],
        Transparent = true,
    };

    /// <summary>
    /// Emits the transparent-identifier frame object <c>{ x: …, y: … }</c> that carries the range
    /// variables of <paramref name="scope"/> plus the newly introduced one. The carried members are read
    /// through <paramref name="scope"/> (the OUTER view), which is why this runs before the scope is
    /// swapped for the extended one.
    /// </summary>
    private void EmitQueryFrame(QueryScope scope, string introduced, Action emitIntroduced)
    {
        _w.Write("{ ");
        foreach (var v in scope.Variables)
        {
            _w.Write($"{NameMangler.JsIdentifier(v)}: {scope.Access(v)}, ");
        }
        _w.Write($"{NameMangler.JsIdentifier(introduced)}: ");
        emitIntroduced();
        _w.Write(" }");
    }

    private void EmitOrderBy(Action inner, QueryScope scope, OrderByClauseSyntax orderBy)
    {
        inner();
        for (var i = 0; i < orderBy.Orderings.Count; i++)
        {
            var descending = orderBy.Orderings[i].IsKind(SyntaxKind.DescendingOrdering);
            var method = i == 0
                ? (descending ? "orderByDescending" : "orderBy")
                : (descending ? "thenByDescending" : "thenBy");
            _w.Write($".{method}("); OpenQueryLambda(scope);
            EmitExpression(orderBy.Orderings[i].Expression);
            _w.Write("; })");
        }
    }

    /// <summary>
    /// Opens a query-clause lambda: <c>(range) =&gt; { return </c>, with the clause's range variables in
    /// scope for the expression that follows. An ARROW, never a <c>function</c> — a query clause's
    /// expression is user code that may read <c>this</c> (e.g. <c>where x.Id == this._id</c> inside an
    /// instance method), and a plain function rebinds <c>this</c> to undefined under the bundle's
    /// "use strict", so such a clause threw "Cannot read properties of undefined". C# forbids
    /// <c>await</c> in a query clause, so the arrow never needs the async form.
    /// </summary>
    private void OpenQueryLambda(QueryScope scope)
    {
        _w.Write($"({scope.Parameter}) => {{ return ");
        _queryRanges = scope.Map();
    }

    /// <summary>Runs <paramref name="emit"/> with a specific set of range variables in scope, restoring
    /// the previous set afterwards.</summary>
    private void WithRanges(QueryScope? scope, Action emit)
    {
        var saved = _queryRanges;
        _queryRanges = scope?.Map();
        try { emit(); }
        finally { _queryRanges = saved; }
    }
}
