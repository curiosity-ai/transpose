using System.Text;
using System.Text.Json;

namespace Transpose.Compiler;

/// <summary>
/// Assembles the base runtime's JavaScript bundles (tps.js, tps.meta.js, …) from a project's
/// tps.json <c>resources</c> recipe: each resource is <c>header + Σ(remark + file)</c>, where files
/// are the hand-written <c>Resources/*.js</c> primitives interleaved with the compiler-emitted
/// <c>Resources/.generated/*.js</c> per-class files (outputBy: ClassPath). This is the stitching the
/// legacy H5 compiler did to produce h5.js; here it produces tps.js.
/// </summary>
internal static class RuntimeAssembler
{
    public static List<(string name, byte[] bytes)> Assemble(string projectDir)
    {
        var tpsJson = Path.Combine(projectDir, "tps.json");
        var result = new List<(string, byte[])>();
        if (!File.Exists(tpsJson)) return result;

        using var doc = JsonDocument.Parse(File.ReadAllText(tpsJson), new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        });
        if (!doc.RootElement.TryGetProperty("resources", out var resources)) return result;

        var year = System.DateTime.Now.Year.ToString(System.Globalization.CultureInfo.InvariantCulture);

        foreach (var r in resources.EnumerateArray())
        {
            if (!r.TryGetProperty("name", out var nameEl)) continue;
            var name = nameEl.GetString();
            if (string.IsNullOrEmpty(name) || !name!.EndsWith(".js", System.StringComparison.OrdinalIgnoreCase)) continue;
            if (!r.TryGetProperty("files", out var files) || files.ValueKind != JsonValueKind.Array) continue;

            var remark = r.TryGetProperty("remark", out var rk) ? rk.GetString() ?? "" : "";
            var sb = new StringBuilder();

            if (r.TryGetProperty("header", out var hdrEl) && hdrEl.GetString() is { } hdr)
            {
                var hp = Path.Combine(projectDir, hdr.Replace('\\', '/'));
                if (File.Exists(hp))
                    sb.Append(File.ReadAllText(hp).Replace("{version}", "").Replace("{year}", year));
            }

            foreach (var f in files.EnumerateArray())
            {
                var rel = f.GetString();
                if (string.IsNullOrEmpty(rel)) continue;
                var path = Path.Combine(projectDir, rel!.Replace('\\', '/'));
                if (!File.Exists(path))
                {
                    MsBuildDiagnostic.WriteWarning(MsBuildDiagnostic.CodeMissingRuntimeBundle,
                        $"runtime bundle file missing: {rel}");
                    continue;
                }
                if (remark.Length > 0) sb.Append(remark.Replace("{name}", Path.GetFileName(path)));
                sb.Append(File.ReadAllText(path));
            }

            result.Add((name!, Encoding.UTF8.GetBytes(sb.ToString())));
        }
        return result;
    }
}
