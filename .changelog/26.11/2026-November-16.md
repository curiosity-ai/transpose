# Transpose — week of 2026-11-16

_Covers commits to `master` from 2026-11-16 (Mon) to 2026-11-22 (Sun). The release version and the current packages are filled in by the weekly changelog run._

## Language and Compiler

- **Fix: `[Convention]` on a compiled type applies inside its own project.** A
  property, field or event of a non-`[External]` type with `[Convention]` (on the
  type or on the member) now gets the convention's name in the project that
  declares it. Before, that project kept the C# name while every project that
  referenced it applied the convention, so one property had two JavaScript names.
  Example: in a library, `[ObjectLiteral, Convention(Notation.CamelCase)] class
  LayerVisibility { bool? Skin }` was written as `Skin` by the library and as
  `skin` by the app, and the object carried both. Methods are unchanged: a
  compiled type's methods keep their C# name in both places.
- **Fix: a convention never renames a member onto another member's name.** On a
  compiled type, `[Convention(Notation.CamelCase)] class C { int count; int Count
  => count; }` keeps the property as `Count`, in the declaring project and in
  every project that references it. Before, a referencing project read
  `c.count`, which is the field, not the property.

### Compatibility

- **Rebuild a package that uses `[Convention]` with this compiler.** A package
  built by an older compiler contains JavaScript that uses the C# names for its
  own `[Convention]` members. A project built with this compiler uses the
  convention names for them, as projects did before. So nothing changes for the
  consumer side, but the package's own code and the consumer's code agree only
  when the package is rebuilt with this compiler. Until then, the mismatch that
  this fix removes stays in that package.
- **Projects that read a renamed member through JavaScript.** If your code,
  `[Template]` or `Script` text reads such a member by its C# name (for example
  `obj.Skin` in hand-written JavaScript), change it to the convention name
  (`obj.skin`).
- The base library (`Transpose.BCL`) output is unchanged.
