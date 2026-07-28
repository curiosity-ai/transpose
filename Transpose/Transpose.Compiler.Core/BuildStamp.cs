using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;

namespace Transpose.Compiler;

/// <summary>
/// The build stamp every assembly Transpose produces carries: which compiler built it, and the oldest
/// compiler that may consume it. It rides in the assembly as a private manifest resource named
/// <see cref="ResourceName"/>, next to the <c>Transpose.Resources.json</c> manifest and the embedded
/// JavaScript:
///
/// <code>
/// {
///   "compilerVersion": "26.7.1234",
///   "minimumCompilerVersion": "26.7.1234"
/// }
/// </code>
///
/// <b>Why.</b> A Transpose package DLL is not a normal .NET library: its real payload is the
/// JavaScript embedded inside it, and how a consumer's own emitted JavaScript binds to that payload
/// (member names, overload numbering, the runtime helpers it calls) is decided by the *compiler*
/// doing the consuming. Feed a package built by a newer <c>tps</c> to an older one and the failure
/// mode is a bundle that is subtly wrong at runtime rather than a build error. So a package declares
/// the compiler it needs, and <see cref="CheckReferences(IEnumerable{string})"/> fails the build with
/// an actionable message instead — see <see cref="MsBuildDiagnostic.CodeCompilerTooOld"/>.
///
/// The minimum is simply the version of the compiler that produced the assembly: a package is
/// consumable by the compiler that built it and by anything newer. Nothing declares it by hand.
///
/// <b>Compatibility both ways.</b> An assembly built before this stamp existed has no such resource,
/// which reads back as <see cref="TryRead"/> → null and is skipped, so an old package never fails a
/// new compiler. And the stamp is deliberately *not* listed in <c>Transpose.Resources.json</c>: it is
/// compiler metadata, not a web resource, so <c>OutputBuilder</c> never extracts it into a site.
/// </summary>
internal sealed record BuildStamp(string CompilerVersion, string MinimumCompilerVersion)
{
    /// <summary>The manifest-resource name the stamp is embedded under.</summary>
    public const string ResourceName = "Transpose.Build.json";

    /// <summary>The stamp the compiler that is running writes into what it builds. A dev-tree compiler
    /// stamps <see cref="Compiler.CompilerVersion.Unversioned"/>, which can never fail a check.</summary>
    public static BuildStamp ForCurrentCompiler()
        => new(Compiler.CompilerVersion.Text, Compiler.CompilerVersion.Text);

    /// <summary>The stamp's bytes, as they are embedded.</summary>
    public byte[] ToJsonBytes()
        => new UTF8Encoding(false).GetBytes(JsonSerializer.Serialize(
            new { compilerVersion = CompilerVersion, minimumCompilerVersion = MinimumCompilerVersion },
            new JsonSerializerOptions { WriteIndented = true }));

    /// <summary>
    /// Reads the stamp out of an assembly on disk, or null when it carries none (a plain .NET
    /// assembly, or a Transpose package built before the stamp existed) or cannot be read at all.
    ///
    /// Deliberately goes through <see cref="PEReader"/> rather than loading the assembly or opening it
    /// with Mono.Cecil: this runs over every reference of every project on every build, and a Transpose
    /// package DLL is tens of megabytes (all of it embedded JavaScript). Reading the manifest-resource
    /// table and one small blob out of it touches almost none of that.
    /// </summary>
    public static BuildStamp? TryRead(string assemblyPath)
    {
        try
        {
            using var file = File.OpenRead(assemblyPath);
            using var pe = new PEReader(file);
            if (!pe.HasMetadata) return null;

            var metadata = pe.GetMetadataReader();
            foreach (var handle in metadata.ManifestResources)
            {
                var resource = metadata.GetManifestResource(handle);
                // A non-nil Implementation means the resource lives in another file of the assembly;
                // Transpose only ever embeds, so such an entry is never ours.
                if (!resource.Implementation.IsNil) continue;
                if (!string.Equals(metadata.GetString(resource.Name), ResourceName, StringComparison.OrdinalIgnoreCase))
                    continue;

                var corHeader = pe.PEHeaders.CorHeader;
                if (corHeader is null || corHeader.ResourcesDirectory.Size == 0) return null;

                // Each embedded resource is stored at its own offset into the resources directory,
                // length-prefixed with a 4-byte little-endian size.
                var block = pe.GetSectionData(corHeader.ResourcesDirectory.RelativeVirtualAddress);
                var start = (int)resource.Offset;
                if (start < 0 || start + sizeof(int) > block.Length) return null;
                var reader = block.GetReader(start, block.Length - start);
                var size = reader.ReadInt32();
                if (size < 0 || size > reader.RemainingBytes) return null;

                return Parse(reader.ReadBytes(size));
            }
        }
        catch { /* an unreadable assembly is not this check's problem — the compile will report it */ }
        return null;
    }

