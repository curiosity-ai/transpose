using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.X86;
using System.Text;

namespace Transpose.Bench;

/// <summary>
/// Describes the machine a benchmark ran on. Compiler timings are meaningless without it: the same
/// build can take 8 s or 40 s depending on core count and memory bandwidth, so every report leads
/// with the CPU model, core/thread counts, RAM, and the SIMD/crypto instruction sets available (the
/// JIT's vector width in particular changes how fast Roslyn's own hot loops run).
/// </summary>
internal sealed record MachineInfo(
    string CpuModel,
    int PhysicalCores,
    int LogicalCores,
    double MaxMhz,
    long TotalRamBytes,
    long AvailableRamBytes,
    string Os,
    string Architecture,
    string Runtime,
    string GcMode,
    IReadOnlyList<string> Capabilities)
{
    public static MachineInfo Collect()
    {
        var (model, physical, mhz) = ReadCpuDetails();
        var (totalRam, availRam) = ReadMemory();

        return new MachineInfo(
            CpuModel: model,
            PhysicalCores: physical,
            LogicalCores: Environment.ProcessorCount,
            MaxMhz: mhz,
            TotalRamBytes: totalRam,
            AvailableRamBytes: availRam,
            Os: RuntimeInformation.OSDescription.Trim(),
            Architecture: RuntimeInformation.ProcessArchitecture.ToString(),
            Runtime: RuntimeInformation.FrameworkDescription,
            GcMode: (System.Runtime.GCSettings.IsServerGC ? "Server" : "Workstation")
                    + (System.Runtime.GCSettings.LatencyMode == System.Runtime.GCLatencyMode.SustainedLowLatency ? ", SustainedLowLatency" : "")
                    + (GCSettings_Concurrent() ? ", concurrent" : ", non-concurrent"),
            Capabilities: DetectCapabilities());
    }

    private static bool GCSettings_Concurrent()
    {
        // There is no public API for "is background GC enabled"; the config switch is the closest
        // observable. Absent config it defaults to on.
        var v = AppContext.GetData("System.GC.Concurrent")?.ToString();
        return v is null || !string.Equals(v, "false", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>CPU model string, physical core count and peak clock. Read from
    /// <c>/proc/cpuinfo</c> + <c>/sys</c> on Linux (the only place the model name is exposed without
    /// P/Invoke); other platforms fall back to what the runtime reports.</summary>
    private static (string model, int physicalCores, double maxMhz) ReadCpuDetails()
    {
        var model = RuntimeInformation.ProcessArchitecture.ToString() + " CPU";
        var physical = Environment.ProcessorCount;
        double mhz = 0;

        if (OperatingSystem.IsLinux())
        {
            try
            {
                var lines = File.ReadAllLines("/proc/cpuinfo");
                var coreIds = new HashSet<string>();
                string? physicalId = null;
                foreach (var line in lines)
                {
                    var idx = line.IndexOf(':');
                    if (idx < 0) continue;
                    var key = line.AsSpan(0, idx).Trim().ToString();
                    var value = line.AsSpan(idx + 1).Trim().ToString();
                    switch (key)
                    {
                        case "model name" or "Model" or "Hardware" or "cpu model":
                            if (value.Length > 0) model = value;
                            break;
                        case "physical id":
                            physicalId = value;
                            break;
                        case "core id":
                            coreIds.Add((physicalId ?? "0") + "/" + value);
                            break;
                        case "cpu MHz":
                            if (double.TryParse(value, CultureInfo.InvariantCulture, out var m)) mhz = Math.Max(mhz, m);
                            break;
                    }
                }
                if (coreIds.Count > 0) physical = coreIds.Count;
            }
            catch { /* best effort */ }

            // The advertised maximum turbo frequency, when the kernel exposes it — a better
            // normalisation input than the momentary "cpu MHz" above.
            foreach (var f in new[] { "/sys/devices/system/cpu/cpu0/cpufreq/cpuinfo_max_freq" })
            {
                try
                {
                    if (File.Exists(f) && double.TryParse(File.ReadAllText(f).Trim(), CultureInfo.InvariantCulture, out var khz))
                        mhz = Math.Max(mhz, khz / 1000.0);
                }
                catch { /* best effort */ }
            }
        }

        return (model, physical, mhz);
    }

    /// <summary>Total and currently-available RAM. Inside a container the cgroup limit is what
    /// actually applies, so prefer the runtime's view (which honours it) and cross-check
    /// <c>/proc/meminfo</c>.</summary>
    private static (long total, long available) ReadMemory()
    {
        var info = GC.GetGCMemoryInfo();
        long total = info.TotalAvailableMemoryBytes;
        long available = 0;

        if (OperatingSystem.IsLinux())
        {
            try
            {
                foreach (var line in File.ReadLines("/proc/meminfo"))
                {
                    var parts = line.Split(':', 2);
                    if (parts.Length != 2) continue;
                    var kbText = parts[1].Replace("kB", "").Trim();
                    if (!long.TryParse(kbText, CultureInfo.InvariantCulture, out var kb)) continue;
                    if (parts[0] == "MemTotal" && total <= 0) total = kb * 1024;
                    else if (parts[0] == "MemAvailable") available = kb * 1024;
                }
            }
            catch { /* best effort */ }
        }
        return (total, available);
    }

    /// <summary>The instruction-set extensions the JIT will actually use in this process. Reported
    /// because they gate how fast Roslyn's (and the emitter's) string/span work runs — the same
    /// binary is materially faster on a machine where AVX-512 or SVE is enabled.</summary>
    private static List<string> DetectCapabilities()
    {
        var caps = new List<string>();

        void Add(string name, bool supported) { if (supported) caps.Add(name); }

        Add($"Vector<T>={System.Numerics.Vector<byte>.Count * 8}bit", true);
        Add("Vector64.HwAccel", Vector64.IsHardwareAccelerated);
        Add("Vector128.HwAccel", Vector128.IsHardwareAccelerated);
        Add("Vector256.HwAccel", Vector256.IsHardwareAccelerated);
        Add("Vector512.HwAccel", Vector512.IsHardwareAccelerated);

        var arch = RuntimeInformation.ProcessArchitecture;
        if (arch is System.Runtime.InteropServices.Architecture.X86 or System.Runtime.InteropServices.Architecture.X64)
        {
            Add("SSE2", Sse2.IsSupported);
            Add("SSE3", Sse3.IsSupported);
            Add("SSSE3", Ssse3.IsSupported);
            Add("SSE4.1", Sse41.IsSupported);
            Add("SSE4.2", Sse42.IsSupported);
            Add("AVX", Avx.IsSupported);
            Add("AVX2", Avx2.IsSupported);
            Add("AVX512F", Avx512F.IsSupported);
            Add("AVX512BW", Avx512BW.IsSupported);
            Add("AVX512VBMI", Avx512Vbmi.IsSupported);
            Add("AVX10v1", Avx10v1.IsSupported);
            Add("AVX-VNNI", AvxVnni.IsSupported);
            Add("FMA", Fma.IsSupported);
            Add("BMI1", Bmi1.IsSupported);
            Add("BMI2", Bmi2.IsSupported);
            Add("LZCNT", Lzcnt.IsSupported);
            Add("POPCNT", Popcnt.IsSupported);
            Add("AES", System.Runtime.Intrinsics.X86.Aes.IsSupported);
            Add("PCLMULQDQ", Pclmulqdq.IsSupported);
        }
        else if (arch is System.Runtime.InteropServices.Architecture.Arm64 or System.Runtime.InteropServices.Architecture.Arm)
        {
            Add("AdvSIMD", AdvSimd.IsSupported);
            Add("AdvSIMD.Arm64", AdvSimd.Arm64.IsSupported);
            Add("CRC32", Crc32.IsSupported);
            Add("AES", System.Runtime.Intrinsics.Arm.Aes.IsSupported);
            Add("SHA1", Sha1.IsSupported);
            Add("SHA256", Sha256.IsSupported);
            Add("Dp", Dp.IsSupported);
            Add("Rdm", Rdm.IsSupported);
#pragma warning disable SYSLIB5003 // Sve is experimental; reporting its availability is exactly the point
            Add("SVE", Sve.IsSupported);
#pragma warning restore SYSLIB5003
        }

        return caps;
    }

    public string Describe()
    {
        var sb = new StringBuilder();
        sb.AppendLine("Machine");
        sb.AppendLine($"  CPU:          {CpuModel}");
        sb.AppendLine($"  Cores:        {PhysicalCores} physical / {LogicalCores} logical"
            + (MaxMhz > 0 ? $"  @ {MaxMhz / 1000.0:F2} GHz max" : ""));
        sb.AppendLine($"  RAM:          {Fmt.Gb(TotalRamBytes)} total"
            + (AvailableRamBytes > 0 ? $", {Fmt.Gb(AvailableRamBytes)} available" : ""));
        sb.AppendLine($"  OS:           {Os} ({Architecture})");
        sb.AppendLine($"  Runtime:      {Runtime}");
        sb.AppendLine($"  GC:           {GcMode}");
        sb.AppendLine($"  CPU features: {string.Join(", ", Capabilities)}");
        return sb.ToString();
    }
}
