---
name: write-changelog
description: >-
  Generate a single, global, user-focused weekly changelog for the Transpose
  C#-to-JavaScript compiler (the `Transpose.*` NuGet packages: the compiler,
  the base library, the Web bindings, the MSBuild SDK). Use whenever asked to
  "write a changelog", "update the changelog", "draft release notes",
  "summarize this week's changes", or to produce the weekly changelog.
  Produces ONE global changelog file per week organised by category (Features,
  Language and Compiler, Runtime and Base Library, Web and Package Bindings,
  Build and Tooling, Performance), lists the current published NuGet package
  versions, writes the file under `.changelog/<yy.M>/`, and refreshes
  `.changelog/CurrentVersion` so a release can be cut afterwards.
---

# Writing the Transpose weekly changelog

## Goal

Produce one technical but **user-focused** changelog for an external audience
that cannot see the source. Readers are the developers who *use* Transpose to
build browser front-ends in C#. They care about what compiles, what the emitted
JavaScript does, what the build does, and what the public surface is (the
packages, the `tps` CLI, `tps.json`, the codegen attributes). They do not care
about internal mechanics.

There is a **single global changelog for the whole Transpose product** — not one
file per NuGet package. Inside that one file the changes are grouped into
product **categories** (see §3), so a reader sees "what changed in the language
support / the runtime / the bindings / the build" in one place rather than
chasing per-package files.

"Technical but not internal": you may name the public-facing surface a user
touches — a C# language feature, a BCL type or method, a codegen attribute, a
`tps.json` setting, a `tps` flag, a diagnostic code, an MSBuild property, a
package id. Do **not** reveal internal class names, file paths, private method
names, emitter method names, or how the change is wired up underneath.

The one deliberate exception: **the emitted JavaScript is public**. It is what
runs in the user's browser, and a user debugging their app reads it. So naming
a runtime global (`Transpose.cast`, `TransposeR.fromPromise`, `Transpose.define`)
or describing the shape of the emitted code is fine when that is what changed.
Naming the emitter source file that produced it is not.

## Cadence

The changelog runs **weekly** and covers the **past 7 days**. Pick `today` as
the generation date and `today − 7 days` as the start of the window. Weeks run
**Monday to Sunday**.

Transpose became a distinct product on **2026-07-15** (the restructure and
rebrand from h5), and its first packages were published on **2026-07-16**. There
is no changelog before the week of **2026-07-13** — the history behind that
point belongs to h5, not Transpose. Do not backfill into it.

---

## 1. Collect commits from `master`

The changelog is built from the **main branch** of this repository (`master`).
List **all** commits in the window, then work out what each one means and which
category it belongs to:

1. List commits in the window:
   ```bash
   git log master --since="<7d ago>" --pretty=format:'%H %s'
   ```
   In a cloud container the clone is usually **shallow** (only the last few days
   of history). If your window reaches further back, unshallow first:
   ```bash
   git rev-parse --is-shallow-repository        # "true" means shallow
   git fetch --unshallow origin master          # one-time, then git log works for any range
   ```
   The GitHub MCP alternative is `mcp__github__list_commits`
   (`owner=curiosity-ai`, `repo=transpose`, `sha=master`,
   `since=<ISO-8601 7 days ago>`).

2. For each commit, understand what changed by looking at progressively more
   ground truth until you are confident:
   - the **commit message** first;
   - the **associated PR** body when the commit came from a merged PR (find it
     via the `(#NNN)` in the squash-commit subject, or
     `mcp__github__list_pull_requests`) — prefer the PR body **when** it
     explains user-facing impact;
   - the **code diff** itself (`git show <sha>`) whenever the message or PR is
     vague, names a file without describing the change, or only describes
     implementation. Commit messages routinely under-state user impact; the diff
     is the ground truth.

