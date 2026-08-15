#!/usr/bin/env bash
# Bootstrap the Transpose BCL with the tps compiler.
#
# The base runtime library (BCL/Transpose.BCL, package "Transpose") is special: it *defines* the C#
# BCL (System.*), so it is compiled to a self-contained reference assembly (Transpose.dll) rather
# than transpiled. Every other project (Transpose.Core and the Packages) is a JavaScript binding
# library ([assembly: External]) that the tps compiler transpiles to JS, binding against the base.
#
# This script:
#   1. Builds the base reference assembly Transpose.dll (self-contained, NoStdLib).
#   2. Builds the Transpose.Core reference assembly (binds against the base).
#   3. Runs `tps` on Transpose.Core and each Packages/* library, emitting their JS.
#
# NOTE: assembling the full runtime bundle (tps.js) from BCL/Transpose.BCL/Resources per the base
# project's tps.json (outputBy: ClassPath + resource combine) is a remaining compiler task — see
# CLAUDE.md. This script validates that the compiler resolves and transpiles the binding libraries.
set -euo pipefail
cd "$(dirname "$0")"
ROOT="$(pwd)"
OUT="$ROOT/artifacts/bootstrap"
REFS="$OUT/refs"
mkdir -p "$OUT" "$REFS"

echo "==> Building the tps compiler"
dotnet build Transpose/Transpose.Compiler/Transpose.Compiler.csproj -c Debug -v q >/dev/null
TPS="$ROOT/Transpose/Transpose.Compiler/bin/Debug/net10.0/tps.dll"

# --- helper: compile a project's C# to a self-contained reference assembly (no transpilation) ---
build_ref() {
  local proj_dir="$1" asm="$2"; shift 2
  local tmp="$OUT/_ref_$asm"
  rm -rf "$tmp"; mkdir -p "$tmp"
  cat > "$tmp/ref.csproj" <<EOF
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.1</TargetFramework>
    <AssemblyName>$asm</AssemblyName>
    <NoStdLib>true</NoStdLib><NoCompilerStandardLib>true</NoCompilerStandardLib>
    <ExcludeMscorlibFacade>true</ExcludeMscorlibFacade>
    <DisableImplicitFrameworkReferences>true</DisableImplicitFrameworkReferences>
    <GenerateAssemblyInfo>false</GenerateAssemblyInfo>
    <DebugType>None</DebugType><DebugSymbols>false</DebugSymbols>
    <LangVersion>7.2</LangVersion><AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <DefineConstants>Transpose;CORE;TRACE</DefineConstants>
    <NoWarn>1591,0626,0824,0660,0661,0169,0649,0067,0414,0108,0114</NoWarn>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="$ROOT/$proj_dir/**/*.cs" Exclude="$ROOT/$proj_dir/Resources/**/*.cs;$ROOT/$proj_dir/**/bin/**;$ROOT/$proj_dir/**/obj/**" />
$(for r in "$@"; do echo "    <Reference Include=\"$r\"><HintPath>$REFS/$r.dll</HintPath></Reference>"; done)
  </ItemGroup>
$( [ "$asm" = "Transpose" ] && cat <<X
  <ItemGroup>
    <Compile Remove="$ROOT/$proj_dir/shared/System/DateTime.cs" />
    <Compile Remove="$ROOT/$proj_dir/shared/System/Globalization/InternalGlobalizationHelper.cs" />
    <Compile Remove="$ROOT/$proj_dir/shared/System/Reflection/MethodInfo.cs" />
    <Compile Remove="$ROOT/$proj_dir/shared/System/Resources/RuntimeResourceSet.cs" />
  </ItemGroup>
X
)
</Project>
EOF
  dotnet build "$tmp/ref.csproj" -c Debug -v q -o "$tmp/bin" >/dev/null
  cp "$tmp/bin/$asm.dll" "$REFS/$asm.dll"
  echo "    built $REFS/$asm.dll"
}

echo "==> Building base reference assembly (Transpose.dll)"
build_ref "BCL/Transpose.BCL" "Transpose"
export TRANSPOSE_DLL_PATH="$REFS/Transpose.dll"

echo "==> Building Transpose.Core reference assembly"
build_ref "BCL/Transpose.Core" "Transpose.Core" "Transpose"

# --- transpile the binding libraries with tps ---
transpile() {
  local csproj="$1" name; name="$(basename "$csproj" .csproj)"; shift
  local extra=(); for r in "$@"; do extra+=(--reference "$REFS/$r.dll"); done
  echo "==> tps $name"
  if dotnet "$TPS" "$csproj" --out "$OUT/$name.js" --quiet "${extra[@]}" 2>&1 | tail -3; then :; fi
}

transpile BCL/Transpose.Core/Transpose.Core.csproj
transpile Packages/Transpose.Newtonsoft.Json/Transpose.Newtonsoft.Json.csproj
transpile Packages/Transpose.System.Text.Json/Transpose.System.Text.Json.csproj
transpile Packages/Transpose.Howler/Transpose.Howler.csproj        Transpose.Core
transpile Packages/Transpose.WebGL2/Transpose.WebGL2.csproj        Transpose.Core
transpile Packages/Transpose.P2/Transpose.P2.csproj                Transpose.Core
transpile Packages/Transpose.HttpClient/Transpose.HttpClient.csproj Transpose.Core

echo "==> Done. JS outputs in $OUT"
ls -la "$OUT"/*.js 2>/dev/null || true
