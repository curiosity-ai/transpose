# Compiler Configuration

The H5 compiler is configured primarily through a `h5.json` file located in your project root.

## Basic `h5.json` Structure

```json
{
  "output": "dist",
  "module": {
    "type": "ES6"
  },
  "sourceMap": true
}
```

## Common Options

- **`output`**: The directory where generated JavaScript files will be placed.
- **`fileName`**: The name of the generated JavaScript file (defaults to the assembly name).
- **`module`**: Configuration for JavaScript modules.
  - `type`: `AMD`, `CommonJS`, `UMD`, `ES6`, or `None`.
- **`sourceMap`**: Boolean indicating whether to generate source maps for debugging C# in the browser.
- **`define`**: A list of preprocessor symbols to define during compilation.

## Advanced Configuration

### Resource Management

H5 can manage and bundle external assets (JS, CSS, fonts) using the `resources` section. These resources are automatically injected into the generated `index.html`.

```json
{
  "resources": [
    {
      "name": "app-dependencies.js",
      "files": [
        "assets/js/jquery.min.js",
        "assets/js/bootstrap.bundle.min.js"
      ],
      "output": "js"
    },
    {
      "name": "styles.css",
      "files": [ "assets/css/*.css" ],
      "output": "css"
    },
    {
      "name": "lazy-module.js",
      "files": [ "assets/js/lazy-module.js" ],
      "load": false
    }
  ]
}
```

- **`name`**: The name of the bundled file.
- **`files`**: A list of files or wildcards to include.
- **`output`**: The sub-directory in the output folder where the bundle will be placed.
- **`load`**: `true` (default) injects the resource into `index.html` — a `<script>` for
  JavaScript, a `<link rel="stylesheet">` for CSS. `false` still copies the resource into
  the output folder (and still embeds it when the project is packaged) but leaves loading
  it to your code: a module you fetch on demand, a theme you swap in at runtime, an asset
  another file references. This is the declarative form of h5's `.dontload` name suffix
  (`"name": "lazy-module.js.dontload"`), which is still honoured; either spelling alone
  suppresses the injection.

The flag survives packaging. When a library declares `"load": false`, that is recorded in
the resource manifest embedded in its DLL, so a project *referencing* the library also
extracts the file into its site without referencing it from its own `index.html`.

### References You Load Yourself

A referenced library is scripted from `index.html` in dependency order, before your own bundle.
That is right for a library the application needs at start-up and wasteful for one a single
screen needs — a chart or map binding can be several megabytes on every page load. List such a
reference in `dontLoadReferences` and the compiler extracts everything it contributes into the
site as usual but references none of it from `index.html`:

```jsonc
{
  "dontLoadReferences": [ "Tesserae.Plotly" ]
}
```

- Each entry is matched against a referenced assembly's **name** (`Tesserae.Plotly`, not a path),
  case-insensitively, with `*`/`?` wildcards; a `.dll` suffix is accepted and ignored.
- It applies to everything that assembly ships — its compiled bundle, its authored scripts, its
  stylesheets — for the same reason the resource `load` flag does: the point is that the page
  loads none of it until the application asks.
- An entry that matches no referenced assembly is reported as a warning (`TPS0106`), so a typo or
  a dependency you have since dropped does not silently do nothing.

Loading the library is then your code's job, the first time it needs it:

```csharp
await Transpose.Require.RequireAsync("plotly.js", "Tesserae.Plotly.js");
var chart = new Tesserae.Plotly.PlotlyChart().Title("deferred chart");
```

`Require` falls back between the `.js` and `.min.js` spellings of the same file, so one call
works in a Debug site (which carries the readable bundle) and in a Release site (the minified
one) alike. Reaching the deferred code *before* it is loaded fails at run time, exactly as it
would for any library the page never loaded.

This is the consumer-side counterpart of `loadCompiledOutput: false`, which a *library* sets to
keep its own bundle out of index.html. Use that when the library knows it is loaded on demand,
and `dontLoadReferences` when the application decides it for a library that does not.

### Output Folder Cleanup

Transpose keeps the output folder free of stale artifacts with `cleanOutputFolder`
(enabled by default). After a successful build it compares the output folder with
exactly the files the build produced and deletes only the leftovers from a previous
build — a renamed bundle, a `.min` variant no longer emitted, a removed resource, a
stale `index.min.html` — then removes any directory it empties. Files the current
build wrote are never touched, and a build that fails leaves the previous output
intact (the cleanup runs only after the site is assembled).

```jsonc
{
  "cleanOutputFolder": true,                     // default; set false to keep stale files
  "cleanOutputFolderExclude": [ "favicon.ico", "vendor/*" ]
}
```

- **`cleanOutputFolder`**: `true` (default) prunes stale files; `false` disables pruning.
- **`cleanOutputFolderExclude`**: glob patterns (`*`/`?` wildcards, matched against each
  file's output-relative path and its name) that are never pruned even when stale — the
  escape hatch for hand-placed files that live alongside the generated site.

This is the successor to h5's `cleanOutputFolderBeforeBuild`, which deleted by glob
*before* compiling; the diff-based approach needs no pattern and cannot remove a file the
current build produced.

### HTML Injection

The H5 compiler can generate a basic `index.html` file and inject references to the generated script and the defined resources. This is enabled by default unless `html` configuration is explicitly customized or disabled.

### Configuration Merging

You can create environment-specific configuration files:
- `h5.Debug.json`
- `h5.Release.json`

When building in a specific configuration (e.g., `Debug`), the compiler will first load `h5.json` and then merge it with `h5.Debug.json`. Values in the configuration-specific file will overwrite those in the base `h5.json`.

## MSBuild Properties

You can also control some compiler behaviors via MSBuild properties in your `.csproj`:

- `<UpdateH5>false</UpdateH5>`: Disables automatic updates of the H5 compiler tool.
- `<H5NoCore>true</H5NoCore>`: Prevents the compiler from automatically including the core library.
