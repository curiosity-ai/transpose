using System;
using System.Collections.Generic;
using System.Linq;

namespace H5.Translator
{
    /// <summary>
    /// Kill switches for individual rewrite cases of the pre-translation rewrite
    /// pipeline (see docs/REWRITE-REMOVAL-PLAN.md). A case listed in the
    /// H5_DISABLE_REWRITE environment variable (comma/semicolon separated,
    /// e.g. "S45,R1") skips that lowering, so the effect of removing it can be
    /// probed and the suite can be run both ways while a case is being migrated
    /// out of the rewriter. Case IDs match the plan document.
    /// </summary>
    internal static class RewriteSwitches
    {
        public const string EnvVar = "H5_DISABLE_REWRITE";

        private static readonly HashSet<string> _disabled = new HashSet<string>(
            (Environment.GetEnvironmentVariable(EnvVar) ?? string.Empty)
                .Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim()),
            StringComparer.OrdinalIgnoreCase);

        public static bool AnyDisabled => _disabled.Count > 0;

        /// <summary>Raw env-var value, folded into the rewriter cache hash so toggling switches never reuses stale cached output.</summary>
        public static string CacheKeyComponent => Environment.GetEnvironmentVariable(EnvVar) ?? string.Empty;

        public static bool Disabled(string caseId) => _disabled.Contains(caseId);
    }
}
