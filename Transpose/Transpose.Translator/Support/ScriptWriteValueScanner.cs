using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Transpose.Translator;

/// <summary>
/// Reports a <c>Script.Write&lt;T&gt;</c> whose value is used but whose template is several
/// JavaScript statements (TransposeR0006).
///
/// The template is opaque text, emitted where the call stands. Where the call is a statement — an
/// expression statement, the body of a void lambda or void expression-bodied member — that text may
/// be any number of statements. Where its value is used it has to be one expression: the emitter
/// writes <c>return {template};</c>, <c>let x = {template};</c> or brackets it as an operand, so
/// a template of two statements returns the first one's value and leaves the second unreachable
/// (<c>return var y = x + 1; y * 2;</c> does not even parse). Nothing in the emitted code says so.
///
/// A trailing <c>;</c> is not a second statement (<c>"hotkeys.getScope();"</c> is common), and
/// semicolons inside brackets, strings, comments and regular expressions are not top-level. A
/// template that needs statements and a value is written as an IIFE
/// (<c>"(function(){ var y = {0} + 1; return y * 2; })()"</c>) or from a block body.
///
/// Driven from <see cref="UnsupportedFeatureScanner"/>'s walk, so it adds no pass of its own.
/// </summary>
internal static class ScriptWriteValueScanner
{
    public static Diagnostic? Check(SemanticModel model, InvocationExpressionSyntax invocation)
    {
        // Syntactic pre-filter before any binding: this runs on every invocation in the compilation.
        var name = invocation.Expression switch
        {
            MemberAccessExpressionSyntax ma => ma.Name,
            SimpleNameSyntax sn             => sn,
            _                               => null,
        };
        if (name is not GenericNameSyntax { Identifier.Text: "Write" }) return null;
        if (invocation.ArgumentList.Arguments.Count < 1) return null;
        if (IsValueDiscarded(model, invocation)) return null;
        if (model.GetConstantValue(invocation.ArgumentList.Arguments[0].Expression).Value is not string template) return null;
        if (!HasCodeAfterTopLevelSemicolon(template)) return null;
        if (model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol { Name: "Write" } write
            || write.ContainingType?.ToDisplayString() != "Transpose.Script")
            return null;

        return Diagnostics.Create(Diagnostics.ScriptWriteStatementsAsValue, invocation.GetLocation(),
            "\"(function(){ var y = {0} + 1; return y * 2; })()\"");
    }

    /// <summary>True where the call's value is thrown away, so the emitter writes the template as statements.</summary>
    private static bool IsValueDiscarded(SemanticModel model, InvocationExpressionSyntax invocation)
    {
        switch (invocation.Parent)
        {
            case ExpressionStatementSyntax:
                return true;
            case ArrowExpressionClauseSyntax arrow:
                return model.GetDeclaredSymbol(arrow.Parent!) is IMethodSymbol { ReturnsVoid: true };
            case AnonymousFunctionExpressionSyntax lambda when lambda.Body == invocation:
                return model.GetTypeInfo(lambda).ConvertedType
                    is INamedTypeSymbol { TypeKind: TypeKind.Delegate, DelegateInvokeMethod.ReturnsVoid: true };
            default:
                return false;
        }
    }

    /// <summary>
    /// True when <paramref name="js"/> has a <c>;</c> outside every bracket, string, template
    /// literal, comment and regular expression, followed by more than whitespace, comments or further
    /// semicolons. Unbalanced brackets answer false: this only reports what it is sure of.
    /// </summary>
    internal static bool HasCodeAfterTopLevelSemicolon(string js)
    {
        var depth            = 0;
        var sawTopLevelSemi  = false;
        var regexAllowed     = true;

        for (var i = 0; i < js.Length; i++)
        {
            var c = js[i];

            if (char.IsWhiteSpace(c)) continue;

            if (c == '/' && i + 1 < js.Length && js[i + 1] == '/')
            {
                while (i < js.Length && js[i] != '\n') i++;
                continue;
            }

            if (c == '/' && i + 1 < js.Length && js[i + 1] == '*')
            {
                var end = js.IndexOf("*/", i + 2, System.StringComparison.Ordinal);
                if (end < 0) return false;
                i = end + 1;
                continue;
            }

            if (sawTopLevelSemi && depth == 0 && c != ';') return true;

            switch (c)
            {
                case '"' or '\'' or '`':
                    if (!SkipQuoted(js, ref i, c)) return false;
                    regexAllowed = false;
                    break;
                case '/' when regexAllowed:
                    if (!SkipRegex(js, ref i)) return false;
                    regexAllowed = false;
                    break;
                case '(' or '[' or '{':
                    depth++;
                    regexAllowed = true;
                    break;
                case ')' or ']' or '}':
                    if (--depth < 0) return false;
                    regexAllowed = c == '}';
                    break;
                case ';':
                    if (depth == 0) sawTopLevelSemi = true;
                    regexAllowed = true;
                    break;
                default:
                    if (char.IsLetterOrDigit(c) || c is '_' or '$')
                    {
                        var start = i;
                        while (i + 1 < js.Length && (char.IsLetterOrDigit(js[i + 1]) || js[i + 1] is '_' or '$')) i++;
                        var word = js.Substring(start, i - start + 1);
                        regexAllowed = word is "return" or "typeof" or "case" or "do" or "else" or "in" or "of"
                                           or "new" or "delete" or "void" or "throw" or "instanceof" or "yield" or "await";
                    }
                    else
                    {
                        regexAllowed = true;
                    }
                    break;
            }
        }

        return false;
    }

    private static bool SkipQuoted(string js, ref int i, char quote)
    {
        for (i++; i < js.Length; i++)
        {
            if (js[i] == '\\') { i++; continue; }
            if (js[i] == quote) return true;
        }
        return false;
    }

    private static bool SkipRegex(string js, ref int i)
    {
        var inClass = false;
        for (i++; i < js.Length; i++)
        {
            var c = js[i];
            if (c == '\\') { i++; continue; }
            if (c == '\n') return false;
            if (c == '[') inClass = true;
            else if (c == ']') inClass = false;
            else if (c == '/' && !inClass)
            {
                while (i + 1 < js.Length && char.IsLetter(js[i + 1])) i++;
                return true;
            }
        }
        return false;
    }
}
