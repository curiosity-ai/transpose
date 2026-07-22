# ATTRIBUTES.md — special-case attributes in Transpose

This document catalogues every attribute that the Transpose compiler *special-cases* to drive
JavaScript generation — i.e. attributes the translator looks for by name and acts on, as opposed to
ordinary attributes that are merely carried into reflection metadata.

All Transpose codegen attributes live in the **`Transpose`** namespace and are defined in
`BCL/Transpose.BCL/Attributes/`. A handful of Web-binding markers live in **`Transpose.Core`**
(`BCL/Transpose.Core/`). The translator matches an attribute by its fully-qualified name through
`TransposeNaming.AttrIs(attributeData, "Transpose.XxxAttribute")`; the match strings must stay in
sync with the C# namespaces in the BCL.

The catalogue is split into:

1. **General-purpose attributes** — meaningful in any Transpose project (application or library).
2. **BCL / Core binding attributes** — used to author the runtime (`Transpose.dll`) and the
   `[assembly: External]` binding libraries (`Transpose.Core`, `Packages/*`); rarely useful in
   ordinary application code.
3. **Recognized System / BCL attributes** — standard .NET attributes the translator honours.
4. **Vestigial markers** — attributes present in the libraries but *not* special-cased by the
   compiler (documented so their inertness is explicit).

### Status legend

| Status | Meaning |
| --- | --- |
| ✅ **Implemented** | The translator special-cases the attribute and emits accordingly. |
| ➖ **Equivalent by design** | Not read as an attribute, but Transpose's default behaviour already produces the effect the attribute requests, so it is a no-op. |
| ⏳ **Not yet implemented** | Recognized as a gap vs. H5; currently ignored. Rationale/impact noted. |

---

## 1. General-purpose attributes

These are the attributes an application (or any library) author will use.

### `[External]` — ✅ Implemented
Marks a type/member (or, via `[assembly: External]`, an entire assembly) as defined in native
JavaScript. No body is emitted; the member is named by its runtime binding (camelCase for the BCL,
native names for DOM/`Packages`), is excluded from reflection metadata and interface registration,
and a plain indexer becomes native bracket access.
*Handled in:* `TransposeNaming.HasExternalAttribute/IsExternal/IsExternalType`, consumed throughout
the emitter. Assembly-level form is synthesized from the csproj `<AssemblyAttribute>` items in
`ProjectResolver`.

### `[Name("jsName")]` — ✅ Implemented
Overrides the emitted JS name of a type or member (used verbatim, never overload-suffixed). A dotted
value (`[Name("tss.IC")]`) also fixes the mangled interface-slot prefix, drives indexer accessor
names, and overrides enum member string-names.
*Handled in:* `TransposeNaming.GetName`, `NameMangler.TypeFullName`, `MangledTypeName`, etc.

### `[Namespace(...)]` — ✅ Implemented
Overrides the emitted namespace of a type. `[Namespace(false)]` (or `[Namespace("")]`) **suppresses**
the namespace so the type binds to its bare entity name — this is how `Transpose.Core`'s primitive
bindings (`String`, `Number`, `Object`, `Boolean`, …) map onto the JS globals. `[Namespace("x.y")]`
**replaces** the C# namespace with a custom one. `[Namespace(true)]` is the default (no change).
*Handled in:* `TransposeNaming.NamespaceOverride`, applied in `Emitter.TypeRef` (references) and
`NameMangler.TypeFullName` (definitions).

### `[Template("...")]` — ✅ Implemented
A JS code template that replaces a member's call site entirely (`{this}`, `{0}`, `{arg}`
substitution). A templated member is inlined, excluded from overload numbering, keeps its raw name,
and does not thread type arguments.
*Handled in:* `TransposeNaming.GetTemplate`, consumed across `Emitter.Expressions*`.

### `[Script("line", …)]` — ✅ Implemented
Supplies a **raw JavaScript body** for a method/accessor/operator; the C# body (if any) is discarded.
Because the lines *are* the body, this lets an `extern` (body-less) member be emitted with
hand-written JS.
*Handled in:* `TransposeNaming.GetScriptBody`; consumed in `Emitter.EmitMethodBody`,
`EmitAccessorBody`, and admitted for emission by `IsEmittableMethod`.

### `[ObjectLiteral]` — ✅ Implemented
Emits a class/struct as a plain JS object: the type definition gets `$literal: true`, and `new`
emits an object literal seeded per `ObjectInitializationMode` (Ignore / Initializer-only /
DefaultValue-all).
*Handled in:* `Emitter.Types` (`$literal`), `Emitter.Expressions2` (construction + init mode).

