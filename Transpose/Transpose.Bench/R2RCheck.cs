using System.Reflection.PortableExecutable;
using System.Text;

namespace Transpose.Bench;

/// <summary>
/// Reports whether a <c>tps</c> installation is ReadyToRun-compiled.
///
/// This matters twice over. It belongs in every benchmark report, because a JIT-only and an R2R build
/// of the same commit differ by ~1 s per invocation (the fixed cost of JIT-compiling Roslyn) — a
/// timing without that context can be off by 15-20% for reasons that have nothing to do with the
/// compiler's code. And it is the check a release pipeline needs: R2R is easy to lose silently (a
/// missing <c>-p:PublishReadyToRun=true</c>, a RID-agnostic publish) and the only symptom is that
/// everyone's builds are quietly slower.
///
/// Detection is the PE ManagedNativeHeader directory: a ReadyToRun image carries one, plain IL does
/// not. Native files (the apphost) have no metadata at all and are reported separately.
/// </summary>
internal static class R2RCheck
{
    /// <summary>The assemblies whose compilation state actually drives startup cost: the compiler
    /// itself, the translator, and Roslyn (by far the largest JIT bill).</summary>
    private static readonly string[] Significant =
    {
        "tps.dll", "Transpose.Translator.dll",
        "Microsoft.CodeAnalysis.dll", "Microsoft.CodeAnalysis.CSharp.dll",
    };

    internal sealed record Result(
        string Directory,
        int ReadyToRunCount,
        int IlOnlyCount,
        IReadOnlyList<string> SignificantIlOnly,
        IReadOnlyList<string> SignificantMissing)
    {
        /// <summary>True when every assembly that matters for startup is ReadyToRun. False also when
        /// one of them is simply absent — a `tps` resolved from PATH may be a shim whose payload lives
        /// elsewhere, and "cannot tell" must never read as "verified".</summary>
        public bool IsReadyToRun => ReadyToRunCount > 0 && SignificantIlOnly.Count == 0 && SignificantMissing.Count == 0;

        public string Describe()
        {
            var sb = new StringBuilder();
            if (IsReadyToRun)
            {
                sb.Append($"ReadyToRun: yes — {ReadyToRunCount} assembly(ies) precompiled");
                if (IlOnlyCount > 0) sb.Append($", {IlOnlyCount} left as IL");
            }
            else if (SignificantMissing.Count > 0 && ReadyToRunCount == 0 && IlOnlyCount == 0)
            {
                sb.Append("ReadyToRun: unknown — no managed assemblies found next to the tps executable "
                        + "(a PATH shim? point --tps at the published payload to check)");
            }
            else
            {
                sb.Append($"ReadyToRun: NO — {IlOnlyCount} assembly(ies) are IL-only");
                if (SignificantIlOnly.Count > 0) sb.Append($" (including {string.Join(", ", SignificantIlOnly)})");
                if (SignificantMissing.Count > 0) sb.Append($"; not found: {string.Join(", ", SignificantMissing)}");
                sb.Append(". Expect ~1 s of extra JIT per invocation.");
            }
            return sb.ToString();
        }
    }

    /// <summary>Inspects the directory holding <paramref name="tpsPath"/>, following a dotnet-tool
    /// shim to the real payload first.</summary>
    public static Result Inspect(string tpsPath)
    {
        var dir = Path.GetDirectoryName(ResolvePayload(tpsPath)) ?? ".";
        var r2r = 0;
        var il = 0;
        var significantIl = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in SafeEnumerate(dir))
        {
            var name = Path.GetFileName(file);
            bool? isR2R = Classify(file);
            if (isR2R is null) continue;              // native or unreadable — not a managed assembly
            seen.Add(name);
            if (isR2R.Value) r2r++;
            else
            {
                il++;
                if (Significant.Contains(name, StringComparer.OrdinalIgnoreCase)) significantIl.Add(name);
            }
        }

