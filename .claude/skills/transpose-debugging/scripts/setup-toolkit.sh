#!/usr/bin/env bash
# Sets up the Transpose emit/debug toolkit used to inspect and validate the JavaScript that the
# compiler emits for a C# snippet, and to run it in Node and compare against native .NET.
#
# What it builds (idempotent — safe to re-run):
#   * The Release Transpose.Translator.dll (referenced by the runners).
#   * /tmp/jsdumper   — dumps embedded .js manifest resources from a DLL (h5 corpus + built packages).
#   * /tmp/emitrunner — prints/`--run`s the transpose output for a plain C# snippet.
#   * /tmp/jsonrunner — same but with the Transpose.Newtonsoft.Json binding referenced + prepended.
#
# It also builds the runtime Transpose.dll (BCL) if it is missing and prints the TRANSPOSE_DLL_PATH
# you must export before invoking any runner.
#
# Usage:  bash .claude/skills/transpose-debugging/scripts/setup-toolkit.sh
#         (then follow the printed `export TRANSPOSE_DLL_PATH=...` line)
set -euo pipefail

REPO="$(git -C "$(dirname "${BASH_SOURCE[0]}")" rev-parse --show-toplevel)"
TR_REL_DLL="$REPO/Transpose/Transpose.Translator/bin/Release/net10.0/Transpose.Translator.dll"
RUNTIME_DLL="$REPO/BCL/Transpose.BCL/bin/Debug/netstandard2.0/Transpose.dll"

echo "repo: $REPO"

echo "==> building Release Transpose.Translator (referenced by the runners)"
dotnet build "$REPO/Transpose/Transpose.Translator/Transpose.Translator.csproj" -c Release \
  | grep -E "error|Build succeeded" | tail -3

# --- jsdumper -------------------------------------------------------------------------------------
mkdir -p /tmp/jsdumper
cat > /tmp/jsdumper/jsdumper.csproj <<'EOF'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>disable</Nullable>
    <AssemblyName>jsdumper</AssemblyName>
  </PropertyGroup>
</Project>
EOF
cat > /tmp/jsdumper/Program.cs <<'EOF'
using System;
using System.IO;
using System.Reflection;
class P {
  static void Main(string[] args) {
    var dll = args[0]; var outDir = args[1];
    Directory.CreateDirectory(outDir);
    var asm = Assembly.LoadFrom(Path.GetFullPath(dll));
    foreach (var name in asm.GetManifestResourceNames()) {
      using var s = asm.GetManifestResourceStream(name);
      if (s == null) continue;
      using var ms = new MemoryStream(); s.CopyTo(ms);
      var bytes = ms.ToArray();
      File.WriteAllBytes(Path.Combine(outDir, name.Replace(Path.DirectorySeparatorChar, '_')), bytes);
      Console.WriteLine($"{name}\t{bytes.Length}");
    }
  }
}
EOF

# --- emitrunner -----------------------------------------------------------------------------------
mkdir -p /tmp/emitrunner
cat > /tmp/emitrunner/emitrunner.csproj <<EOF
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>disable</Nullable>
    <ImplicitUsings>disable</ImplicitUsings>
    <AssemblyName>emitrunner</AssemblyName>
  </PropertyGroup>
  <ItemGroup>
    <Reference Include="Transpose.Translator"><HintPath>$TR_REL_DLL</HintPath></Reference>
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="5.3.0" />
  </ItemGroup>
</Project>
EOF
cat > /tmp/emitrunner/Program.cs <<'EOF'
using System;
using System.IO;
using Transpose.Translator;
class P {
    static void Main(string[] args) {
        if (args.Length == 0) { Console.Error.WriteLine("usage: <file.cs> [--run]"); return; }
        var r = new RoslynTranslator().Translate(File.ReadAllText(args[0]));
        if (!r.Success) {
            Console.Error.WriteLine("TRANSLATION FAILED:");
            foreach (var d in r.Diagnostics) Console.Error.WriteLine("  " + d.GetMessage());
            return;
        }
        Console.WriteLine(args.Length > 1 && args[1] == "--run"
            ? RoslynTranslator.LoadRuntime() + "\n" + r.Javascript
            : r.Javascript);
    }
}
EOF

# --- jsonrunner (Newtonsoft) ----------------------------------------------------------------------
NSJ_DLL="$REPO/Packages/Transpose.Newtonsoft.Json/bin/Debug/netstandard2.0/Transpose.Newtonsoft.Json.dll"
mkdir -p /tmp/jsonrunner
cat > /tmp/jsonrunner/jsonrunner.csproj <<EOF
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>disable</Nullable>
    <ImplicitUsings>disable</ImplicitUsings>
    <AssemblyName>jsonrunner</AssemblyName>
  </PropertyGroup>
  <ItemGroup>
    <Reference Include="Transpose.Translator"><HintPath>$TR_REL_DLL</HintPath></Reference>
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="5.3.0" />
  </ItemGroup>
</Project>
EOF
cat > /tmp/jsonrunner/Program.cs <<EOF
using System;
using System.IO;
using Transpose.Translator;
class P {
    static void Main(string[] args) {
        if (args.Length == 0) { Console.Error.WriteLine("usage: <file.cs> [--run]"); return; }
        var nsjRef = "$NSJ_DLL";
        var r = new RoslynTranslator().Translate(new[] { ("App.cs", File.ReadAllText(args[0])) }, "App", new[] { nsjRef });
        if (!r.Success) {
            Console.Error.WriteLine("TRANSLATION FAILED:");
            foreach (var d in r.Diagnostics) Console.Error.WriteLine("  " + d.GetMessage());
            return;
        }
        if (args.Length > 1 && args[1] == "--run") {
            var nsjJs   = File.ReadAllText("/tmp/nsjjs/newtonsoft.json.js");
            var nsjMeta = File.ReadAllText("/tmp/nsjjs/generated.meta.js");
            Console.WriteLine(RoslynTranslator.LoadRuntime() + "\n" + nsjJs + "\n" + nsjMeta + "\n" + r.Javascript);
        } else {
            Console.WriteLine(r.Javascript);
        }
    }
}
EOF

echo "==> building the runner projects"
for r in jsdumper emitrunner jsonrunner; do
  dotnet build "/tmp/$r/$r.csproj" -c Debug | grep -E "error|Build succeeded" | tail -1
done

if [ ! -f "$RUNTIME_DLL" ]; then
  echo "==> runtime Transpose.dll missing — building it (~25s)"
  dotnet "$REPO/Transpose/Transpose.Compiler/bin/Debug/net10.0/tps.dll" \
    --project "$REPO/BCL/Transpose.BCL/Transpose.BCL.csproj" --build-runtime -c Debug \
    -o "$RUNTIME_DLL" | tail -3
fi

cat <<EOF

================================================================================
Toolkit ready. Before invoking any runner, export the runtime path:

  export TRANSPOSE_DLL_PATH=$RUNTIME_DLL

Inspect emitted JS:   dotnet /tmp/emitrunner/bin/Debug/net10.0/emitrunner.dll /tmp/snippet.cs
Run in Node:          dotnet /tmp/emitrunner/bin/Debug/net10.0/emitrunner.dll /tmp/snippet.cs --run 2>&1 | node
Dump JS from a DLL:   dotnet /tmp/jsdumper/bin/Debug/net10.0/jsdumper.dll <path-to.dll> <outdir>

Snippets must be a full program: class Program { public static void Main() { ... } }
Keep snippets in /tmp (NOT in a runner dir — a stray .cs there breaks the build).
================================================================================
EOF