    /// <summary>Parses the stamp's JSON, tolerating an unknown or differently-cased property set.
    /// Returns null when the bytes are not a stamp at all.</summary>
    private static BuildStamp? Parse(byte[] json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true });
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;

            string? Read(string name)
            {
                foreach (var property in doc.RootElement.EnumerateObject())
                    if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)
                        && property.Value.ValueKind == JsonValueKind.String)
                        return property.Value.GetString();
                return null;
            }

            var minimum = Read("minimumCompilerVersion");
            var built = Read("compilerVersion");
            if (minimum is null && built is null) return null;
            return new BuildStamp(built ?? "", minimum ?? "");
        }
        catch { return null; }
    }

    /// <summary>
    /// The gate: fails the build when any assembly in <paramref name="assemblyPaths"/> was built by a
    /// compiler newer than the one running. Returns the diagnostic to report, or null when everything
    /// is consumable (which includes every case where the check does not apply — see
    /// <see cref="Compiler.CompilerVersion.EnforceMinimum"/>).
    /// </summary>
    public static Diagnostic? CheckReferences(IEnumerable<string> assemblyPaths)
        => Compiler.CompilerVersion.EnforceMinimum
            ? CheckReferences(assemblyPaths, Compiler.CompilerVersion.Current!)
            : null;

    /// <summary>
    /// <see cref="CheckReferences(IEnumerable{string})"/> against an explicit compiler version, with no
    /// gate — the form the tests drive, since the test run is itself a Debug build and would otherwise
    /// find the check switched off.
    /// </summary>
    public static Diagnostic? CheckReferences(IEnumerable<string> assemblyPaths, Version current)
    {
        var running = Compiler.CompilerVersion.Normalize(current);

        List<(string name, Version required)>? tooNew = null;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in assemblyPaths)
        {
            if (string.IsNullOrEmpty(path) || !seen.Add(Path.GetFullPath(path))) continue;

            var required = Compiler.CompilerVersion.TryParse(TryRead(path)?.MinimumCompilerVersion);
            if (required is null) continue;                                        // not stamped: nothing to check
            if (Compiler.CompilerVersion.Normalize(required) <= running) continue;  // consumable

            (tooNew ??= new List<(string, Version)>()).Add((Path.GetFileNameWithoutExtension(path), required));
        }

        if (tooNew is null) return null;

        // Highest requirement first, then by name, so the message is deterministic and its headline
        // version is the one the user actually has to install.
        tooNew.Sort((a, b) =>
        {
            var byVersion = Compiler.CompilerVersion.Normalize(b.required)
                .CompareTo(Compiler.CompilerVersion.Normalize(a.required));
            return byVersion != 0 ? byVersion : string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase);
        });

        const int NamesShown = 4;
        var names = string.Join(", ", tooNew.Take(NamesShown).Select(x => $"'{x.name}' ({x.required})"));
        if (tooNew.Count > NamesShown) names += $", and {tooNew.Count - NamesShown} more";

        return Diagnostic.Create(OutdatedCompiler, Location.None,
            $"This project references assemblies built by a newer Transpose — {names}. The compiler in use is "
            + $"{current}, but {tooNew[0].required} or newer is required. Update it with: "
            + "dotnet tool install --global Transpose.Compiler");
    }

    /// <summary>
    /// The descriptor behind <see cref="MsBuildDiagnostic.CodeCompilerTooOld"/>. A Roslyn
    /// <see cref="Diagnostic"/> rather than a bare string so the CLI and
    /// <c>Transpose.Compiler.Library</c> report it through the one diagnostic path they already share.
    /// </summary>
    private static readonly DiagnosticDescriptor OutdatedCompiler = new(
        id: MsBuildDiagnostic.CodeCompilerTooOld,
        title: "The Transpose compiler is out of date",
        messageFormat: "{0}",
        category: "Transpose",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}
