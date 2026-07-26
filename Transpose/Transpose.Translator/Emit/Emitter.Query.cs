using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Transpose.Translator;

public sealed partial class Emitter
{
    /// <summary>
    /// Translates a LINQ query expression into the tps.js Enumerable chain
    /// (<c>System.Linq.Enumerable.from(src).where(fn).orderBy(fn).select(fn)</c>) — the same
    /// runtime API the method-syntax <c>[Template]</c>s target. Supports single-source
    /// from/where/orderby/select/group by; multi-from, let, and join are reported unsupported.
    /// </summary>
    private void EmitQuery(QueryExpressionSyntax query)
    {
        Action emit = () =>
        {
            _w.Write("System.Linq.Enumerable.from(");
            EmitExpression(query.FromClause.Expression);
            _w.Write(")");
        };
        EmitQueryBody(emit, NameMangler.JsIdentifier(query.FromClause.Identifier.Text), query.Body);
    }

    private void EmitQueryBody(Action emitSource, string range, QueryBodySyntax body)
    {
        var emit = emitSource;

        foreach (var clause in body.Clauses)
        {
            var inner = emit;
            switch (clause)
            {
                case WhereClauseSyntax where:
                    emit = () =>
                    {
                        inner();
                        _w.Write(".where("); OpenQueryLambda(range);
                        EmitExpression(where.Condition);
                        _w.Write("; })");
                    };
                    break;

                case OrderByClauseSyntax orderBy:
                    emit = () => EmitOrderBy(inner, range, orderBy);
                    break;

                default:
                    Unsupported(clause, $"query clause {clause.Kind()} (use method syntax)");
                    break;
            }
        }

        // Produce the select/group result sequence.
        Action result;
        switch (body.SelectOrGroup)
        {
            case SelectClauseSyntax select when select.Expression is IdentifierNameSyntax sid && sid.Identifier.Text == range:
                result = emit; // identity projection — the sequence is already the result
                break;
            case SelectClauseSyntax select:
                {
                    var inner = emit;
                    result = () =>
                    {
                        inner();
                        _w.Write(".select("); OpenQueryLambda(range);
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
                        _w.Write(".groupBy("); OpenQueryLambda(range);
                        EmitExpression(group.ByExpression);
                        _w.Write("; }");
                        if (!(group.GroupExpression is IdentifierNameSyntax gid && gid.Identifier.Text == range))
                        {
                            _w.Write(", "); OpenQueryLambda(range);
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

        // `into` continuation: the result becomes the source of a new query body.
        if (body.Continuation is not null)
        {
            EmitQueryBody(result, NameMangler.JsIdentifier(body.Continuation.Identifier.Text), body.Continuation.Body);
        }
        else
        {
            result();
        }
    }

    private void EmitOrderBy(Action inner, string range, OrderByClauseSyntax orderBy)
    {
        inner();
        for (var i = 0; i < orderBy.Orderings.Count; i++)
        {
            var descending = orderBy.Orderings[i].IsKind(SyntaxKind.DescendingOrdering);
            var method = i == 0
                ? (descending ? "orderByDescending" : "orderBy")
                : (descending ? "thenByDescending" : "thenBy");
            _w.Write($".{method}("); OpenQueryLambda(range);
            EmitExpression(orderBy.Orderings[i].Expression);
            _w.Write("; })");
        }
    }

    /// <summary>
    /// Opens a query-clause lambda: <c>(range) =&gt; { return </c>. An ARROW, never a <c>function</c> —
    /// a query clause's expression is user code that may read <c>this</c> (e.g.
    /// <c>where x.Id == this._id</c> inside an instance method), and a plain function rebinds
    /// <c>this</c> to undefined under the bundle's "use strict", so such a clause threw
    /// "Cannot read properties of undefined". C# forbids <c>await</c> in a query clause, so the
    /// arrow never needs the async form.
    /// </summary>
    private void OpenQueryLambda(string range) => _w.Write($"({range}) => {{ return ");
}
