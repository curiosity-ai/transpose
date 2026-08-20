using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp;

namespace Transpose.HttpClient.Tests;

/// <summary>
/// Compiles and runs a C# program that uses the <c>Transpose.HttpClient</c> package as translated
/// JavaScript on Node, against a fake <c>XMLHttpRequest</c>.
///
/// Unlike the JSON packages, this one is not a thin binding over hand-written JavaScript: it is a real
/// C# implementation of the <c>System.Net.Http</c> surface that Transpose compiles like any other
/// library, and its transport is the browser's <c>XMLHttpRequest</c>. So a run needs three stages:
/// <list type="number">
///   <item>compile <c>Transpose.Core</c> (the DOM/ES bindings the package binds its transport
///     against) into a reference assembly — it is <c>[assembly: External]</c>, so it emits
///     essentially no JavaScript;</item>
///   <item>compile <c>Transpose.HttpClient</c> against that reference, keeping both its assembly (so
///     the snippet can bind to <c>System.Net.Http.*</c>) and its emitted JavaScript (which is the
///     implementation under test);</item>
///   <item>translate the snippet against both references and run
///     runtime + XHR stub + package JS + snippet on Node.</item>
/// </list>
///
/// Stage 1 costs about ten seconds — <c>Transpose.Core</c> is a very large binding library — so the
/// artifacts are cached on disk, keyed by a hash of everything that goes into them. A second
/// <c>dotnet test</c> run over unchanged sources starts in well under a second.
/// </summary>
public static class TranslatedHttpClientRunner
{
    private static readonly Lazy<Artifacts> _artifacts = new(Build, LazyThreadSafetyMode.ExecutionAndPublication);

    private sealed record Artifacts(
        string CoreDllPath,
        string PackageDllPath,
        string CoreJavascript,
        string PackageJavascript);

    /// <summary>Repo root, derived from this source file's compile-time path.</summary>
    private static string RepoRoot([CallerFilePath] string thisFile = "")
        // .../Packages/Transpose.HttpClient.Tests/Infrastructure/TranslatedHttpClientRunner.cs → repo root
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", "..", ".."));

    /// <summary>The package under test.</summary>
    public static string PackageDir => Path.Combine(RepoRoot(), "Packages", "Transpose.HttpClient");

    /// <summary>The DOM/ES bindings it binds its transport against.</summary>
    private static string CoreDir => Path.Combine(RepoRoot(), "BCL", "Transpose.Core");

    /// <summary>
    /// The C# side of <c>xhr-stub.js</c>: an external binding onto the stub's <c>$xhr</c> global, so a
    /// snippet declares its routes and asserts on the requests that were made in C# rather than in
    /// embedded JavaScript. Prepended to every translated snippet (and to none of the native ones —
    /// a snippet that uses it cannot be run against the real System.Net.Http).
    /// </summary>
    public const string HarnessSource = """
using Transpose;

/// <summary>Drives the fake XMLHttpRequest the suite runs against.</summary>
[External]
[Name("$xhr")]
public static class Xhr
{
    /// <summary>Answers <paramref name="method"/> requests for <paramref name="url"/> ("*" matches any).</summary>
    /// <param name="headers">Response headers as "Name: value" lines, or "" for none.</param>
    [Name("route")]
    public static extern void Route(string method, string url, int status, string body, string headers);

    /// <summary>As above, with no response headers (the stub reads a missing argument as none).</summary>
    [Name("route")]
    public static extern void Route(string method, string url, int status, string body);

    /// <summary>A route whose <c>response</c> is the PARSED body, i.e. what responseType "json" yields.</summary>
    [Name("routeJson")]
    public static extern void RouteJson(string method, string url, int status, string json);

    /// <summary>A route that fails at the transport level: readyState 4, status 0, no body.</summary>
    [Name("routeNetworkError")]
    public static extern void RouteNetworkError(string method, string url);

    /// <summary>Forgets every route and every recorded request.</summary>
    [Name("reset")]
    public static extern void Reset();

    /// <summary>How many requests reached the transport.</summary>
    [Name("requestCount")]
    public static extern int RequestCount();

    [Name("requestMethod")]
    public static extern string RequestMethod(int index);

    [Name("requestUrl")]
    public static extern string RequestUrl(int index);

    /// <summary>The request body, or "(none)" when the request was sent without one.</summary>
    [Name("requestBody")]
    public static extern string RequestBody(int index);

    /// <summary>Every request header, as sorted "Name: value" lines.</summary>
    [Name("requestHeaders")]
    public static extern string RequestHeaders(int index);

    /// <summary>The XHR responseType the request was sent with, or "(default)" if none was set.</summary>
    [Name("requestResponseType")]
    public static extern string RequestResponseType(int index);

    /// <summary>One request header's value, or "(absent)".</summary>
    [Name("requestHeader")]
    public static extern string RequestHeader(int index, string name);

    /// <summary>Whether abort() was called on the request.</summary>
    [Name("aborted")]
    public static extern bool Aborted(int index);
}
""";

