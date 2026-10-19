using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace Transpose.Translator.Tests;

/// <summary>
/// Executes emitted JavaScript on Node.js (V8 — the same engine as Chromium) and
/// captures console output. Node is used instead of a headless browser because the
/// emitted runtime is browser-agnostic (it only touches <c>console.log</c> and
/// <c>globalThis</c>), which keeps the tests fast and deterministic in CI.
/// </summary>
public static class NodeJsRunner
{
    /// <summary>
    /// Node always writes UTF-8 to its stdout, but <see cref="Process"/> decodes a redirected stream
    /// with <see cref="Console.OutputEncoding"/> unless told otherwise — which on Windows is the
    /// console's OEM code page, not UTF-8. Left unset, every non-ASCII character a test prints came back
    /// mojibake on a Windows agent while passing on Linux (where that encoding is already UTF-8): the
    /// JSON suite's StringsAreEscaped read "é 中" as "├⌐ Σ╕¡", the exact CP437 reading of those UTF-8
    /// bytes. Pin the decoding to what Node actually emits, on every platform.
    /// </summary>
    private static readonly Encoding NodeOutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    /// <param name="jsCode">The JavaScript to run.</param>
    /// <param name="timeZone">An IANA zone to run Node in (its <c>TZ</c>); null inherits the host's.</param>
    /// <param name="clock">Where the JS clock starts (<c>new Date()</c>, <c>Date.now()</c>); it then
    /// runs at real speed. Null keeps the host's clock.</param>
    public static async Task<string> RunAsync(string jsCode, string? timeZone = null, DateTimeOffset? clock = null)
    {
        if (clock != null)
            jsCode = FakeClockPrelude(clock.Value) + jsCode;

        var tempFile = Path.Combine(Path.GetTempPath(), "tps_" + Guid.NewGuid().ToString("N") + ".js");
        await File.WriteAllTextAsync(tempFile, jsCode);

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = ResolveNode(),
                Arguments = "\"" + tempFile + "\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = NodeOutputEncoding,
                StandardErrorEncoding = NodeOutputEncoding,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            if (timeZone != null)
                psi.Environment["TZ"] = timeZone;

            using var process = Process.Start(psi)
                ?? throw new InvalidOperationException("Failed to start node.");

            var stdout = await process.StandardOutput.ReadToEndAsync();
            var stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"Node exited with code {process.ExitCode}.\nSTDERR:\n{stderr}\nSTDOUT:\n{stdout}");
            }

            return stdout;
        }
        finally
        {
            try { File.Delete(tempFile); } catch { /* ignore */ }
        }
    }

    /// <summary>
    /// Replaces the global <c>Date</c> with a subclass whose argument-less forms start at
    /// <paramref name="clock"/>. Every other form (and <c>instanceof Date</c>) is unchanged, so the
    /// runtime cannot tell, and only "now" moves. There is no portable way to move a process's real
    /// clock, and this is the only part of it the runtime reads.
    /// </summary>
    private static string FakeClockPrelude(DateTimeOffset clock)
    {
        var start = clock.ToUnixTimeMilliseconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
        return "(function () {\n" +
               "    var RealDate = Date, shift = " + start + " - RealDate.now();\n" +
               "    class FakeDate extends RealDate {\n" +
               "        constructor(...a) { if (a.length === 0) super(RealDate.now() + shift); else super(...a); }\n" +
               "        static now() { return RealDate.now() + shift; }\n" +
               "    }\n" +
               "    globalThis.Date = FakeDate;\n" +
               "})();\n";
    }

    private static string ResolveNode()
    {
        // Common locations; fall back to PATH.
        foreach (var candidate in new[] { "/opt/node22/bin/node", "/usr/bin/node", "/usr/local/bin/node" })
        {
            if (File.Exists(candidate)) return candidate;
        }
        return "node";
    }
}
