namespace Transpose.Translator;

/// <summary>
/// Where reflection metadata (Transpose.setMetadata registrations) is written, mirroring the
/// existing compiler's <c>reflection.target</c> tps.json setting.
/// </summary>
public enum MetadataTarget
{
    /// <summary>A separate <c>&lt;name&gt;.meta.js</c> file (the tps default for libraries).</summary>
    File,

    /// <summary>Inline, inside the same assembly function as the generated types.</summary>
    Inline,

    /// <summary>Reserved (per-type metadata) — treated as Inline here.</summary>
    Type,

    /// <summary>Reserved (single assembly metadata) — treated as File here.</summary>
    Assembly,
}
