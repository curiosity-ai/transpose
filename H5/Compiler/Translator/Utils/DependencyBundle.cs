using Microsoft.Extensions.Logging;
using Mosaik.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using ZLogger;

namespace H5.Translator.Utils
{
    /// <summary>
    /// Reads and writes H5 dependency bundles.
    ///
    /// An H5 dependency bundle (".h5" file) is a zip archive containing every file
    /// that was produced into the project's binary output directory during
    /// compilation, excluding the H5 javascript/html output folder itself. It can
    /// later be fed back to the compiler so its contents (mainly .NET assemblies)
    /// behave as a project reference for a subsequent compilation.
    /// </summary>
    public static class DependencyBundle
    {
        public const string FileExtension = ".h5";

        private static readonly ILogger Logger = ApplicationLogging.CreateLogger("H5.Translator.Utils.DependencyBundle");

        /// <summary>
        /// Creates a bundle archive at <paramref name="bundlePath"/> containing every
        /// file under <paramref name="sourceDirectory"/> that is not located inside
        /// <paramref name="excludeDirectory"/> (typically the H5 output folder).
        /// </summary>
        public static int Create(string sourceDirectory, string excludeDirectory, string bundlePath)
        {
            if (string.IsNullOrWhiteSpace(sourceDirectory))
            {
                throw new ArgumentException("sourceDirectory must be provided", nameof(sourceDirectory));
            }

            if (string.IsNullOrWhiteSpace(bundlePath))
            {
                throw new ArgumentException("bundlePath must be provided", nameof(bundlePath));
            }

            sourceDirectory = Path.GetFullPath(sourceDirectory);
            var normalizedExclude = string.IsNullOrEmpty(excludeDirectory)
                ? null
                : NormalizeDirectory(Path.GetFullPath(excludeDirectory));
            var normalizedBundlePath = Path.GetFullPath(bundlePath);

            if (!Directory.Exists(sourceDirectory))
            {
                Logger.ZLogWarning("Dependency bundle source directory does not exist: {0}", sourceDirectory);
                return 0;
            }

            var bundleDir = Path.GetDirectoryName(normalizedBundlePath);
            if (!string.IsNullOrEmpty(bundleDir))
            {
                Directory.CreateDirectory(bundleDir);
            }

            if (File.Exists(normalizedBundlePath))
            {
                File.Delete(normalizedBundlePath);
            }

            var sourcePrefix = NormalizeDirectory(sourceDirectory);
            var fileCount = 0;

            using (var fs = File.Create(normalizedBundlePath))
            using (var archive = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                foreach (var file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
                {
                    var fullPath = Path.GetFullPath(file);

                    if (string.Equals(fullPath, normalizedBundlePath, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (normalizedExclude != null &&
                        fullPath.StartsWith(normalizedExclude, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var entryName = fullPath.StartsWith(sourcePrefix, StringComparison.OrdinalIgnoreCase)
                        ? fullPath.Substring(sourcePrefix.Length)
                        : Path.GetFileName(fullPath);

                    entryName = entryName.Replace(Path.DirectorySeparatorChar, '/').TrimStart('/');

                    var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
                    using (var entryStream = entry.Open())
                    using (var src = File.OpenRead(fullPath))
                    {
                        src.CopyTo(entryStream);
                    }

                    fileCount++;
                }
            }

            Logger.ZLogInformation("Created H5 dependency bundle '{0}' with {1} entries (source '{2}', excluded '{3}')",
                normalizedBundlePath, fileCount, sourceDirectory, normalizedExclude ?? "<none>");

            return fileCount;
        }

        /// <summary>
        /// Extracts the bundle at <paramref name="bundlePath"/> into
        /// <paramref name="targetDirectory"/>. Returns the list of full paths of
        /// every file written to disk.
        /// </summary>
        public static List<string> Extract(string bundlePath, string targetDirectory)
        {
            if (string.IsNullOrWhiteSpace(bundlePath))
            {
                throw new ArgumentException("bundlePath must be provided", nameof(bundlePath));
            }

            if (string.IsNullOrWhiteSpace(targetDirectory))
            {
                throw new ArgumentException("targetDirectory must be provided", nameof(targetDirectory));
            }

            if (!File.Exists(bundlePath))
            {
                throw new FileNotFoundException("Dependency bundle not found", bundlePath);
            }

            targetDirectory = Path.GetFullPath(targetDirectory);
            Directory.CreateDirectory(targetDirectory);
            var targetPrefix = NormalizeDirectory(targetDirectory);

            var extracted = new List<string>();

            using (var fs = File.OpenRead(bundlePath))
            using (var archive = new ZipArchive(fs, ZipArchiveMode.Read))
            {
                foreach (var entry in archive.Entries)
                {
                    if (string.IsNullOrEmpty(entry.Name))
                    {
                        continue;
                    }

                    var destination = Path.GetFullPath(Path.Combine(targetDirectory, entry.FullName));

                    if (!destination.StartsWith(targetPrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException(
                            $"Refusing to extract bundle entry '{entry.FullName}' outside the target directory '{targetDirectory}'");
                    }

                    var destinationDir = Path.GetDirectoryName(destination);
                    if (!string.IsNullOrEmpty(destinationDir))
                    {
                        Directory.CreateDirectory(destinationDir);
                    }

                    using (var entryStream = entry.Open())
                    using (var outStream = File.Create(destination))
                    {
                        entryStream.CopyTo(outStream);
                    }

                    extracted.Add(destination);
                }
            }

            Logger.ZLogInformation("Extracted H5 dependency bundle '{0}' into '{1}' ({2} files)",
                bundlePath, targetDirectory, extracted.Count);

            return extracted;
        }

        /// <summary>
        /// Extracts the bundle stored in <paramref name="bundleData"/> into
        /// <paramref name="targetDirectory"/>. Returns the list of full paths of
        /// every file written to disk.
        /// </summary>
        public static List<string> Extract(byte[] bundleData, string targetDirectory)
        {
            if (bundleData == null) throw new ArgumentNullException(nameof(bundleData));
            if (string.IsNullOrWhiteSpace(targetDirectory)) throw new ArgumentException("targetDirectory must be provided", nameof(targetDirectory));

            targetDirectory = Path.GetFullPath(targetDirectory);
            Directory.CreateDirectory(targetDirectory);
            var targetPrefix = NormalizeDirectory(targetDirectory);

            var extracted = new List<string>();

            using (var ms = new MemoryStream(bundleData, writable: false))
            using (var archive = new ZipArchive(ms, ZipArchiveMode.Read))
            {
                foreach (var entry in archive.Entries)
                {
                    if (string.IsNullOrEmpty(entry.Name))
                    {
                        continue;
                    }

                    var destination = Path.GetFullPath(Path.Combine(targetDirectory, entry.FullName));

                    if (!destination.StartsWith(targetPrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException(
                            $"Refusing to extract bundle entry '{entry.FullName}' outside the target directory '{targetDirectory}'");
                    }

                    var destinationDir = Path.GetDirectoryName(destination);
                    if (!string.IsNullOrEmpty(destinationDir))
                    {
                        Directory.CreateDirectory(destinationDir);
                    }

                    using (var entryStream = entry.Open())
                    using (var outStream = File.Create(destination))
                    {
                        entryStream.CopyTo(outStream);
                    }

                    extracted.Add(destination);
                }
            }

            return extracted;
        }

        /// <summary>
        /// Returns the .dll paths from <paramref name="extractedFiles"/>.
        /// </summary>
        public static IEnumerable<string> EnumerateAssemblies(IEnumerable<string> extractedFiles)
        {
            return extractedFiles?.Where(p => p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                   ?? Enumerable.Empty<string>();
        }

        private static string NormalizeDirectory(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return path;
            }

            var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return trimmed + Path.DirectorySeparatorChar;
        }
    }
}
