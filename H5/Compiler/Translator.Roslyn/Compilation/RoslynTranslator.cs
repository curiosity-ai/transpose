using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace H5.Translator.Roslyn;

/// <summary>
/// Public entry point for the Roslyn-only C# → JavaScript translator.
///
/// Pipeline: parse + compile (Roslyn) → surface errors → scan for browser-incompatible
/// features → walk syntax tree guided by the semantic model → emit JavaScript.
/// </summary>
public sealed class RoslynTranslator
{
    /// <summary>
    /// Roslyn errors that are artifacts of compiling against the H5 BCL (which targets a
    /// runtime feature-set narrower than the language) but are harmless when the output is
    /// untyped JavaScript. CS8830: covariant return types in overrides — no runtime type
    /// check exists in JS, so the override simply works.
    /// </summary>
    private static readonly HashSet<string> BenignForJs = new()
    {
        "CS8830", // covariant return types in overrides — no runtime type check in JS
        "CS5001", // no static Main — library-style snippets simply emit no entry point
        // async ValueTask: H5.dll's ValueTask lacks the async method-builder attribute, so
        // Roslyn (against the H5 BCL) rejects it as a task-like return type and then reports
        // a missing return. We emit ValueTask exactly like Task (a Promise → h5.js Task), so
        // both are harmless. (The native comparison compiles against the real BCL, unaffected.)
        "CS1983", // return type of an async method must be void/Task/task-like…
        "CS0161", // not all code paths return a value (fallout of the above; JS returns undefined)
        "CS4032", // 'await' in a method with a non-task-like return (same ValueTask fallout)
    };

    /// <summary>Translate a single source file.</summary>
    public TranslationResult Translate(string source, string path = "App.cs", string assemblyName = CompilationBuilder.DefaultAssemblyName)
        => Translate(new[] { (path, source) }, assemblyName);

    /// <summary>Translate multiple source files into a single JS bundle.</summary>
    public TranslationResult Translate(IEnumerable<(string path, string text)> sources, string assemblyName = CompilationBuilder.DefaultAssemblyName)
    {
        var compilation = CompilationBuilder.Build(sources, assemblyName);

        var diagnostics = new List<Diagnostic>();
        diagnostics.AddRange(compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error && !BenignForJs.Contains(d.Id)));

        if (diagnostics.Count > 0)
        {
            return new TranslationResult(null, diagnostics);
        }

        // Report browser-incompatible features as compilation errors.
        var unsupported = UnsupportedFeatureScanner.Scan(compilation);
        diagnostics.AddRange(unsupported);
        if (unsupported.Any(d => d.Severity == DiagnosticSeverity.Error))
        {
            return new TranslationResult(null, diagnostics);
        }

        try
        {
            var emitter = new Emitter(compilation, assemblyName);
            var js = emitter.Emit();
            return new TranslationResult(js, diagnostics);
        }
        catch (TranslationException ex)
        {
            diagnostics.Add(Diagnostics.Create(Diagnostics.Unsupported, ex.Location, ex.Message));
            return new TranslationResult(null, diagnostics);
        }
    }

    /// <summary>
    /// Convenience: translate and throw with a readable message if anything failed.
    /// Mirrors the behavior tests expect (compilation failures throw).
    /// </summary>
    public string TranslateOrThrow(string source, string path = "App.cs")
    {
        var result = Translate(source, path);
        if (!result.Success)
        {
            var messages = string.Join("\n", result.Errors.Select(d => d.GetMessage()));
            throw new TranslationException(messages.Length > 0 ? messages : "Translation failed.");
        }
        return result.Javascript!;
    }

    private static string? _shim;

    /// <summary>
    /// The full runtime prelude: the real h5.js followed by the thin H5R shim that
    /// adapts the emitter's language-level helpers onto h5.js primitives.
    /// </summary>
    public static string LoadRuntime()
    {
        _shim ??= ReadShim();
        return H5Assemblies.RuntimeJs + "\n" + _shim;
    }

    private static string ReadShim()
    {
        var asm = typeof(RoslynTranslator).Assembly;
        var name = asm.GetManifestResourceNames().First(n => n.EndsWith("h5roslyn.shim.js", StringComparison.Ordinal));
        using var stream = asm.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