### `[Enum(Emit.…)]` — ✅ Implemented
Selects the enum emit mode: `Value` (numeric), `Name`/`StringName` variants (string-backed, with
casing). String modes add `$utype: System.String`, quote member values, and change `default(enum)`
to the zero member's string.
*Handled in:* `TransposeNaming.EnumEmitMode/EnumStringName`, `Emitter.EmitEnum`.

### `[GlobalMethods]` — ✅ Implemented
A static class whose members are projected directly onto the JS global scope (`alert(...)` rather
than `Type.alert(...)`); the type counts as external for naming. Equivalent to an empty-prefix
`[Scope]`.
*Handled in:* `TransposeNaming.ScopePrefix`.

### `[Scope("prefix")]` — ✅ Implemented *(defined in `Transpose.Core`)*
Projects a type's static members and nested types onto an ambient JS binding (e.g. the DOM types
under `Transpose.Core.dom`): `dom.window.foo` emits as `window.foo`. Scoped types are treated as
external for naming and excluded from interface registration and reflection.
*Handled in:* `TransposeNaming.ScopePrefix/IsScopedType`, `Emitter.StaticMemberAccess`.

### `[Reflectable(...)]` — ✅ Implemented
Overrides the default reflection policy per type or member: `[Reflectable(false)]` suppresses the
member/type from the emitted `$m(...)` metadata; `[Reflectable(true)]` (or argument-less) forces it
in. (The advanced filter / accessibility ctor forms are treated as `true`.)
*Handled in:* `TransposeNaming.ReflectableOverride`, applied in `Emitter.IsReflectableType` and
`IsReflectableMember`.

### `[ExpandParams]` — ✅ Implemented
A `params` method so marked has its trailing array spread as individual arguments at the call site
(for native variadic JS/DOM functions).
*Handled in:* `Emitter.Expressions2` (`HasExpandParams`).

### `[IgnoreGeneric]` — ✅ Implemented
A generic method/type so marked does **not** thread its type arguments as leading runtime parameters
at the call site; also honoured in reflection.
*Handled in:* `Emitter.Members` (`ThreadsTypeArgs`), `Emitter.Reflection` (`IsIgnoreGeneric`).

### `[IgnoreCast]` — ➖ Equivalent by design
In H5 this erases the runtime type-check for casts to the annotated type. Transpose already erases
casts to **all** external (native-JS/DOM) types automatically (`Emitter.IsUncheckableExternalCast`,
the same skip-set H5's `CastBlock` uses for `IgnoreCast=false`), so the attribute's dominant
(type-level) intent is covered without reading it.
*Handled in:* `Emitter.EmitNumericConversion` / `IsUncheckableExternalCast`.

### `[Ready]` — ✅ Implemented
A static method so marked is registered via `Transpose.ready(Type.method, Type)` to run on
`DOMContentLoaded` (or immediately if already loaded).
*Handled in:* `Emitter.Types` (`EmitReadyRegistrations`).

### `[FileName]`, `[Output]`, `[OutputBy]` — ⏳ Partially via `tps.json`
Output file/folder layout controls. The layout surface is currently driven by `tps.json`
(`outputBy: ClassPath` is implemented; other modes emit a single bundle); the attribute forms are not
yet read. See "Known remaining work" in `CLAUDE.md`.

---

## 2. BCL / Core binding attributes

These attributes are used to author the runtime and the JS-binding libraries. Most are
`[NonScriptable]` themselves (they leave no runtime trace) and are meaningless in ordinary
application code.

### `[NonScriptable]` — ✅ Implemented
Excludes a type/member/accessor from emitted reflection metadata; also filters BCL codegen/marker
attributes (all `[NonScriptable]`) out of reflectable-attribute lists.
*Handled in:* `Emitter.Reflection` (type / member / accessor / attribute-filter).

### `[GlobalTarget("name")]` — ✅ Implemented
Marks a method as a typed window onto a JS global: the call is replaced by that global name; an empty
name compiles the call away to `void 0` (the "force-reference" marker pattern).
*Handled in:* `TransposeNaming.GlobalTargetName`, `Emitter.Expressions*`.

### `[AccessorsIndexer]` — ✅ Implemented
Forces an external type's indexer to route through `getItem`/`setItem` accessors instead of native
bracket access.
*Handled in:* `TransposeNaming.IsNativeIndexer`.

### `[Convention(Notation, …)]` — ✅ Implemented
Sets the JS name casing (None/lower/upper/camel/Pascal) for members of an external/BCL type, with
per-member-kind and priority/specificity resolution.
*Handled in:* `TransposeNaming.MemberConventionNotation/ResolveNotation`.

### `[Unbox(false)]` — ➖ Equivalent by design
In H5 this disables the default unboxing of `object` parameters on `[External]` methods. Transpose
does not emit parameter unboxing at all (primitives are already native JS values), so "don't unbox"
is already the default — the attribute is a no-op.

### `[ExternalInterface]` — ⏳ Not yet implemented (scanner-only)
Marks an interface implemented outside the Transpose type system (implementers provide no member
aliases). Currently recognized only by the `UnsupportedFeatureScanner` allow-list, with no dedicated
emit branch. Not used by the current libraries; interface naming is largely handled by the
source-vs-external interface distinction (`IsSourceInterface`).

### `[ToAwait]` — ⏳ Not yet implemented
On `Task.Wait()`-style extern methods: the call should be rewritten to `await task.wait()` and the
enclosing method made `async`. Deferred because it requires propagating async-ness up to the caller,
which touches method-signature emission. Used on `System.Threading.Tasks.Task`.

### `[Virtual]` — ⏳ Not yet implemented (behavioural difference)
In H5 a "virtual" type is referenced late-bound as `H5.getClass/getInterface("name")` rather than by
its direct global name (all `H5.Core` types are auto-virtual). Transpose emits direct type
references, which is sufficient in the cases exercised so far. Deferred pending confirmation that no
runtime scenario needs the late-bound form. Used heavily in `Transpose.Core`.

### `[Init(InitPosition)]` — ⏳ Not yet implemented
Emits a static method's body at a file position (Top/Before/After/Bottom of the class) as an
initializer. Deferred: the single library use (assembly-version init) is already covered by
Transpose's own `Transpose.assemblyVersion(...)` emission.

### `[Mixin("expr")]` — ⏳ Not yet implemented
Merges a type's members into a target JS object/prototype named by the expression (and treats the
type as global for naming). Not used by the current libraries.

