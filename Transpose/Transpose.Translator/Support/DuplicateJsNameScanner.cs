using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

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
///
/// Driven from <see cref="UnsupportedFeatureScanner"/>'s walk — it already visits every type
/// declaration with a semantic model in hand, so this adds no pass of its own.
/// </summary>
internal static class DuplicateJsNameScanner
{
    public static void Report(INamedTypeSymbol type, List<Diagnostic> diagnostics)
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
