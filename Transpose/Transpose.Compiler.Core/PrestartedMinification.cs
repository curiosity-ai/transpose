using Transpose.Translator;

namespace Transpose.Compiler;

/// <summary>
/// A minification that was started before the package writer asked for it. The compiled bundle is final as
/// soon as the JavaScript is emitted, while the build goes on to check method bodies for another second;
/// minifying it is the longest single job in writing the package, so it starts then and runs beside that
/// check. The writer takes the result only when it is for the very text, name and setting it would have
/// minified itself (<see cref="IsFor"/>), so using it can never change what is written.
/// </summary>
internal sealed class PrestartedMinification
{
    private readonly string _source;
    private readonly string _fileName;
    private readonly bool _minifyLocals;

    public Task<string> Result { get; }

    private PrestartedMinification(string source, string fileName, bool minifyLocals)
    {
        _source = source;
        _fileName = fileName;
        _minifyLocals = minifyLocals;
        Result = Task.Run(() => PhaseTimings.Measure("  ├ minify bundle", () => JsMinifier.Minify(source, fileName, minifyLocals)));
        // A build that fails afterwards never reads the result; keep its fault from going unobserved.
        Result.ContinueWith(static t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
    }

    public static PrestartedMinification Start(string source, string fileName, bool minifyLocals)
        => new(source, fileName, minifyLocals);

    public bool IsFor(string source, string fileName, bool minifyLocals)
        => ReferenceEquals(source, _source) && string.Equals(fileName, _fileName, StringComparison.Ordinal) && minifyLocals == _minifyLocals;
}
