---
type: Bundle Conventions
title: kfs-info bundle conventions
description: Authoring rules for this OKF bundle — frontmatter template, source URI schemes, citation style, evidence labels, and the directory layout.
tags: [kfs, okf, conventions, meta]
generated: { by: claude-code/claude-fable-5-2, at: 2026-11-25T21:17:39Z }
status: stable
ksa_build: "2026.11.25.5949"
---

# Purpose

`docs/kfs-info/` is an Open Knowledge Format (OKF) v0.2 knowledge bundle documenting **kFS** — the
programmable flight computer built into Kitten Space Agency (KSA): a C-like language (kC), an
assembler (kASM), a bytecode VM (kVM), and the host and game glue around them — from a **modding**
point of view: how it works, what it exposes, and where a StarMap/Harmony mod can extend it.

Everything here was derived by reading the **decompiled game assemblies** for one specific KSA build.
kFS is explicitly a prototype in that build (`KfsPrototype`), so expect it to change. Every concept
carries `ksa_build` so a reader can tell which build a claim was checked against.

# Frontmatter template

Every concept document (every `.md` except `index.md` and `log.md`) starts with:

```yaml
---
type: <see "Type vocabulary">
title: <short human title>
description: <exactly one sentence>
resource: <the primary thing documented — a source URI, see below>
tags: [kfs, <area>, ...]
sources:
  - id: <short-kebab-id>            # stable id, cited from the body as a footnote
    resource: ksa-decomp:KSA.Kfs.Vm/KvmMachine.cs
    title: KvmMachine (decompiled)
generated: { by: claude-code/claude-fable-5-2, at: <ISO 8601 UTC, from `date -u +%Y-%m-%dT%H:%M:%SZ`> }
status: stable            # or draft when substantial parts are inferred rather than read
ksa_build: "2026.11.25.5949"
---
```

`ksa_build` is a bundle-specific extension key.

Add `verified: { by, at }` only when a document's claims were actually checked — by a human reviewer
(`human:<id>`) or by an executed process (`process:<id>`). The documents whose results were produced
by running code against the real `KSA.dll` outside the game carry
`verified: { by: process:kfs-offline-spike, … }`; their bodies say exactly what was run. **Nothing in
this bundle has been checked against the running game.**

## Type vocabulary

Use one of these `type` values (OKF allows any string; staying inside this list keeps the bundle
searchable):

| type | use for |
| --- | --- |
| `Subsystem Reference` | how one subsystem/class cluster works end to end |
| `Language Reference` | kC / kASM syntax and semantics |
| `Data Table Reference` | an embedded JSON table: schema plus its full contents |
| `File Format Reference` | binary or on-disk formats (image, workspace files) |
| `API Reference` | the callable surface of a type, with signatures |
| `Game System Reference` | non-kFS game systems the extension must touch (parts, lights, animations) |
| `Extension Point` | a specific place a mod can hook, with patch targets and risks |
| `How-To` | task-oriented steps |
| `Concept` | a cross-cutting idea or mental model |
| `Glossary` | term definitions |
| `Bundle Conventions` | this document |

# Source URI schemes

The sources live outside this repository, at machine-specific paths, so path-valued fields
(`resource`, `sources[].resource`) use small URI schemes instead of filesystem paths:

| scheme | resolves to | example |
| --- | --- | --- |
| `ksa-decomp:` | `<ksa-game-assemblies>/current/decomp/` (ILSpy output of the game DLLs) | `ksa-decomp:KSA.Kfs/KfsServices.cs` |
| `ksa-content:` | `<ksa-game-assemblies>/current/Content/` (game asset XML etc.) | `ksa-content:Core/...` |
| `unscience:` | the `meow-sci/unscience` repository root | `unscience:zippo.lib/LightController.cs` |
| `gatos:` | the `meow-sci/gatOS` repository root | `gatos:...` |
| `starmap:` | the `StarMapLoader/StarMap` repository root | `starmap:...` |

Files inside the purrtty repository are referenced with ordinary relative paths.

Embedded resources decompile to flat files beside the namespace folders, e.g.
`ksa-decomp:KSA.Kfs.Vm.Tables.Data.services.json` and `ksa-decomp:KSA.Kfs.KC.Library.math.c`.

# Citations

- Cite load-bearing claims with a Markdown footnote keyed to a `sources[].id`:
  `The table is loaded lazily.[^kfs-integration]` then, at the end of the document,
  `[^kfs-integration]: KfsIntegration.cs:89`.
- Put `file:line` (or a line range) in the footnote text whenever the claim is about specific code.
  Line numbers are those of the decompiled file for `ksa_build`; they shift between builds.
- Never key a footnote to a list position.

# Evidence labels

Decompiled code is the only ground truth available. Be explicit about how a claim was established:

- **Unlabelled statements** are read directly from the code or data.
- **`Inferred:`** marks a conclusion drawn from code that was not traced all the way (say what it
  rests on).
- **`Unverified:`** marks something plausible that was not confirmed in the sources at all.
- **`Not active in this build:`** marks features that exist in tables or signatures but are stubbed,
  reserved, or rejected at runtime.

Never present a guess as fact, and never invent identifiers, numbers, or behaviour.

# Body conventions

- Structural Markdown: headings, tables, fenced code. Prefer tables for enumerations and reproduce
  small data tables **in full** — this bundle is meant to be usable without the decompiled sources at
  hand.
- Quote real identifiers exactly (type, member, constant, JSON key, numeric id).
- End a document with a `# Modding notes` section whenever the subject has consequences for extending
  kFS from a mod: what is public vs private/internal, what is static, what is validated, what is cached,
  what runs on which thread.
- Link other concepts with bundle-relative links, e.g. `[services table](/tables/services-table.md)`.

# Directory layout

| directory | contents |
| --- | --- |
| `/overview/` | architecture, glossary, namespace source map, these conventions |
| `/language/` | the kC language, preprocessor, headers, standard library, diagnostics |
| `/compiler/` | compile pipeline, IR, backend, analysis/WCET, kASM, the kVM image format |
| `/vm/` | the kVM machine: opcodes, memory, tasks, costs, traps, syscall ABI |
| `/tables/` | the embedded JSON game tables and their loader |
| `/host/` | the host layer between VM and game: computers, layouts, bus, service handling |
| `/integration/` | the KSA-side glue: dispatch, threading, services, sampling, write-back |
| `/tooling/` | workbench, editor, reference window, console commands, CLI, examples |
| `/game-targets/` | non-kFS game systems the extension targets: parts, lights, animations |
| `/modding/` | extension points and patch strategy for mods |

Each directory has an `index.md` (no frontmatter) listing its concepts with their descriptions. Only
the bundle-root `index.md` carries `okf_version` frontmatter. `log.md` at the root records changes,
newest first.
