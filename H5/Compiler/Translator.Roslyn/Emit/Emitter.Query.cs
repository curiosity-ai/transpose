using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace H5.Translator.Roslyn;

public sealed partial class Emitter
{
    /// <summary>
    /// Translates a LINQ query expression into the equivalent Enumerable method chain.
    /// Supports single-source from/where/orderby/select/group by. Multi-from, let, and
    /// join (which introduce transparent identifiers) are reported as unsupported.
    /// </summary>
    private void EmitQuery(QueryExpressionSyntax query)
    {
        Action emit = () => EmitExpression(query.FromClause.Expression);
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
                        _w.Write("System.Linq.Enumerable.Where(");
                        inner();
                        _w.Write($", function ({range}) {{ return ");
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
                result = emit; // identity projection
                break;
            case SelectClauseSyntax select:
                {
                    var inner = emit;
                    result = () =>
                    {
                        _w.Write("System.Linq.Enumerable.Select(");
                        inner();
                        _w.Write($", function ({range}) {{ return ");
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
                        _w.Write("System.Linq.Enumerable.GroupBy(");
                        inner();
                        _w.Write($", function ({range}) {{ return ");
                        EmitExpression(group.ByExpression);
                        _w.Write("; }");
                        if (!(group.GroupExpression is IdentifierNameSyntax gid && gid.Identifier.Text == range))
                        {
                            _w.Write($", function ({range}) {{ return ");
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
        string MethodFor(int i)
        {
            var descending = orderBy.Orderings[i].IsKind(SyntaxKind.DescendingOrdering);
            return i == 0
                ? (descending ? "OrderByDescending" : "OrderBy")
                : (descending ? "ThenByDescending" : "ThenBy");
        }

        // Opening calls: the outermost is the LAST ordering (ThenBy…), innermost is OrderBy.
        for (var i = orderBy.Orderings.Count - 1; i >= 0; i--)
        {
            _w.Write($"System.Linq.Enumerable.{MethodFor(i)}(");
        }

        inner();

        // Closing: innermost (OrderBy, index 0) is closed first.
        for (var i = 0; i < orderBy.Orderings.Count; i++)
        {
            _w.Write($", function ({range}) {{ return ");
            EmitExpression(orderBy.Orderings[i].Expression);
            _w.Write("; })");
        }
    }
}