### `[Constructor("...")]` — ⏳ Not yet implemented
Supplies a custom/inline JS constructor body; `[Constructor("{}")]` additionally makes the type
cast-transparent. Not used by the current libraries.

### `[InlineConst]` — ➖ Equivalent by design
In H5 a const is emitted as a named reference unless `[InlineConst]` opts into value inlining.
Transpose **always** inlines const values at use sites (`Emitter.Expressions` const-field path), so
the attribute's effect is already the default.

### `[Field]` — ⏳ Not yet implemented
Emits a property as a plain data field (no get/set accessors). Not used by the current libraries.

### `[Cast("...")]` — ⏳ Not yet implemented
A custom cast template applied to `(T)x`/`x as T`. Not used by the current libraries.

### `[Priority(n)]` — ⏳ Not yet implemented
Influences the emission order of types. Not used by the current libraries.

### `[PrivateProtected]` — ⏳ Not yet implemented
A compiler-synthesized marker recording `private protected` accessibility for reflection metadata.
Metadata-only; not used by the current libraries.

### `[Immutable]` — ⏳ Not yet implemented
Marks a type immutable (affects value-type copy semantics). Not used by the current libraries.

### `[Optional]` — ⏳ Not yet implemented (TypeScript-only)
Adds the TypeScript `?` optional modifier to a member in the generated `.d.ts`. Transpose does not
currently emit TypeScript definitions, so this has no effect.

### `[Rules(...)]` — ⏳ Not yet implemented
Overrides code-generation rules (lambda / boxing / array-index / integer / anonymous-type handling).
Not used by the current libraries.

### `[Module]`, `[ModuleDependency]` — ⏳ Not yet implemented
Emit the type/assembly under a JS module system (AMD / CommonJS / ES6 / UMD). Part of the broader
module-format work listed under "Known remaining work" in `CLAUDE.md`.

### `[Adapter]` — ✅ Implemented (via subclasses)
Abstract base for "adapter" attributes; the concrete `[Ready]` is handled directly. No standalone
handling is required.

### `[Where(...)]`, `[Allow(...)]` — ➖ No codegen effect (matches H5)
Flexible generic-constraint / permission markers. Neither H5 nor Transpose special-cases these for
code generation (they are validation/documentation only), so their absence is not a gap.

---

## 3. Recognized System / BCL attributes

### `System.FlagsAttribute` — ✅ Implemented
An enum so marked emits `$flags: true` in its `Transpose.define` config (bitwise/ToString runtime
behaviour). *`Emitter.Types`.*