    /// <summary>
    /// Translates <paramref name="csharpCode"/> (which may use <c>System.Net.Http.*</c> and the
    /// <c>Xhr</c> harness), runs it on Node against the fake XMLHttpRequest, and returns the
    /// normalized console output.
    /// </summary>
    public static async Task<string> RunAsync(string csharpCode)
    {
        var artifacts = _artifacts.Value;

        var result = new RoslynTranslator().Translate(
            new[] { ("Harness.cs", HarnessSource), ("App.cs", csharpCode) },
            CompilationBuilder.DefaultAssemblyName,
            extraReferencePaths: new[] { artifacts.CoreDllPath, artifacts.PackageDllPath },
            preprocessorSymbols: new[] { "DEBUG", "TRACE" });

        if (!result.Success)
        {
            var errors = string.Join("\n", result.Errors.Select(d => d.GetMessage()));
            throw new InvalidOperationException($"Translation failed:\n{errors}");
        }

        // The stub has to be installed before the package's JavaScript runs, because tps.js runs the
        // entry point as soon as the program's assembly is defined.
        var full = string.Join("\n",
            RoslynTranslator.LoadRuntime(),
            XhrStub,
            artifacts.CoreJavascript,
            artifacts.PackageJavascript,
            result.Javascript);

        if (Environment.GetEnvironmentVariable("TPS_DUMP_JS") is { Length: > 0 } dump)
            File.WriteAllText(dump, full);

        return TestOutput.Normalize(await NodeJsRunner.RunAsync(full));
    }

    private static string XhrStub => _stub.Value;

    private static readonly Lazy<string> _stub = new(() =>
    {
        var assembly = Assembly.GetExecutingAssembly();
        var name = assembly.GetManifestResourceNames()
                       .FirstOrDefault(n => n.EndsWith("xhr-stub.js", StringComparison.Ordinal))
                   ?? throw new InvalidOperationException("xhr-stub.js is not embedded in the test assembly.");
        using var stream = assembly.GetManifestResourceStream(name)!;
        return new StreamReader(stream).ReadToEnd();
    });

    // ---- building (and caching) the two reference assemblies ---------------------------------

    private static Artifacts Build()
    {
        var inputs = PackageSources(CoreDir).Concat(PackageSources(PackageDir)).ToList();
        var cache = Path.Combine(Path.GetTempPath(), "transpose-httpclient-tests", CacheKey(inputs));

        var coreDll = Path.Combine(cache, "Transpose.Core.dll");
        var coreJs = Path.Combine(cache, "Transpose.Core.js");
        var packageDll = Path.Combine(cache, "Transpose.HttpClient.dll");
        var packageJs = Path.Combine(cache, "Transpose.HttpClient.js");

        if (!File.Exists(coreDll) || !File.Exists(packageDll) || !File.Exists(coreJs) || !File.Exists(packageJs))
        {
            Directory.CreateDirectory(cache);

            // Transpose.Core declares its [assembly: External] through <AssemblyAttribute> items in its
            // csproj, which `tps` synthesizes into a source file. There is no MSBuild evaluation here,
            // so synthesize the same file: without it every extern member in the bindings is a
            // "native interop is not supported" error.
            var coreSources = PackageSources(CoreDir)
                .Append(("__AssemblyAttributes.cs",
                    "[assembly: Transpose.External]\n"
                    + "[assembly: Transpose.ExternalInterface]\n"
                    + "[assembly: Transpose.Virtual]\n"));

            var core = BuildAssembly(coreSources, "Transpose.Core", null, "CORE");
            File.WriteAllBytes(coreDll, core.Assembly);
            File.WriteAllText(coreJs, core.Javascript);

            var package = BuildAssembly(PackageSources(PackageDir), "Transpose.HttpClient", new[] { coreDll }, null);
            File.WriteAllBytes(packageDll, package.Assembly);
            File.WriteAllText(packageJs, package.Javascript);
        }

        return new Artifacts(coreDll, packageDll, File.ReadAllText(coreJs), File.ReadAllText(packageJs));
    }