        var missing = Significant.Where(s => !seen.Contains(s)).ToArray();
        return new Result(dir, r2r, il, significantIl, missing);
    }

    /// <summary>
    /// Verifies every <c>tps</c> payload under <paramref name="root"/> — one per RID for a multi-RID
    /// tool pack, each identified by the <c>tps.dll</c> sitting in it. Returns the per-payload results;
    /// an empty list means nothing was found, which a caller must treat as a failure rather than a
    /// pass.
    ///
    /// A RID-specific build leaves two copies of each payload in the build tree: the intermediate
    /// build output (plain IL) and the <c>publish</c> subfolder next to it (ReadyToRun, and the one
    /// that gets packed). Only the latter is meaningful, so a directory that has a <c>publish</c> child
    /// with its own payload is skipped — otherwise every RID would report a spurious failure.
    /// </summary>
    public static IReadOnlyList<Result> InspectAll(string root)
    {
        var results = new List<Result>();
        try
        {
            foreach (var dll in Directory.EnumerateFiles(root, "tps.dll", SearchOption.AllDirectories)
                                         .OrderBy(p => p, StringComparer.Ordinal))
            {
                var dir = Path.GetDirectoryName(dll)!;
                if (File.Exists(Path.Combine(dir, "publish", "tps.dll"))) continue;  // superseded
                results.Add(Inspect(dll));
            }
        }
        catch { /* unreadable root — reported as "nothing found" by the empty list */ }
        return results;
    }

    /// <summary>What a single .nupkg turned out to be.</summary>
    internal enum PackageKind { ToolImplementation, Selector, NotATool }

    /// <summary>The verdict for one .nupkg: its kind, and (for an implementation package) whether its
    /// payload is ReadyToRun.</summary>
    internal sealed record PackageResult(string PackagePath, PackageKind Kind, Result? Payload)
    {
        /// <summary>A selector package legitimately carries no implementation, so it passes. Anything
        /// that is not a tool package at all is not this check's business and also passes.</summary>
        public bool IsAcceptable => Kind != PackageKind.ToolImplementation || (Payload?.IsReadyToRun ?? false);

        public string Describe() => Kind switch
        {
            PackageKind.Selector => "RID selector package (no implementation, as expected)",
            PackageKind.NotATool => "not a dotnet-tool package — skipped",
            _ => Payload!.Describe(),
        };
    }

    /// <summary>
    /// Verifies the payload inside every <c>.nupkg</c> in <paramref name="directory"/> — i.e. exactly
    /// what a pipeline is about to push, rather than whatever happens to be in the build tree.
    ///
    /// A multi-RID tool pack produces one implementation package per RID plus a tiny outer selector
    /// package that maps RIDs to those; the selector has no payload and must not be treated as a
    /// failure.
    /// </summary>
    public static IReadOnlyList<PackageResult> InspectPackages(string directory)
    {
        var results = new List<PackageResult>();
        string[] packages;
        try { packages = Directory.GetFiles(directory, "*.nupkg", SearchOption.TopDirectoryOnly); }
        catch { return results; }

        foreach (var pkg in packages.OrderBy(p => p, StringComparer.Ordinal))
        {
            var temp = Path.Combine(Path.GetTempPath(), "tps-r2r-verify", Path.GetFileNameWithoutExtension(pkg));
            try
            {
                if (Directory.Exists(temp)) Directory.Delete(temp, recursive: true);
                System.IO.Compression.ZipFile.ExtractToDirectory(pkg, temp);

                var isTool = Directory.EnumerateFiles(temp, "DotnetToolSettings.xml", SearchOption.AllDirectories).Any();
                var payloadDll = Directory.EnumerateFiles(temp, "tps.dll", SearchOption.AllDirectories).FirstOrDefault();

                var kind = payloadDll is not null ? PackageKind.ToolImplementation
                         : isTool ? PackageKind.Selector
                         : PackageKind.NotATool;
                results.Add(new PackageResult(pkg, kind, payloadDll is null ? null : Inspect(payloadDll)));
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"  warning: could not inspect {Path.GetFileName(pkg)}: {ex.Message}");
                results.Add(new PackageResult(pkg, PackageKind.NotATool, null));
            }
            finally
            {
                try { if (Directory.Exists(temp)) Directory.Delete(temp, recursive: true); } catch { }
            }
        }
        return results;
    }

    /// <summary>
    /// The real executable behind <paramref name="tpsPath"/>. A globally-installed dotnet tool puts a
    /// shim in <c>~/.dotnet/tools</c> — on Unix a symlink into
    /// <c>~/.dotnet/tools/.store/&lt;id&gt;/&lt;version&gt;/…/tools/&lt;tfm&gt;/&lt;rid&gt;/</c> — and the
    /// assemblies live next to the target, not next to the shim. Following it is what lets this check
    /// answer the question for a tool as actually installed, rather than only for a publish folder.
    /// Falls back to the given path when it is not a link (a Windows shim is a real executable, and a
    /// publish folder needs no resolution).
    /// </summary>
    private static string ResolvePayload(string tpsPath)
    {
        var full = Path.GetFullPath(tpsPath);
        try
        {
            // ResolveLinkTarget(returnFinalTarget: true) walks a whole chain of links.
            if (File.ResolveLinkTarget(full, returnFinalTarget: true) is { } target)
                return target.FullName;
        }
        catch { /* not a link, or unreadable — use the path as given */ }
        return full;
    }

    private static IEnumerable<string> SafeEnumerate(string dir)
    {
        try { return Directory.EnumerateFiles(dir, "*.dll", SearchOption.TopDirectoryOnly); }
        catch { return Array.Empty<string>(); }
    }

    /// <summary>True = ReadyToRun, false = IL only, null = not a managed assembly.</summary>
    private static bool? Classify(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            using var pe = new PEReader(fs);
            if (!pe.HasMetadata) return null;
            return pe.PEHeaders.CorHeader?.ManagedNativeHeaderDirectory.Size > 0;
        }
        catch { return null; }
    }
}
