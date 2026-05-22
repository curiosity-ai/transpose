using H5.Contract;
using H5.Translator;
using H5.Translator.Utils;
using MessagePack;
using NuGet.Versioning;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace H5.Compiler.Hosted
{
    [MessagePackObject(keyAsPropertyName: true)]
    public class CompilationRequest
    {
        public CompilationRequest(string assemblyName, H5DotJson_AssemblySettings settings)
        {
            AssemblyName = assemblyName;
            Settings = settings;
        }

        private Dictionary<string, string> SourceCode { get; set; } = new Dictionary<string, string>();
        private Dictionary<string, string> References { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// H5 dependency bundles (".h5" archives) to inject into this compilation
        /// as if their contents were a regular project reference. The key is a
        /// short stable identifier for the bundle (used to scope its extraction
        /// directory); the value is the raw bytes of the bundle file.
        /// </summary>
        public Dictionary<string, byte[]> Bundles { get; set; } = new Dictionary<string, byte[]>();

        public string AssemblyName { get; set; } = "App";

        public bool SkipResourcesExtraction { get; set; } = false;

        public bool SkipEmbeddingResources { get; set; } = true;

        public bool SkipHtmlGeneration { get; set; } = false;


        public H5DotJson_AssemblySettings Settings { get; set; }

        private ProjectProperties ProjectProperties { get; set; } = new ProjectProperties();

        public CompilationRequest WithSourceFile(string fileName, string code)
        {
            SourceCode.Add(fileName, code);
            return this;
        }

        public CompilationRequest WithLanguageVersion(string languageVersion)
        {
            ProjectProperties.LanguageVersion = languageVersion;
            return this;
        }

        public CompilationRequest NoHTML()
        {
            SkipHtmlGeneration = true;
            return this;
        }
        
        public CompilationRequest NoPackageResources()
        {
            SkipResourcesExtraction = true;
            return this;
        }

        public CompilationRequest WithPackageReference(string packageId, NuGetVersion nuGetVersion)
        {
            References[packageId] =  $"<PackageReference Include=\"{packageId}\" Version=\"{nuGetVersion.ToString()}\" />";
            return this;
        }

        /// <summary>
        /// Injects the contents of an H5 dependency bundle (".h5" archive) into
        /// this compilation. Every assembly contained in the bundle is added to
        /// the temporary project as a <c>&lt;Reference&gt;</c>, so the compiler
        /// sees the bundle's DLLs exactly as it would a project reference.
        /// </summary>
        public CompilationRequest WithBundle(string name, byte[] bundleData)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Bundle name must be provided", nameof(name));
            if (bundleData == null || bundleData.Length == 0) throw new ArgumentException("Bundle data must be provided", nameof(bundleData));
            Bundles[name] = bundleData;
            return this;
        }

        /// <summary>
        /// Loads an H5 dependency bundle from disk and injects it into this
        /// compilation (see <see cref="WithBundle(string, byte[])"/>).
        /// </summary>
        public CompilationRequest WithBundleFile(string bundlePath)
        {
            if (string.IsNullOrWhiteSpace(bundlePath)) throw new ArgumentException("bundlePath must be provided", nameof(bundlePath));
            if (!File.Exists(bundlePath)) throw new FileNotFoundException("Dependency bundle not found", bundlePath);
            var name = Path.GetFileNameWithoutExtension(bundlePath);
            return WithBundle(name, File.ReadAllBytes(bundlePath));
        }

        public CompilationOptions ToOptions(string sourceDirectory, NuGetVersion sdkTargetVersion)
        {
            foreach(var (file, code) in SourceCode)
            {
                var fileName = Path.Combine(sourceDirectory, file);
                File.WriteAllText(fileName, code);
            }

            var bundleReferences = ExtractBundles(sourceDirectory);

            var projFile = Path.Combine(sourceDirectory, "auto-generated-project.csproj");

            File.WriteAllText(projFile,
@"

<Project Sdk=""h5.Target/$(SDKTARGET)"">
  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
    <AssemblyName>$(ASSEMBLYNAME)</AssemblyName>
    <GeneratePackageOnBuild>false</GeneratePackageOnBuild>
    <DebugType>None</DebugType>
    <DebugSymbols>false</DebugSymbols>
    <$(SER)>$(SERVAL)</$(SER)>
    <$(SHG)>$(SHGVAL)</$(SHG)>
    <$(SRE)>$(SREVAL)</$(SRE)>
  </PropertyGroup>

  <ItemGroup>
$(PKGREF)
  </ItemGroup>
  <ItemGroup>
$(BUNDLEREF)
  </ItemGroup>
</Project>"

.Replace("$(PKGREF)", string.Join("\n", References.Values))
.Replace("$(BUNDLEREF)", string.Join("\n", bundleReferences))
.Replace("$(SDKTARGET)", sdkTargetVersion.ToString())
.Replace("$(ASSEMBLYNAME)", AssemblyName)
.Replace("$(SER)", H5.Translator.Translator.ProjectPropertyNames.H5_Specific.SkipEmbeddingResources)
.Replace("$(SERVAL)", SkipEmbeddingResources ? "true" : "false")
.Replace("$(SHG)", H5.Translator.Translator.ProjectPropertyNames.H5_Specific.SkipHtmlGeneration)
.Replace("$(SHGVAL)", SkipHtmlGeneration ? "true" : "false")
.Replace("$(SRE)", H5.Translator.Translator.ProjectPropertyNames.H5_Specific.SkipResourcesExtraction)
.Replace("$(SREVAL)", SkipResourcesExtraction ? "true" : "false")
);

            return new CompilationOptions()
            {
                ProjectLocation = projFile,
                DefaultFileName = AssemblyName,
                H5Location = null,
                Rebuild = true,
                ProjectProperties = ProjectProperties
            };
        }

        private List<string> ExtractBundles(string sourceDirectory)
        {
            var references = new List<string>();
            if (Bundles == null || Bundles.Count == 0)
            {
                return references;
            }

            var bundleRoot = Path.Combine(sourceDirectory, ".h5bundles");
            Directory.CreateDirectory(bundleRoot);

            foreach (var (name, data) in Bundles)
            {
                var safeName = string.Concat(name.Select(c => char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_'));
                var targetDir = Path.Combine(bundleRoot, safeName);
                Directory.CreateDirectory(targetDir);

                var extracted = DependencyBundle.Extract(data, targetDir);

                foreach (var dll in DependencyBundle.EnumerateAssemblies(extracted))
                {
                    var includeName = Path.GetFileNameWithoutExtension(dll);
                    references.Add($"    <Reference Include=\"{includeName}\"><HintPath>{dll}</HintPath><Private>false</Private></Reference>");
                }
            }

            return references;
        }
    }
}