3. **Skip without reading the diff** only when the message unambiguously
   indicates no user-observable change: merge commits, `update packages`,
   `update gitignore`, formatting passes, pipeline YAML tweaks, and similar pure
   housekeeping.

4. **A new or changed test is a strong signal, not an entry.** This repo's tests
   are differential (a snippet is run as translated JS on Node *and* natively on
   .NET, and the outputs are diffed), so a new test almost always documents a
   real behaviour change or fix. Read the test to learn *what construct* now
   behaves correctly, then write the entry about the construct — never about the
   test.

5. **Determine the category** of every real change from the paths it touches
   (see §3) so it lands in the right section.

For a window with many commits, parallelise with **sub-agents**:

- Spawn one `general-purpose` sub-agent (via the `Agent` tool) per natural group
  of commits (e.g. one per week for a backfill, or split a single huge week by
  area). Hand each agent the **exact commit hashes** for its group and have it
  run `git show --stat <hash>` / `git show <hash>` against those hashes,
  classify per this skill, and **write its result to a file** (e.g.
  `/tmp/clog/out/week-NN.body.md`) so you can assemble afterwards rather than
  passing large diffs back through chat.
- Always tell the agent to focus on **that specific historical set of
  commits, not the current repo HEAD** (it must analyse what the commits changed
  at the time, not describe today's code). This is the single most important
  instruction for accurate historical changelogs.
- Have the agent return only a short summary plus the path it wrote; you keep
  the conclusions, not the file dumps.
- Batch launches (roughly 8-12 agents per message) and let the rest run in the
  background; you are notified as each completes.

---

## 2. Group, consolidate, filter

### Relevance test

Include an entry only if a user of the compiler would:

- observe a change in what compiles or what the emitted JavaScript does, or
- gain a new capability (a language feature, a BCL API, a `tps.json` setting, a
  CLI flag), or
- need to take an action (upgrade, rename, reconfigure), or
- notice a materially faster or leaner build.

If a change only explains **how** something works internally, omit it or fold it
into a higher-level entry. If in doubt, leave it out.

Always exclude: pure refactors, test-only changes with no behaviour change,
CI/pipeline tweaks, internal renames, dependency bumps with no behaviour change,
formatting passes, and documentation-only commits (`CLAUDE.md`, `TODO.*.md`,
`AGENTS.md`).

!!! One carve-out for docs
A **user-facing** documentation deliverable *is* an entry: the migration guide,
the README's getting-started, the published docs site. `MIGRATION.md` landing is
worth a bullet; a note added to `TODO.optimization.md` is not.

### Consolidation rules

- Merge related changes that touch the same construct, feature area, or surface
  into one entry. The reader cares about the area, not the number of commits.
- When several small fixes touch one construct (a run of struct-copy fixes, a
  run of string-formatting fixes), write **one** entry naming the construct —
  "Struct assignment now copies nested struct fields" — rather than itemising
  each commit.
- If a feature was added and iterated on within the same window, describe the
  end state, not the iteration path. A feature that landed and was then fixed
  three times is one Feature entry, not one Feature plus three Fixes.

### Implementation-detail filter

These are mechanics, not impact, and should NOT appear in entries:

- emitter, translator or compiler-internal type and method names
- source file paths and project folder names
- internal variable, constant and field names
- Roslyn API names and syntax-node type names
- specific dependency version numbers (except where a user must pin one)
- test class and test method names
- pipeline, branch and repository-structure names

**"Must act on it" carve-out.** Mention a concrete name **only** when the user
must type, set, configure, reference or read it themselves: a package id, a
`tps.json` key, a `tps` flag, an MSBuild property, a diagnostic code, a codegen
attribute, a C# or BCL API, or a runtime global that appears in the emitted
JavaScript they debug. Keep these minimal but do not omit them — for this
product they are most of the value.

---

## 3. Categories

The single global changelog is organised into exactly these six product
**categories**, in this order. Attribute each change to a category from the
source paths it touches (a single commit may legitimately contribute to more
than one category when each surface is genuinely impacted):

| Category | What it covers | Source paths (attribution) | Published as |
|---|---|---|---|
| **Features** | Significant, headline capabilities whose value spans more than one surface — a new mode of operation the user adopts as a whole (watch mode, incremental builds, compiler-as-a-library). Describe the end-to-end capability once here instead of splitting it | spans `Transpose/**` + `BCL/**`, or a whole new component | the relevant packages together |
| **Language and Compiler** | What compiles and what the emitted JavaScript does: C# language features, correctness of emitted code, codegen attributes, diagnostics for unsupported constructs | `Transpose/Transpose.Translator/**` (the emitter, naming, the feature scanner) | NuGet `Transpose.Compiler` |
| **Runtime and Base Library** | The browser-side .NET surface: `System.*` APIs, collections, LINQ, text and formatting, dates, tasks, reflection metadata, and the `tps.js` runtime primitives | `BCL/Transpose.BCL/**` (both the C# definitions and `Resources/*.js`) | NuGet `Transpose.BCL` |
| **Web and Package Bindings** | The binding libraries: DOM and ES5/ES6, JSON, HTTP, and the other JavaScript-library bindings | `BCL/Transpose.Core/**`, `Packages/**` | NuGet `Transpose.Core`, `Transpose.Newtonsoft.Json`, `Transpose.HttpClient`, … |
| **Build and Tooling** | Everything about running a build: the `tps` CLI and its flags, the MSBuild SDK, the `tps.json` surface, reference and project resolution, the generated site and `index.html`, resources, diagnostics formatting, and the compiler-as-a-library API | `Transpose/Transpose.Compiler/**`, `Transpose/Transpose.Compiler.Core/**`, `Transpose/Transpose.Compiler.Library/**`, `Transpose/Transpose.Build.Target/**`, `Transpose/Transpose.Template/**` | NuGet `Transpose.Compiler`, `Transpose.Compiler.Library`, `Transpose.Build.Target`, `Transpose.Template` |
| **Performance** | Build wall time, allocations and memory — where the user-visible win is "the build is faster or leaner". Only when it is measurable and worth saying; a micro-optimisation is not an entry | anywhere, when the change is a measured build-cost win | NuGet `Transpose.Compiler` |

**Language and Compiler vs. Runtime and Base Library.** Split by *where the
behaviour lives*. If the fix changed the **JavaScript the compiler writes for
your code**, it is Language and Compiler. If it changed **an API your code
calls**, it is Runtime and Base Library. A `string.Format` bug is Runtime; a
string-interpolation emit bug is Compiler. When one change genuinely required
both halves (a new BCL method plus the emit that targets it), describe it once
under whichever half the user thinks of it as.

**When to use Features vs. the surface categories.** Put a change in
**Features** when it is a *single significant capability* the user adopts as a
whole and whose value spans surfaces, so describing it once is clearer than
splitting it. Keep changes scoped to one surface in their own category. Do not
double-list: if a capability is described under **Features**, do not also
itemise its halves elsewhere (you may still list genuinely separate, smaller
changes to those surfaces normally). Reserve **Features** for the headline
items.

Omit any category heading that has no entries this week.

Within each category, write bullet entries. Lead each bullet with a short
**bold title**, optionally prefixed with the change type when it helps the
reader scan (`**Feature**`, `**Improvement**`, `**Fix**`), followed by one to
three sentences of user-facing impact. Example:

```markdown
## Language and Compiler

- **Fix: nested struct fields are copied on assignment.** Assigning a struct
  that itself holds struct-typed fields now clones those fields too, so
  mutating the copy no longer writes through to the original.
- **Unsupported constructs are reported as errors.** P/Invoke declarations and
  inline arrays now fail the build with a diagnostic instead of emitting code
  that cannot run in a browser.
```

---

## 4. Fetch the current published versions

Every changelog file opens with a **Current packages** block listing the live
published versions, so the reader knows exactly which artifacts the changelog
corresponds to. Fetch these at generation time (skip a value and write
`unknown` rather than fabricating one if a fetch fails).

List the **same consistent set of packages on every changelog file** so each one
reads as a complete release manifest. When a package had no new release in the
period, **carry forward** its last-known version (the most recent one published
on or before the end of the window) rather than omitting the line. Never leave a
package out just because it was quiet.

The one case where a line *is* omitted: a package whose **first** release is
later than the window. It has no version to carry forward and did not exist, so
it simply does not appear in the earlier files. This is why the earliest
changelog lists only `Transpose.Compiler` and `Transpose.Build.Target` — the rest
were first published the following week.

**Link the package name, never paste a raw URL.** Each entry wraps the bold
component name in a Markdown link
(`- **[Transpose.Compiler](https://www.nuget.org/packages/Transpose.Compiler)** v<version>`),
not a bare URL after the version.

### The NuGet endpoints

!!! The flat-container index is not available for these packages
`https://api.nuget.org/v3-flat-container/<id>/index.json` returns **404** for
the `Transpose.*` ids. Use the **registration** index instead, which also gives
you publish dates (needed to place a version in a week):

```
https://api.nuget.org/v3/registration5-semver1/<lowercase-id>/index.json
```

For packages with many versions the index lists `items` as *pages* (each with an
`@id`); fetch each page and read `items[].catalogEntry.version` and
`.published`. A `published` year of `1900` means unlisted — skip it.

Two more traps, both worth handling in the fetch script:

- **The response carries a UTF-8 BOM.** Decode with `utf-8-sig`, or
  `json.loads` fails with "Unexpected UTF-8 BOM".
- `api.nuget.org` and `www.nuget.org` are reachable from the cloud container;
  `azuresearch-*.nuget.org` is not.

A short Python helper over the registration API is the reliable way to do this;
it is cheap and removes every transcription error.

### The package set

| Component | NuGet page |
|---|---|
| `Transpose.Compiler` | https://www.nuget.org/packages/Transpose.Compiler |
| `Transpose.BCL` | https://www.nuget.org/packages/Transpose.BCL |
| `Transpose.Core` | https://www.nuget.org/packages/Transpose.Core |
| `Transpose.Build.Target` | https://www.nuget.org/packages/Transpose.Build.Target |
| `Transpose.Newtonsoft.Json` | https://www.nuget.org/packages/Transpose.Newtonsoft.Json |
| `Transpose.HttpClient` | https://www.nuget.org/packages/Transpose.HttpClient |
| `Transpose.Compiler.Library` | https://www.nuget.org/packages/Transpose.Compiler.Library |

Package facts worth knowing (confirmed from the registry):

- **The base library's package id is `Transpose.BCL`**, but its assembly is
  `Transpose` (so the DLL is `Transpose.dll` and the runtime global in emitted
  JavaScript is `Transpose`). Every other package id matches its assembly name.
- **`Transpose.Compiler` and `Transpose.Compiler.Library` ship at the same
  version**, always. They share the unpublished translator and compiler core, so
  one pipeline packs and pushes both in a single run. If the two versions differ
  in what you fetched, re-check — one of them is mid-publish.
- **The packages are versioned in CalVer `yy.M.<buildId>` and released
  independently**, so their numbers do not move in lockstep and a package can be
  several builds behind the compiler without anything being wrong.
- **`Transpose.Template`, `Transpose.WebGL2`, `Transpose.Howler`,
  `Transpose.P2` and `Transpose.Placeholders` have build pipelines but are
  **not yet published** to nuget.org.** Do not list them in the manifest and do
  not describe them as installable. Re-check with a `curl -o /dev/null -w
  '%{http_code}' https://www.nuget.org/packages/<Id>` before adding one — when
  the first one appears, add it to the table above.

---

## 5. Tone and formatting

- Be concise. Each entry is a **short bold title** plus one to three sentences.
  Lead with what the user can now do, or what stopped being a problem.
- Write in clear, lightly-technical language. The reader is a C# developer but
  is not reading your code.
- Name the construct. "Nested struct fields are copied on assignment" is useful;
  "value semantics improved" is not. This is a compiler — specificity *is* the
  user value.
- Do not reference commits, commit hashes, PR numbers, branch names, or internal
  tickets.
- Do not use emojis.
- Do not use em-dashes (`—`). Use commas, parentheses, or split the sentence.
- Explain the impact, not the mechanics.

---

## 6. Output: one global file per week, under the `<yy.M>` folder

Compute the calendar version for the current month in **`yy.M`** format
(two-digit year, dot, **non-padded** month), matching the CalVer scheme the CI
release pipelines use (`yy.M.<buildId>`):

```bash
date -d "<gen-date>" +%y.%-m       # GNU date  → e.g. 26.7
printf '%d.%d' "$(date -j -f %Y-%m-%d "<gen-date>" +%y)" \
               "$(date -j -f %Y-%m-%d "<gen-date>" +%m)"   # macOS BSD date
```

(Use a non-padded month so October–December read `26.10`–`26.12` while July
reads `26.7`, not `26.07`.)

Each weekly changelog corresponds to a **release**, and is named by that
release's full revision: **`yy.M.<compilerBuild>`**, where `<compilerBuild>` is
the build number of the newest **`Transpose.Compiler`** version published in the
week. The compiler is the product's anchor — every release goes through its
pipeline, and its build number is the highest-cadence one — so it plays the role
the Docker build number plays for Curiosity Workspace. Write the file to:

```
.changelog/<yy.M>/<yy.M.compilerBuild>.md
```

Example: a week whose newest `Transpose.Compiler` release is `26.7.3204` is
written to `.changelog/26.7/26.7.3204.md`. Naming by revision (not by generation
date) means the filename is the exact version the release carries, weeks sort by
build number within the month, and there is never a name collision.

If a week genuinely had no `Transpose.Compiler` release, fall back to the week's
Monday date (`.changelog/<yy.M>/YYYY-MMMM-DD.md`, full English month name).

### File template

```markdown
# Transpose <yy.M.compilerBuild> — week of YYYY-MM-DD

_Release version <yy.M.compilerBuild>. Covers commits to `master` from YYYY-MM-DD (Mon) to YYYY-MM-DD (Sun)._

## Current packages

- **[Transpose.Compiler](https://www.nuget.org/packages/Transpose.Compiler)** v<version>
- **[Transpose.Compiler.Library](https://www.nuget.org/packages/Transpose.Compiler.Library)** v<version>
- **[Transpose.BCL](https://www.nuget.org/packages/Transpose.BCL)** v<version>
- **[Transpose.Core](https://www.nuget.org/packages/Transpose.Core)** v<version>
- **[Transpose.Build.Target](https://www.nuget.org/packages/Transpose.Build.Target)** v<version>
- **[Transpose.Newtonsoft.Json](https://www.nuget.org/packages/Transpose.Newtonsoft.Json)** v<version>
- **[Transpose.HttpClient](https://www.nuget.org/packages/Transpose.HttpClient)** v<version>

## Features

- **Short title.** One to three sentences describing a significant capability
  the user adopts as a whole.

## Language and Compiler

- **Short title.** One to three sentences.

## Runtime and Base Library

- **Short title.** One to three sentences.

## Web and Package Bindings

- **Short title.** One to three sentences.

## Build and Tooling

- **Short title.** One to three sentences.

## Performance

- **Short title.** One to three sentences, with the measured win where there is
  one.
```

Omit any of the six category headings that has no entries. If the whole week
produced no user-visible changes (only housekeeping), say so in the summary and
do not create an empty file.

---

## 7. Refresh `CurrentVersion` (so a release can be cut)

The changelog folder carries a single `CurrentVersion` file holding the current
calendar version in **`yy.M` format only** (no build number, no `v` prefix),
matching the CI release CalVer:

```
.changelog/CurrentVersion
```

Its entire content is the current month's calendar version, e.g.:

```
26.7
```

Write / overwrite this file as the **last** step of generating the weekly
changelog, so it always matches the `<yy.M>` folder you just wrote into.

Cutting the actual release is a separate step: each package has its own pipeline
under `.devops/`, and versions are stamped by CI as `yy.M.<buildId>`.
`CurrentVersion` is the hand-off — it records the calendar version the next
release should carry.

---

## 8. Mirror the entry into the public documentation site (if available)

The public docs site (`curiosity-ai/documentation`) republishes this changelog at
**docs.curiosity.ai/transpose/changelog** using Neko's folder-based changelog
feature, sourced from `documentation/transpose/changelog/`. **Whenever a checkout
of that repo is available, mirror each weekly file you just wrote into it** so
the public changelog stays in sync. In a cloud session the repo is added to the
session scope (check with `mcp__claude-code-remote__list_repos` / `add_repo` if
it is not yet present); locally, look for a sibling `documentation/` clone.

Neko aggregates **only the `.md` files that sit directly inside** the changelog
folder — it does **not** recurse into per-month subfolders — so the docs copy is
**flat**: one file per release named after its version,
`transpose/changelog/<yy.M.compilerBuild>.md` (no `<yy.M>/` subfolder). The file
name is parsed as the version badge and sorted newest-first automatically.

The docs copy uses a **different markup** from the `.changelog/` source:
frontmatter, a `links` block for the package manifest, `# :icon-…:` H1 category
headings, and one `::: change` container per entry. The docs repo documents all
three in its own skills (`.claude/skills/changelog`, `.claude/skills/change`,
`.claude/skills/links`) — read those for the full attribute reference. Always
open the newest existing file in `documentation/transpose/changelog/` and match
it verbatim; the shape is:

````md
---
date: 27 Jul 2026
link: https://www.nuget.org/packages/Transpose.Compiler/26.7.3204
---

```links title="Current packages" icon="box"
Transpose.Compiler | https://www.nuget.org/packages/Transpose.Compiler | v26.7.3204
Transpose.BCL | https://www.nuget.org/packages/Transpose.BCL | v26.7.3120
Transpose.Core | https://www.nuget.org/packages/Transpose.Core | v26.7.3122
Transpose.Build.Target | https://www.nuget.org/packages/Transpose.Build.Target | v26.7.3049
Transpose.Newtonsoft.Json | https://www.nuget.org/packages/Transpose.Newtonsoft.Json | v26.7.3202
```

# :icon-sparkles: Features

::: change {badge="Feature" title="Short title, no trailing period"}
One to three sentences of user-facing impact.
:::
````

Transform each file as you copy it:

1. **Drop the `# Transpose <ver> — week of <date>` H1.** The version badge is
   generated from the file name, so the H1 is redundant (and it uses an em-dash,
   which the docs style guide forbids).
2. **Drop the `_Release version … Covers commits to `master` …_` line.** It names
   the branch and "commits" — internal mechanics that must not appear in a public
   changelog.
3. **Add frontmatter** carrying the week-of date (`D MMM YYYY`, the Monday of the
   week) and a `link` to the compiler release for the week.
4. **Turn the `## Current packages` bullet list into a `links` block.** The docs
   manifest is deliberately **shorter** than the source's: five lines, in this
   order — `Transpose.Compiler`, `Transpose.BCL`, `Transpose.Core`,
   `Transpose.Build.Target`, `Transpose.Newtonsoft.Json`. Each line is
   `Name | url | version`. (`Transpose.Compiler.Library` shares the compiler's
   version and `Transpose.HttpClient` is a minor binding, so both are left out.)
5. **Category headings become `# :icon-…:` H1s**, in the same order as the
   source, with these fixed icons:
   `# :icon-sparkles: Features`, `# :icon-code-simple: Language and Compiler`,
   `# :icon-cube: Runtime and Base Library`, `# :icon-browser: Web and Package Bindings`,
   `# :icon-terminal: Build and Tooling`, `# :icon-rocket: Performance`.
6. **Every bullet becomes a `::: change` container**, with the bold title moved
   into `title=` (no trailing period, no `Fix:` / `Improvement:` / `Feature:`
   prefix) and the sentences as the body. The `badge=` value is the change type,
   from exactly this vocabulary: `badge="Feature"` (a brand-new capability, API,
   setting, flag or option), `badge="Improved"` (refines existing behaviour) and
   `badge="Fixed"` (corrects wrong or broken behaviour). Tie-breakers: something
   you could not do at all before is a Feature; making an existing thing better
   is Improved; correcting broken behaviour is Fixed. Within a section, order
   Feature, then Improved, then Fixed.
7. **Honor the docs style guide** (`documentation/.claude/CLAUDE.md`): replace
   banned vague adjectives — most commonly "robust"/"robustly" →
   "reliable"/"reliably", and drop "comprehensive", "powerful", "seamless" — and
   replace effort/speed filler ("easy", "instantly") with concrete wording.
8. **Sanitize for an external audience.** The `.changelog/` source is written for
   a reader who knows the repo; the public copy must not assume that. Preserve
   the release value and the chronology; only change how it is said:
   - **Internal implementation-failure wording → positive phrasing.** Never ship
     the words deadlock, freeze/frozen, memory leak, leak, silently fail(ing),
     garbage, race condition, stall, crash, null-reference. Rewrite around the
     user-visible win ("more reliable under load", "lower memory use on a large
     build").
   - **Never name the h5 predecessor as a defect source.** "h5 did X wrong" reads
     badly and h5 is still in use. Say what Transpose does, and reference the
     [migration guide](/transpose/migrating-from-h5) for a difference a porting
     user must act on.
   - **Do not describe a not-yet-implemented feature as coming.** State what
     exists. A gap belongs in the docs pages, which say so plainly, not in a
     changelog promise.
   - **Do not name an unpublished package** (see §4).
   - **Consolidate small items.** Fold several tiny fixes in a section into one
     `title="Reliability improvements"` entry. Don't over-merge genuinely
     distinct features.
   If a section is emptied by these removals, drop its now-empty heading too.

The folder is marked as a changelog by `transpose/changelog/index.yml`
(`changelog: true`) — leave that file alone.

Verify it renders before committing: `Neko build -i . -o /tmp/nekoout
--no-api-sync` from the docs repo root should print
`Generated changelog /transpose/changelog` and parse each new file. (The CLI
executable is `Neko` with a capital N and lives in `$HOME/.dotnet/tools`;
pre-existing failures from the `tesserae/` live samples are unrelated.) Commit
the mirrored file(s) on a branch in the **documentation** repo and open a
separate PR there — it is a different repository, so it gets its own branch and
PR.

---

## 9. Open a PR

Commit the new `.changelog/<yy.M>/<yy.M.compilerBuild>.md` file(s) together with
the refreshed `.changelog/CurrentVersion` on a single branch, and open one PR
against `master`:

- Branch: `claude/changelog-YYYY-MM-DD` (generation date, ISO).
- PR title: `Changelog week of YYYY-MM-DD`.
- PR body: a short summary of the headline changes per category, plus the
  calendar version recorded in `CurrentVersion`. Do not paste the full file.
- Label: **`changelog`**. Add it on creation.

In the Claude Code Cloud container the `gh` CLI is not available; use the
`mcp__github__*` tool family for branch / PR / label operations. Locally, `gh`
is fine.

---

## 10. Bulk / historical backfill

To generate several weeks at once, the same rules apply, plus a repeatable
pipeline:

1. **Unshallow** the clone (§1) so `git log` sees the whole range.
2. **Do not go back before the week of 2026-07-13** (§Cadence). Everything
   earlier is h5.
3. **Gather raw data once** for the whole range:
   - commits: `git log master --since=<start> --until=<end>
     --pretty=format:'%cd%x09%H%x09%s' --date=short` (committer date buckets by
     when work landed on `master`).
   - NuGet versions with publish dates: walk the registration API per package
     (§4); also fetch the **last version published before the range start** as a
     carry-forward anchor so the earliest weeks still show a version.
4. **Bucket by Monday-started week.** For each week assemble: all commits in
   `[Mon, Sun]`, the newest `Transpose.Compiler` build in the week (→ the
   revision `yy.M.<build>`, and the `<yy.M>` folder from that build's month), and
   the latest-as-of-week-end version of every package (carry forward). Skip weeks
   with zero commits. Writing one small temp file per week (commit hashes plus
   the package/version context) makes the next step clean.
5. **Fan out sub-agents**, one per week (§1), each pointed at that week's temp
   file and instructed to analyse **only those historical commit hashes** (not
   HEAD) and write its category body to a known path. Batch the launches;
   collect as they finish.
6. **Assemble deterministically.** You (not the agents) prepend the header and
   the consistent **Current packages** manifest to each agent's body and write
   the final `.changelog/<yy.M>/<yy.M.compilerBuild>.md`. Keeping assembly in one
   place guarantees identical formatting, correct version strings, and correct
   links across every file.
7. Set `.changelog/CurrentVersion` to the `yy.M` of the most recent week, then
   commit all files together.

Scripting steps 3-4 and 6 (a short Python helper over the registration JSON and
`git log`) is reliable and cheap; reserve the sub-agents for the
judgement-heavy diff reading in step 5.

---

## Quick checklist

1. Set `gen-date = today`, `start = today − 7d` (Mon–Sun week). Unshallow the
   clone if the window predates it. Never reach back before 2026-07-13.
2. List **all** `master` commits in the window; for each, read message → PR body
   → diff as needed to find user impact and category. Spawn `general-purpose`
   sub-agents (one per commit group/week) for large windows, each scoped to
   specific hashes and told to analyse the historical commits, not HEAD.
3. Apply the relevance test, the consolidation rules, and the
   implementation-detail filter. Read new tests to learn the construct, then
   write about the construct.
4. Group entries into the six categories (Features for headline cross-surface
   capabilities, then Language and Compiler, Runtime and Base Library, Web and
   Package Bindings, Build and Tooling, Performance). Split Compiler from Runtime
   by *where the behaviour lives*, and do not double-list a Features item.
5. Fetch the NuGet versions via the **registration** API (the flat container
   404s; decode with `utf-8-sig`) for the **Current packages** block; carry
   forward any quiet package so the manifest is the same complete set on every
   file. Leave the unpublished packages out.
6. Compute the revision `yy.M.<compilerBuild>` and write
   `.changelog/<yy.M>/<yy.M.compilerBuild>.md`.
7. Overwrite `.changelog/CurrentVersion` with `yy.M`.
8. If a `documentation` checkout is available, mirror the file(s) into
   `documentation/transpose/changelog/` (flat, version-named) with the public
   transform — drop the H1 and the `master`/commits line, add `date:` + `link:`
   frontmatter, convert the manifest to a five-line `links` block, use
   `# :icon-…:` category headings and one `::: change {badge=… title=…}`
   container per entry, sanitize, fix banned adjectives. Build to confirm
   `Generated changelog /transpose/changelog`, then commit and open a separate PR
   in the docs repo.
9. Branch + commit the `.changelog` file(s) + PR labelled `changelog`.
