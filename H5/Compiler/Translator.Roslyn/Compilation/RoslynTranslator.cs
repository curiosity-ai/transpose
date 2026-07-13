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
    /// <summary>Translate a single source file.</summary>
    public TranslationResult Translate(string source, string path = "App.cs", string assemblyName = CompilationBuilder.DefaultAssemblyName)
        => Translate(new[] { (path, source) }, assemblyName);

    /// <summary>Translate multiple source files into a single JS bundle.</summary>
    public TranslationResult Translate(IEnumerable<(string path, string text)> sources, string assemblyName = CompilationBuilder.DefaultAssemblyName)
    {
        var compilation = CompilationBuilder.Build(sources, assemblyName);

        var diagnostics = new List<Diagnostic>();
        diagnostics.AddRange(compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));

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
            var emitter = new Emitter(compilation);
            var body = emitter.Emit();
            var js = LoadRuntime() + "\n" + body;
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

    private static string? _cachedRuntime;

    public static string LoadRuntime()
    {
        if (_cachedRuntime is not null) return _cachedRuntime;

        var asm = typeof(RoslynTranslator).Assembly;
        var name = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("h5roslyn.runtime.js", StringComparison.Ordinal));

        if (name is null)
        {
            throw new InvalidOperationException("Embedded runtime resource h5roslyn.runtime.js not found.");
        }

        using var stream = asm.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        _cachedRuntime = reader.ReadToEnd();
        return _cachedRuntime;
    }
}
