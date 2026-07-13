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
        var range = NameMangler.JsIdentifier(query.FromClause.Identifier.Text);

        // Start from the source, then fold each clause into a nested Enumerable call.
        Action emit = () => EmitExpression(query.FromClause.Expression);

        foreach (var clause in query.Body.Clauses)
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

        switch (query.Body.SelectOrGroup)
        {
            case SelectClauseSyntax select:
                // Skip an identity `select x` projection.
                if (select.Expression is IdentifierNameSyntax id && id.Identifier.Text == query.FromClause.Identifier.Text)
                {
                    emit();
                }
                else
                {
                    var inner = emit;
                    _w.Write("System.Linq.Enumerable.Select(");
                    inner();
                    _w.Write($", function ({range}) {{ return ");
                    EmitExpression(select.Expression);
                    _w.Write("; })");
                }
                break;

            case GroupClauseSyntax group:
                var innerG = emit;
                _w.Write("System.Linq.Enumerable.GroupBy(");
                innerG();
                _w.Write($", function ({range}) {{ return ");
                EmitExpression(group.ByExpression);
                _w.Write("; }");
                if (!(group.GroupExpression is IdentifierNameSyntax gid && gid.Identifier.Text == query.FromClause.Identifier.Text))
                {
                    _w.Write($", function ({range}) {{ return ");
                    EmitExpression(group.GroupExpression);
                    _w.Write("; }");
                }
                _w.Write(")");
                break;

            default:
                Unsupported(query.Body.SelectOrGroup, "query body");
                break;
        }

        if (query.Body.Continuation is not null)
        {
            Unsupported(query.Body.Continuation, "query continuation (into)");
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
