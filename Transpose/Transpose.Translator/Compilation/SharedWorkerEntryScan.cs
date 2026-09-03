using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Transpose.Translator;

/// <summary>
/// One <c>[SharedWorkerEntry]</c> method: the worker's name, and the JavaScript call that starts it.
/// </summary>
/// <param name="Name">
/// The worker's name as written on the attribute. It is both the emitted script's base name and the
/// <c>name</c> the page constructs the <c>SharedWorker</c> with, which is part of a shared worker's
/// identity — so one name is one worker, and the page and the script cannot disagree about which.
/// </param>
/// <param name="Call">
/// The call the worker script makes once the runtime is up, e.g. <c>LiveWorker.Main()</c>. Resolved
/// here, where the naming layer is, so the output builder only has to write it down.
/// </param>
public sealed record SharedWorkerEntry(string Name, string Call);

/// <summary>
/// Finds the <c>[SharedWorkerEntry]</c> methods in a compilation.
///
/// <para>
/// A declaration scan rather than part of the emit walk, for two reasons: the bundle and the module
/// paths are two separate walks and both need the same answer, and nothing here depends on a method
/// <em>body</em> — so an incremental build over a body-only edit reaches the same result without
/// re-emitting anything.
/// </para>
/// </summary>
internal static class SharedWorkerEntryScan
{
    public static IReadOnlyList<SharedWorkerEntry> Collect(
        CSharpCompilation compilation, NameMangler names, List<Diagnostic> diagnostics)
    {
        var found = new List<SharedWorkerEntry>();
        var byName = new Dictionary<string, ISymbol>(System.StringComparer.OrdinalIgnoreCase);

        foreach (var type in AllTypes(compilation.Assembly.GlobalNamespace))
        {
            foreach (var method in type.GetMembers().OfType<IMethodSymbol>())
            {
                var attr = TransposeNaming.FindAttr(method, TransposeNaming.SharedWorkerEntryAttr);
                if (attr is null) continue;

                var location = method.Locations.FirstOrDefault(l => l.IsInSource);
                var display = method.ToDisplayString();

                // The entry runs once, from the worker script, with nothing to pass it and nothing to
                // hand a result back to.
                var fault = !method.IsStatic                            ? "it is not static"
                          : method.Parameters.Length > 0                ? "it takes parameters"
                          : !method.ReturnsVoid                         ? "it returns a value"
                          : method.IsGenericMethod                      ? "it is generic"
                          : null;

                if (fault is not null)
                {
                    diagnostics.Add(Diagnostics.Create(Diagnostics.BadSharedWorkerEntry, location, display, fault));
                    continue;
                }

                var name = attr.ConstructorArguments.Length > 0
                    ? attr.ConstructorArguments[0].Value as string
                    : null;

                if (string.IsNullOrWhiteSpace(name))
                {
                    diagnostics.Add(Diagnostics.Create(Diagnostics.BadSharedWorkerEntry, location, display,
                        "its name is empty"));
                    continue;
                }

                name = name!.Trim();

                // The name becomes a file beside the bundle, so it cannot reach out of the site.
                if (name.IndexOfAny(new[] { '/', '\\', ':' }) >= 0 || name == "." || name == "..")
                {
                    diagnostics.Add(Diagnostics.Create(Diagnostics.BadSharedWorkerEntry, location, display,
                        $"its name '{name}' is not a plain file name"));
                    continue;
                }

                // Two entries under one name would each emit the same script over the other, and the
                // page asking for that name would get whichever won.
                if (byName.TryGetValue(name, out var already))
                {
                    diagnostics.Add(Diagnostics.Create(Diagnostics.DuplicateSharedWorkerName, location,
                        name, $"{already.ToDisplayString()} and {display}"));
                    continue;
                }

                byName[name] = method;

                var typeRef = type.Arity > 0
                    ? names.TypeFullName(type) + "$" + type.Arity
                    : names.TypeFullName(type);

                found.Add(new SharedWorkerEntry(name, $"{typeRef}.{TransposeNaming.MemberJsName(method)}()"));
            }
        }

        // Emitted output has to be reproducible, and GetMembers/GetTypeMembers order is not contractual.
        return found.OrderBy(e => e.Name, System.StringComparer.Ordinal).ToList();
    }

    private static IEnumerable<INamedTypeSymbol> AllTypes(INamespaceSymbol ns)
    {
        foreach (var member in ns.GetMembers())
        {
            switch (member)
            {
                case INamespaceSymbol nested:
                    foreach (var t in AllTypes(nested)) yield return t;
                    break;
                case INamedTypeSymbol type:
                    foreach (var t in SelfAndNested(type)) yield return t;
                    break;
            }
        }
    }

    private static IEnumerable<INamedTypeSymbol> SelfAndNested(INamedTypeSymbol type)
    {
        yield return type;
        foreach (var nested in type.GetTypeMembers())
            foreach (var t in SelfAndNested(nested)) yield return t;
    }
}
