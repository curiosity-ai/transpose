using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Transpose.Translator;

/// <summary>
/// Explains a "type not found" error for a .NET API the browser cannot provide.
///
/// <para>
/// <see cref="UnsupportedFeatureScanner"/> reports a use of <c>System.IO</c>, <c>System.Threading</c> or
/// <c>System.Net.Sockets</c> as <c>TransposeR0001</c> ("Threading primitives (System.Threading.Thread)
/// are not supported in the browser environment") — but only for a type the browser BCL declares, since
/// it works from bound symbols. Most of those namespaces is simply not in <c>Transpose.dll</c>
/// (<c>SemaphoreSlim</c>, <c>ThreadPool</c>, <c>Path</c>, <c>FileInfo</c>, all of
/// <c>System.Net.Sockets</c>), and for those Roslyn got there first with
/// <c>CS0234</c>/<c>CS0246</c>/<c>CS0103</c>, which reads like a missing package reference rather than
/// an API that cannot exist in a browser. This pass rewrites exactly those diagnostics into the scan's
/// message, so the same kind of mistake reads the same way whichever half of the namespace it hit.
/// </para>
///
/// <para>
/// A qualified name (<c>System.Threading.SemaphoreSlim</c>, <c>using System.Net.Sockets;</c>) names its
/// namespace, so any missing member of a denied namespace is rewritten. A bare name
/// (<c>new SemaphoreSlim(1)</c> under <c>using System.Threading;</c>) only is when it is a type .NET
/// really declares in a denied namespace <i>and</i> the file imports that namespace — anything else is
/// left as the ordinary compile error it is (a typo stays a typo).
/// </para>
/// </summary>
internal static class BrowserApiDiagnostics
{
    private static readonly HashSet<string> RewrittenIds = new() { "CS0234", "CS0246", "CS0103" };

    /// <summary>Simple names of .NET types in the denied namespaces that <c>Transpose.dll</c> does not
    /// declare, keyed to their namespace. A name the browser BCL does declare never reaches here: it
    /// binds, and the scan reports it.</summary>
    private static readonly Dictionary<string, string> KnownTypes = new(System.StringComparer.Ordinal)
    {
        // System.Threading
        ["SemaphoreSlim"] = "System.Threading", ["Semaphore"] = "System.Threading",
        ["Mutex"] = "System.Threading", ["ManualResetEvent"] = "System.Threading",
        ["ManualResetEventSlim"] = "System.Threading", ["AutoResetEvent"] = "System.Threading",
        ["EventWaitHandle"] = "System.Threading", ["WaitHandle"] = "System.Threading",
        ["CountdownEvent"] = "System.Threading", ["Barrier"] = "System.Threading",
        ["ReaderWriterLock"] = "System.Threading", ["ReaderWriterLockSlim"] = "System.Threading",
        ["SpinLock"] = "System.Threading", ["SpinWait"] = "System.Threading",
        ["ThreadPool"] = "System.Threading", ["ThreadLocal"] = "System.Threading",
        ["ThreadStart"] = "System.Threading", ["ParameterizedThreadStart"] = "System.Threading",
        ["ThreadState"] = "System.Threading", ["ThreadPriority"] = "System.Threading",
        ["AsyncLocal"] = "System.Threading", ["Volatile"] = "System.Threading",
        ["ExecutionContext"] = "System.Threading", ["LazyInitializer"] = "System.Threading",
        ["PeriodicTimer"] = "System.Threading",
        // System.IO
        ["Path"] = "System.IO", ["FileInfo"] = "System.IO", ["DirectoryInfo"] = "System.IO",
        ["Directory"] = "System.IO", ["FileSystemInfo"] = "System.IO", ["DriveInfo"] = "System.IO",
        ["FileSystemWatcher"] = "System.IO", ["FileMode"] = "System.IO", ["FileAccess"] = "System.IO",
        ["FileShare"] = "System.IO", ["FileOptions"] = "System.IO", ["FileAttributes"] = "System.IO",
        ["SearchOption"] = "System.IO", ["FileNotFoundException"] = "System.IO",
        ["DirectoryNotFoundException"] = "System.IO", ["PathTooLongException"] = "System.IO",
        ["UnmanagedMemoryStream"] = "System.IO",
        // System.IO.Compression
        ["GZipStream"] = "System.IO.Compression", ["DeflateStream"] = "System.IO.Compression",
        ["BrotliStream"] = "System.IO.Compression", ["ZipArchive"] = "System.IO.Compression",
        ["ZipFile"] = "System.IO.Compression", ["CompressionMode"] = "System.IO.Compression",
        ["CompressionLevel"] = "System.IO.Compression",
        // System.Net.Sockets
        ["Socket"] = "System.Net.Sockets", ["TcpClient"] = "System.Net.Sockets",
        ["TcpListener"] = "System.Net.Sockets", ["UdpClient"] = "System.Net.Sockets",
        ["NetworkStream"] = "System.Net.Sockets", ["SocketException"] = "System.Net.Sockets",
        ["AddressFamily"] = "System.Net.Sockets", ["SocketType"] = "System.Net.Sockets",
        ["ProtocolType"] = "System.Net.Sockets",
    };