### `System.AttributeUsageAttribute` — ✅ Implemented
For an attribute type's reflection metadata, reads `Inherited`/`AllowMultiple` and emits the
`ni`/`am` flags. *`Emitter.Reflection`.*

### `System.Diagnostics.ConditionalAttribute` — ✅ Implemented
A call to a `[Conditional("SYM")]` method is **removed entirely** (arguments not evaluated) when
none of its symbols are defined in the compilation — matching C# semantics. *`Emitter.Statements` /
`IsRemovedConditionalCall`.*

### `System.Runtime.CompilerServices.ModuleInitializerAttribute` — ✅ Implemented
Static methods so marked are collected and emitted as module-initializer calls at startup.
*`Emitter.Members` (`ModuleInitializerMethods`).*

### `DllImport` / `LibraryImport` / `InlineArray` — ✅ Implemented (as diagnostics)
`System.Runtime.InteropServices.DllImportAttribute`, `LibraryImportAttribute`, and
`System.Runtime.CompilerServices.InlineArrayAttribute` are reported as **unsupported features**
(P/Invoke and inline arrays have no browser equivalent). *`UnsupportedFeatureScanner`.*

### `InternalsVisibleTo` — ✅ Implemented (skipped)
Skipped when synthesizing `[assembly: …]` attributes from csproj items (no meaning in transpiled
JS). *`ProjectResolver`.*

### Stripped / ignored (no special handling needed)
`MethodImpl`, `DebuggerHidden`, `DebuggerStepThrough`, `CompilerGenerated`, the `Nullable*` family,
`AsyncStateMachine`, `Extension` (handled via Roslyn symbol APIs, not attribute lookups), etc. — H5
strips these; Transpose simply never emits them.

---

## 4. Vestigial markers (`Transpose.Core`)

The following attributes appear on `Transpose.Core` types (historical Bridge.NET / decompiler
artifacts) but are special-cased by **neither** H5 nor Transpose. They are inert with respect to code
generation and are listed only for completeness:

`[CombinedClass]`, `[StaticInterface]`, `[ClassInterface]`, `[FormerInterface]`,
`[InterfaceWrapper]`, `[GenericDefault]`, `[ExportedAs]`, `[Generated]`.

---

## Quick reference

| Attribute | Group | Status |
| --- | --- | --- |
| `External` | General | ✅ |
| `Name` | General | ✅ |
| `Namespace` | General | ✅ |
| `Template` | General | ✅ |
| `Script` | General | ✅ |
| `ObjectLiteral` | General | ✅ |
| `Enum` | General | ✅ |
| `GlobalMethods` | General | ✅ |
| `Scope` | General | ✅ |
| `Reflectable` | General | ✅ |
| `ExpandParams` | General | ✅ |
| `IgnoreGeneric` | General | ✅ |
| `IgnoreCast` | General | ➖ |
| `Ready` | General | ✅ |
| `FileName` / `Output` / `OutputBy` | General | ⏳ (tps.json) |
| `NonScriptable` | BCL/Core | ✅ |
| `GlobalTarget` | BCL/Core | ✅ |
| `AccessorsIndexer` | BCL/Core | ✅ |
| `Convention` | BCL/Core | ✅ |
| `Unbox` | BCL/Core | ➖ |
| `InlineConst` | BCL/Core | ➖ |
| `ExternalInterface` | BCL/Core | ⏳ |
| `ToAwait` | BCL/Core | ⏳ |
| `Virtual` | BCL/Core | ⏳ |
| `Init` | BCL/Core | ⏳ |
| `Mixin` | BCL/Core | ⏳ |
| `Constructor` | BCL/Core | ⏳ |
| `Field` | BCL/Core | ⏳ |
| `Cast` | BCL/Core | ⏳ |
| `Priority` | BCL/Core | ⏳ |
| `PrivateProtected` | BCL/Core | ⏳ |
| `Immutable` | BCL/Core | ⏳ |
| `Optional` | BCL/Core | ⏳ (TS-only) |
| `Rules` | BCL/Core | ⏳ |
| `Module` / `ModuleDependency` | BCL/Core | ⏳ |
| `Adapter` | BCL/Core | ✅ (subclasses) |
| `Where` / `Allow` | BCL/Core | ➖ (no codegen) |
| `System.Flags` | System | ✅ |
| `System.AttributeUsage` | System | ✅ |
| `System.Diagnostics.Conditional` | System | ✅ |
| `ModuleInitializer` | System | ✅ |
| `DllImport` / `LibraryImport` / `InlineArray` | System | ✅ (diagnostic) |
| Core markers (`CombinedClass`, …) | Vestigial | ➖ (inert) |
