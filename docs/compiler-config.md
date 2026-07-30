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
