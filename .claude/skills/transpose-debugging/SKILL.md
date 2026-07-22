---
name: transpose-debugging
description: >-
  Reproduce, inspect, and diagnose how the Transpose C#→JS compiler translates a construct, using a
  fast emit→Node→native-.NET loop. Use this WHENEVER you are investigating a suspected
  mis-compilation, a runtime error in emitted JS, or "what JavaScript does Transpose produce for
  X?" — including reproducing a bug report, checking a snippet before/after an emitter change, or
  confirming a fix behaves like native .NET. Triggers on any work in the transpose repo that
  involves running or inspecting emitted JavaScript, comparing transpose output to native .NET, or
  debugging the emitter (Transpose/Transpose.Translator/Emit/*.cs) or runtime (BCL Resources/*.js,
  tps.shim.js). Prefer this over ad-hoc `dotnet run` or hand-built test projects.
---

# Debugging the Transpose compiler

Transpose compiles C# to JavaScript via Roslyn. The fastest way to understand or fix a
mis-compilation is to translate a tiny C# snippet, look at the emitted JS, run it in Node, and diff
the result against what native .NET does. This skill sets up that loop and captures the pitfalls
that otherwise cost hours.

## One-time setup (per session/container)

Run the setup script — it builds the Release translator and three tiny runner projects in `/tmp`,
and builds the runtime `Transpose.dll` if missing:

```bash
bash .claude/skills/transpose-debugging/scripts/setup-toolkit.sh
```

Then export the runtime path it prints (every runner needs it — it makes the runner use YOUR
freshly-built runtime, not the NuGet-cached one):

```bash
export TRANSPOSE_DLL_PATH=<repo>/BCL/Transpose.BCL/bin/Debug/netstandard2.0/Transpose.dll
```

## The loop

Write a **full-program** snippet to `/tmp` (keep it OUT of the runner dirs — a stray `.cs` there
gives "more than one entry point"):

```csharp
using System;
public class Program { public static void Main() { Console.WriteLine("hi " + (1+2)); } }
```

Then:

```bash
# 1. See the emitted JS (no execution) — read it to spot the wrong construct
dotnet /tmp/emitrunner/bin/Debug/net10.0/emitrunner.dll /tmp/snippet.cs

# 2. Run it in Node — the runtime auto-runs Main()
dotnet /tmp/emitrunner/bin/Debug/net10.0/emitrunner.dll /tmp/snippet.cs --run 2>&1 | node

# 3. Compare against native .NET. Usually you can reason out native behaviour; when unsure, run it:
#    (cd /tmp && rm -rf nat && mkdir nat && cd nat && dotnet new console -o . >/dev/null \
#      && cp /tmp/snippet.cs Program.cs && dotnet run)
```

A **behavioural bug** is any case where transpose's Node output differs from native .NET's. Cosmetic
differences in the emitted JS (variable names, spacing) are NOT bugs — only observable behaviour is.

If translation *fails* (a `TRANSLATION FAILED` banner), run without `--run` to read the Roslyn/emit
diagnostics. Distinguish a **real C# error in your snippet** (e.g. positional-pattern on `object`
has no `Deconstruct`) from a **missing BCL API** (`'string' does not contain a definition for X` — a
BCL gap, not a silent behavioural bug) from an **emitter crash**.

## Finding and fixing the root cause

The emitter is a syntax-tree walk under `Transpose/Transpose.Translator/Emit/`:
`Emitter.Expressions*.cs`, `Emitter.Statements.cs`, `Emitter.Members.cs`, `Emitter.Patterns.cs`,
`Emitter.Types.cs`, `Emitter.ValueTypes.cs`, `Emitter.Query.cs`, `Emitter.Reflection.cs`. Member/JS
name resolution lives in `Support/TransposeNaming.cs` and `Support/NameMangler.cs`. The language shim
is `Transpose/Transpose.Translator/Runtime/tps.shim.js`; the hand-written runtime primitives are
`BCL/Transpose.BCL/Resources/*.js`.

Typical diagnosis: grep the emitter for how the *working* form is emitted (e.g. normal member access
→ `TransposeNaming.MemberJsName(symbol)`) and compare with the broken path (e.g. a pattern emitting a
raw identifier). The fix is usually routing the broken path through the same resolver.

## Pitfalls that waste time — read these

- **Rebuild the RIGHT thing, and confirm it.** The emit runner references the **Release**
  `Transpose.Translator.dll`; the `tps` compiler / `--build-runtime` / the test suite use the
  **Debug** build. After editing the emitter, rebuild **both** configs. `dotnet build -v q | tail`
  HIDES compile errors — always confirm `Build succeeded / 0 Error(s)`, or the runner silently uses
  the previous DLL.
- **Rebuild the runner too.** The runner copies `Transpose.Translator.dll` into its own `bin/`, so
  after rebuilding the Release translator you must `dotnet build /tmp/emitrunner/emitrunner.csproj`
  (re-running `setup-toolkit.sh` does this) or it keeps using the stale copy. This is the #1 "my fix
  didn't take" trap.
- **Rebuild the runtime when you touch runtime JS or the BCL.** Changes to `Resources/*.js` or the
  BCL C# need `--build-runtime` (see the `transpose-runtime-and-bcl` skill). Shim changes
  (`tps.shim.js`) are embedded in the **translator** — rebuild the translator, not the runtime.
- **Node vs browser.** With no DOM, `Transpose.ready` fires synchronously — don't rely on
  load-order-sensitive ordering in Node-only checks.
- **`Console.Write` (no newline) appends a newline per call** under the Node harness (console.log
  semantics, shared with h5). Build a string and `WriteLine` once rather than asserting on
  `Console.Write` spacing.
- **Boxed value types.** Enums box to a `Transpose.box` object (carrying the enum type) — not a
  plain number; primitives (int/string/bool) box to plain JS values. `Transpose.getDefaultValue(type)`
  is the runtime-dispatched default (handles BCL structs).

## Related skills

- Hunting for divergences systematically against the proven-correct h5 output → **transpose-h5-audit**.
- Rebuilding the runtime/BCL, adding a BCL API, or adding a regression test → **transpose-runtime-and-bcl**.
