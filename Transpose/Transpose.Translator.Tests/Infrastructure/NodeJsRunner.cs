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

    public static async Task<string> RunAsync(string jsCode)
    {
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
