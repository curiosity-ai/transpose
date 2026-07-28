using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Transpose.Translator;

/// <summary>
/// Reports two members of one type that would be emitted under the same JavaScript name.
///
/// A type's methods become the keys of a single JavaScript object literal, so two members that
/// resolve to one name emit as duplicate keys and JavaScript silently keeps only the last —
/// the earlier member becomes unreachable, and which one survives depends on declaration order.
/// C# overloads cannot collide (<see cref="TransposeNaming"/> numbers them), so in practice this
/// is <c>[Name("x")]</c> applied to two members, the shape that legacy h5 tolerated because it
/// mangled every implementation differently.
///
/// This is an error rather than a warning because the emitted code is wrong either way: the call
/// that binds to the shadowed member in C# runs the surviving one in JavaScript.
/// </summary>
internal static class DuplicateJsNameScanner
{
    /// <summary>
    /// Checks the types declared in <paramref name="trees"/>. A collision is a property of a type's
    /// declaration surface, so an incremental build that scans only the changed files still reaches
    /// every type whose members moved.
    /// </summary>
    public static IReadOnlyList<Diagnostic> Scan(TreeModel models, IEnumerable<SyntaxTree> trees)
    {
        var diagnostics = new List<Diagnostic>();
        var seen = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);

        foreach (var tree in trees)
        {
            var model = models.SemanticModelFor(tree);

            foreach (var decl in tree.GetRoot().DescendantNodes().OfType<TypeDeclarationSyntax>())
            {
                // A partial type is declared more than once; GetMembers() already returns the union,
                // so check the symbol once no matter how many declarations it has.
                if (model.GetDeclaredSymbol(decl) is INamedTypeSymbol type && seen.Add(type))
                {
                    Check(type, diagnostics);
                }
            }
        }

        return diagnostics;
    }

    private static void Check(INamedTypeSymbol type, List<Diagnostic> diagnostics)
    {
        // Instance methods and static methods land in different literals (`methods:` versus
        // `statics.methods:`), so a static and an instance member may share a name.
        var collisions = type.GetMembers()
            .OfType<IMethodSymbol>()
            .Where(Emitter.IsEmittableMethod)
            .GroupBy(m => (m.IsStatic, Name: TransposeNaming.MemberJsName(m)))
            .Where(g => g.Count() > 1);

        foreach (var collision in collisions)
        {
            var members = collision.OrderBy(m => m.Name).ToArray();
            var signatures = string.Join(", ", members.Select(m => m.ToDisplayString(SignatureFormat)));

            // Report against every declaration, so fixing whichever one the developer opens is enough
            // to see the error move rather than vanish.
            foreach (var member in members)
            {
                diagnostics.Add(Diagnostics.Create(
                    Diagnostics.DuplicateJsName,
                    member.Locations.FirstOrDefault(),
                    collision.Key.Name,
                    type.ToDisplayString(),
                    signatures));
            }
        }
    }

    private static readonly SymbolDisplayFormat SignatureFormat = new(
        memberOptions: SymbolDisplayMemberOptions.IncludeParameters,
        parameterOptions: SymbolDisplayParameterOptions.IncludeType | SymbolDisplayParameterOptions.IncludeParamsRefOut,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes);
}