    private static (byte[] Assembly, string Javascript) BuildAssembly(
        IEnumerable<(string path, string text)> sources,
        string assemblyName,
        IEnumerable<string>? references,
        string? extraSymbol)
    {
        var symbols = extraSymbol is null
            ? new[] { "TRANSPOSE", "TRACE" }
            : new[] { "TRANSPOSE", "TRACE", extraSymbol };

        var result = new RoslynTranslator().BuildAssembly(
            sources,
            assemblyName,
            extraReferencePaths: references,
            preprocessorSymbols: symbols,
            // Both projects pin C# 7.2 in their csproj, and they mean it: `dom.Literals.cs` declares a
            // type named `required`, which is a keyword from C# 11 on.
            languageVersion: LanguageVersion.CSharp7_2,
            emitAssembly: true);

        if (result.AssemblyBytes is null || result.Javascript is null)
        {
            var errors = string.Join("\n", result.Diagnostics
                .Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
                .Select(d => d.ToString()));
            throw new InvalidOperationException($"Failed to build {assemblyName}:\n{errors}");
        }

        return (result.AssemblyBytes, result.Javascript);
    }

    /// <summary>Every C# file of a project, excluding build output and the JavaScript resource trees.</summary>
    private static List<(string path, string text)> PackageSources(string dir) =>
        Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories)
            .Select(p => p.Replace('\\', '/'))
            .Where(p => !p.Contains("/bin/") && !p.Contains("/obj/") && !p.Contains("/Resources/"))
            .OrderBy(p => p, StringComparer.Ordinal)
            .Select(p => (path: p, text: File.ReadAllText(p)))
            .ToList();

    /// <summary>
    /// A content hash over everything the cached assemblies were built from: the two projects' sources,
    /// the translator (whose emitter decides the JavaScript) and the BCL reference it injects. Any of
    /// those changing has to invalidate the cache, or a suite run silently tests the previous build.
    /// </summary>
    private static string CacheKey(IEnumerable<(string path, string text)> sources)
    {
        using var sha = SHA256.Create();
        using var stream = new CryptoStream(Stream.Null, sha, CryptoStreamMode.Write);
        using (var writer = new StreamWriter(stream, new UTF8Encoding(false), 1 << 16, leaveOpen: true))
        {
            foreach (var (path, text) in sources)
            {
                writer.Write(path);
                writer.Write('\0');
                writer.Write(text);
                writer.Write('\0');
            }

            writer.Write(typeof(RoslynTranslator).Assembly.Location);
            writer.Write('\0');
            writer.Write(File.GetLastWriteTimeUtc(typeof(RoslynTranslator).Assembly.Location).Ticks);
            writer.Write('\0');
            // The injected BCL: its own build changes the emitted names the package binds to.
            writer.Write(TransposeAssemblies.TransposeDllPath);
            writer.Write('\0');
            writer.Write(File.GetLastWriteTimeUtc(TransposeAssemblies.TransposeDllPath).Ticks);
            writer.Write('\0');
            writer.Write(RoslynTranslator.LoadRuntime().Length);
        }

        stream.FlushFinalBlock();
        return Convert.ToHexString(sha.Hash!)[..16];
    }
}