    /// <summary>Returns <paramref name="diagnostics"/> with every rewritable "not found" error replaced
    /// by the browser-API diagnostic that explains it.</summary>
    public static List<Diagnostic> Rewrite(IEnumerable<Diagnostic> diagnostics)
    {
        var result = new List<Diagnostic>();
        foreach (var d in diagnostics)
            result.Add(TryRewrite(d) ?? d);
        return result;
    }

    private static Diagnostic? TryRewrite(Diagnostic d)
    {
        if (!RewrittenIds.Contains(d.Id) || d.Location.SourceTree is not { } tree) return null;

        var node = tree.GetRoot().FindNode(d.Location.SourceSpan, getInnermostNodeForTie: true);
        // The error spans the whole qualified name in expression position (`System.IO.Path` in
        // `System.IO.Path.GetFileName(…)`), and just the missing name in a type position.
        node = node switch
        {
            MemberAccessExpressionSyntax ma => ma.Name,
            QualifiedNameSyntax q => q.Right,
            _ => node,
        };
        if (node is not SimpleNameSyntax name) return null;
        var simple = name.Identifier.ValueText;

        string? fullName = null;
        if (QualifierOf(name) is { } qualifier)
        {
            // `System.Threading.SemaphoreSlim`, `using System.Net.Sockets;` — the namespace is written.
            var ns = qualifier.Replace("global::", string.Empty);
            if (UnsupportedFeatureScanner.DeniedApiMessage(ns + "." + simple) is not null) fullName = ns + "." + simple;
        }
        else if (KnownTypes.TryGetValue(simple, out var knownNs) && Imports(name, knownNs))
        {
            fullName = knownNs + "." + simple;
        }

        if (fullName is null || UnsupportedFeatureScanner.DeniedApiMessage(fullName) is not { } message) return null;
        return Diagnostics.Create(Diagnostics.Unsupported, d.Location, message);
    }

    /// <summary>The written qualifier of a name that is the right-hand side of a qualified name or a
    /// member access (<c>System.Threading</c> in <c>System.Threading.SemaphoreSlim</c>), or null.</summary>
    private static string? QualifierOf(SimpleNameSyntax name) => name.Parent switch
    {
        QualifiedNameSyntax q when q.Right == name => q.Left.ToString(),
        MemberAccessExpressionSyntax m when m.Name == name => m.Expression.ToString(),
        AliasQualifiedNameSyntax a when a.Name == name => a.Alias.ToString(),
        _ => null,
    };

    /// <summary>True if the file containing <paramref name="node"/> imports <paramref name="ns"/> with a
    /// plain <c>using</c> directive, at the top of the file or in an enclosing namespace.</summary>
    private static bool Imports(SyntaxNode node, string ns)
    {
        foreach (var ancestor in node.AncestorsAndSelf())
        {
            var usings = ancestor switch
            {
                CompilationUnitSyntax cu => cu.Usings,
                BaseNamespaceDeclarationSyntax nsDecl => nsDecl.Usings,
                _ => default,
            };
            if (usings.Any(u => u.Alias is null && u.StaticKeyword.RawKind == 0
                                && u.NamespaceOrType.ToString().Replace("global::", string.Empty) == ns))
                return true;
        }
        return false;
    }
}
