---
type: File Format Reference
title: kFS workspace and files
description: The on-disk kFS workspace under Documents/My Games/Kitten Space Agency/kfs — its folder structure, file extensions, generated headers, templates, naming rules, and exactly which files a build reads.
resource: ksa-decomp:KSA.Kfs.Host.Workbench/Workspace.cs
tags: [kfs, tooling, workspace, files, projects, extension]
sources:
  - id: workspace
    resource: ksa-decomp:KSA.Kfs.Host.Workbench/Workspace.cs
    title: Workspace (decompiled)
  - id: build-project
    resource: ksa-decomp:KSA.Kfs.Host.Workbench/BuildProject.cs
    title: BuildProject (decompiled)
  - id: workbench
    resource: ksa-decomp:KSA.Kfs.Host.Workbench/Workbench.cs
    title: Workbench (decompiled)
  - id: editor-model
    resource: ksa-decomp:KSA.Kfs.Host.Workbench/EditorModel.cs
    title: EditorModel (decompiled)
  - id: workspace-tree
    resource: ksa-decomp:KSA.Kfs.Host.Workbench/WorkspaceTree.cs
    title: WorkspaceTree (decompiled)
  - id: workspace-tree-state
    resource: ksa-decomp:KSA.Kfs.Host.Workbench/WorkspaceTreeState.cs
    title: WorkspaceTreeState (decompiled)
  - id: workspace-folder
    resource: ksa-decomp:KSA.Kfs.Host.Workbench/WorkspaceFolder.cs
    title: WorkspaceFolder (decompiled)
  - id: kfs-integration
    resource: ksa-decomp:KSA.Kfs/KfsIntegration.cs
    title: KfsIntegration (decompiled)
  - id: constants
    resource: ksa-decomp:KSA/Constants.cs
    title: KSA Constants (decompiled)
  - id: generated-headers
    resource: ksa-decomp:KSA.Kfs.KC/GeneratedHeaders.cs
    title: GeneratedHeaders (decompiled)
  - id: kc-front-end
    resource: ksa-decomp:KSA.Kfs.KC/KcFrontEnd.cs
    title: KcFrontEnd (decompiled)
  - id: system-headers
    resource: ksa-decomp:KSA.Kfs.KC/SystemHeaders.cs
    title: SystemHeaders (decompiled)
  - id: editor-window
    resource: ksa-decomp:KSA.Kfs/KfsEditorWindow.cs
    title: KfsEditorWindow (decompiled)
  - id: text-box-widget
    resource: ksa-decomp:KSA.Kfs/KfsTextBoxWidget.cs
    title: KfsTextBoxWidget (decompiled)
  - id: offline-harness
    resource: /modding/offline-harness.md
    title: Offline harness experiments E1–E12
generated: { by: claude-code/claude-fable-5-2, at: 2026-11-26T06:11:10Z }
status: stable
ksa_build: "2026.11.25.5949"
---

# Location

The game creates its [Workbench](/tooling/workbench.md) with the workspace folder

```text
Path.Combine(Constants.DocumentsFolderPath, "kfs")
```

and `Constants.DocumentsFolderPath` is
`<Environment.SpecialFolder.Personal>/My Games/Kitten Space Agency`.[^kfs-integration][^constants]
So the default workspace is `<Documents>/My Games/Kitten Space Agency/kfs`. (`Workspace` also declares
the constant `DEFAULT_FOLDER_NAME = "kfs"`. No reference to it appears in the decompiled sources, but
a `const` is inlined by the compiler; `Inferred:` the `"kfs"` literal above is that constant.)[^workspace]

`kfs ws <path>` points the workbench at another folder for the rest of the session
(`Workspace.Set`, which also creates it). Nothing saves the choice: a restarted game is back on the
default folder.[^workspace][^workbench]

# What exists on first run

Nothing is written at start-up. The `Workspace` constructor only resolves the path.[^workspace] The
folder is created (empty) the first time something calls `Workspace.Ensure()`: `kfs ws`, `kfs ls`,
`kfs edit` with no file, any file creation, a header write, or the editor's file tree being drawn.
[^workspace][^workbench][^editor-window]

`Workspace.List()` and `Workspace.Write()` call `Ensure()` themselves, so **merely listing the
workspace from code creates the folder**. A mod that wants to show the workspace's files without
that side effect (for a menu, say) has to enumerate `Workspace.Folder` with its own directory
calls; `Workspace.Exists(file)`, `Resolve(file)` and the `Folder` property touch nothing.[^workspace-ensure]

Examples appear only when the player asks for them (`kfs examples`, or File > Examples in the editor);
see [examples](/tooling/examples.md).

# Folder structure

```text
<workspace>/
  some_program.c          a root-level file builds on its own (plus what it #includes)
  some_routine.kasm
  shared.h                root-level headers: reachable from root-level .c files only
  my_project/             a subfolder is a project: all its sources build together
    main.c
    helper.h
    fast.kasm
    vehicle.h             generated, never part of the build input
    vehicle.kinc          generated, never part of the build input
```

- **A subfolder is a project.** Building any file inside it builds every source file of that folder
  as one program.[^workspace]
- **A root-level `.c` or `.kasm` file is its own project**, named after the file.[^workspace]
- Listing is **one level deep**: `Workspace.List()` returns root files, then the files of each direct
  subfolder as `folder/file`; deeper folders are not listed (and so do not show in `kfs ls` or the
  editor tree), although a path into them still resolves.[^workspace]
- Listing filters only by a leading `.` in the file name. The help text and the "no files" message
  speak of `.kasm`, `.kinc`, `.c` and `.h` files, but any other file in the folder is listed too.
  [^workspace][^workbench]
- Paths are workspace-relative with `/` separators. `Workspace.Resolve` returns a rooted path
  unchanged and does not reject `..`, so the file commands also accept paths outside the workspace.
  [^workspace]

# File types

| name / extension | meaning | builds as a root? | in a project's build input? |
| --- | --- | --- | --- |
| `.c` | kC source | yes (kC compile) | yes |
| `.h` | kC header | no: `<file> is a header: build a .c or .kasm file that includes it (a subfolder builds as one project)` | yes |
| `.kasm` | kASM source | yes (assembled, or compiled alongside `.c` files of the same folder) | yes |
| `.kinc` | kASM include | no (same message) | yes |
| `vehicle.h`, `vehicle.kinc` | headers **generated** for the current target's I/O layout | no | **excluded by name** |
| `<name>.kasm.h` | not a file: a header the kC front end generates in memory for `<name>.kasm` | no | pulled in as `<name>.kasm` |
| anything else | listed, editable, ignored by builds | no | no |

`Workspace.Extensions` is `{ ".kasm", ".kinc", ".c", ".h" }` (matched case-insensitively);
`Workspace.SourceExtensions` (`{ ".c", ".kasm" }`) is declared but unused. `Workspace.IsKc(file)` is
true for `.c` and `.h`.[^workspace]

The workbench keeps built images, listings and compile reports **in memory only**
(`AssembleOutcome`); it never writes a `.kvm` image or a report file. Those artefacts exist only for
the [CLI](/tooling/cli.md), which the shipped game does not expose.[^workbench]

## Size and encoding limits

| limit | value | where |
| --- | --- | --- |
| largest source file read | `MAX_SOURCE_BYTES` = 4,194,304 bytes; larger files raise an `IOException` with `<name> is <n> bytes; the largest source file kFS reads is 4194304 bytes` | [^workspace] |
| editor's saved-text buffer | `EditorModel.SAVED_CAPACITY` = 262,144 bytes initially, grown on demand | [^editor-model] |
| editor text box buffer | `KfsTextBoxWidget.BUFFER_CAPACITY` = 262,144 bytes | [^text-box-widget] |
| encoding | the editor reads UTF-8 (a UTF-8 BOM is stripped); a UTF-16 BOM or invalid UTF-8 falls back to `File.ReadAllText`; files are written with `File.WriteAllText` | [^editor-model][^workspace] |

`Inferred:` a file of 262,144 bytes or more (up to the 4 MiB read limit) cannot be shown by the text
box. The widget's buffer is fixed-size and nothing truncates: `KfsTextBoxWidget.Draw` passes the
whole text to `ImInputString.SetValue`, which throws (`Length must be less than Capacity to
accommodate null-terminator.`) when the UTF-8 length is not below the capacity.[^text-box-widget]
What the editor window does with that exception was not traced.

# Generated headers: `vehicle.h` and `vehicle.kinc`

`Workbench.WriteHeaders(file)` writes both files into the project folder of `file` (or the workspace
root for a root-level file) through
`GeneratedHeaders.Write(folder, Tables, TargetLayout, TargetName, Target.Part.Name)`.[^workbench] Each
file is rewritten only when its bytes differ (UTF-8, no BOM).[^generated-headers]

| when | call |
| --- | --- |
| `kfs build <file>`, and every `kfs load` (which builds first) | `Build` → `ReportHeaders(file, always: false)` |
| editor Upload | `LoadBuilt` → `ReportHeaders(file, always: false)` |
| `kfs headers [file]` | `Headers` → `ReportHeaders(file, always: true)` |

Editor Save and the editor's Build button do **not** refresh them.[^workbench][^editor-model]

The header banner says the files are "Generated and read-only: the in-game workbench rewrites it at
kfs build and kfs headers, and edits are lost".[^generated-headers] They are for the reader and for
external tools: the build never reads them from disk. `ProjectOf` skips both names, and the compiler
substitutes its own generated text for `<vehicle.h>` and `<vehicle.kinc>`.[^workspace][^kc-front-end]
Their content is documented in [kC generated headers](/language/kc-generated-headers.md) and
[I/O layout generation](/host/io-layout-generation.md).

# What a build reads: `BuildProject`

```csharp
public sealed record BuildProject(string Folder, string Name,
                                  IReadOnlyDictionary<string, string> Files, bool IsKc)
{
    public string WorkspacePath(string file);   // Folder + "/" + file, or file when Folder is ""
}
```

`Workspace.ProjectOf(file, openFile, openText, out why)` builds it.[^workspace][^build-project] `Files`
maps a **project-relative** name to the file's text; for the file currently open in the editor the
editor's (possibly unsaved) text is used instead of the disk text.

## File in a subfolder

- `Folder` = the file's folder (everything before the last `/`), `Name` = that folder's last segment.
- `Files` = every file directly in the folder whose extension is in `Workspace.Extensions`, in ordinal
  order, **except** `vehicle.h` and `vehicle.kinc`.
- `IsKc` = at least one `.c` file is present.

## Root-level file

- `.h` or `.kinc`: no project; `why` is the "is a header" message above.
- Otherwise `Folder = ""`, `Name` = file name without extension, `IsKc` = extension is `.c`, and
  `Files` starts with the file itself.
- For a `.c` file the text is scanned breadth-first for quoted includes, using the multiline regex
  shown below:
  - `"X.kasm.h"` adds root-level `X.kasm` when it exists;
  - any other name **without a `/`** that exists at the workspace root is added and scanned in turn.
- A root-level `.kasm` file is alone in `Files`.

```text
^\s*#\s*include\s*"([^"]+)"\s*
```

The scan is a regular expression over the whole text (`RegexOptions.Multiline`, applied with
`Matches`), not the preprocessor, and it differs from what the compiler will later include in
three ways:[^workspace-include-scan]

- **It does not know comments or conditionals.** An `#include "x.h"` inside a `/* … */` block or
  an inactive `#if` branch is found like any other, so `x.h` joins the project's file set (when it
  exists) although the compiler never reads it. This only widens the set.
- **It misses an indented include that directly follows another include line.** The pattern ends
  in `\s*`, which also consumes the line break and the next line's indentation; the search for the
  next match then resumes in the middle of that line, where `^` cannot match. In

  ```c
  #include "a.h"
      #include "b.h"
  #include "c.h"
  ```

  the scan finds `a.h` and `c.h` and not `b.h`. If `b.h` is reached by no other include it is
  missing from the project's file set, and the compiler — which looks a quoted include up in that
  set, not on disk — reports it as not found (`KC-E015`) although the file exists. An unindented
  include on the next line is found (the match stops exactly at its line start).
- **The name may span lines**: `[^"]+` accepts a line break between the quotes.

`Inferred:` these three follow from the pattern and from how `Regex.Matches` continues after a
match; the game was not run. The same pattern, executed by .NET's regular-expression engine,
behaves this way in purrTTY's tests (`purrTTY.Kfs.Tools.Tests/Projects/IncludeScanTests.cs` in
this repository), whose project resolver reproduces the scan deliberately so that a tool builds
the same file set as the game.

Only a root-level `.c` file is scanned at all; a folder project takes every source file of the
folder.

## How the two languages resolve includes

| include | resolved from |
| --- | --- |
| kC `#include "name"` | `BuildProject.Files`, then the in-memory `.kasm.h` headers, then the system headers[^kc-front-end] |
| kC `#include <name>` | `CompileInput.SystemFiles` (empty in the workbench), then the generated `vehicle.h`, then `SystemHeaders.Files`[^kc-front-end][^system-headers] |
| kASM `.include "name"` (pure kASM project) | `folder/name` in the workspace, then root-level `name` (`Assembler.ProjectFiles` callback set by the workbench)[^workbench] |
| kASM `.include "name"` (inside a kC project) | `BuildProject.Files` only[^kc-front-end] |
| kASM `.include <name>` | the built-in includes; see [kASM assembly language](/compiler/kasm-assembly-language.md) |

So for kC there is **no shared include directory in the workspace**: a `.h` must sit in the project
folder (or, for a root-level `.c`, at the root).

## Build cache invalidation

`AssembleOutcome.SourceStamps` records the last-write time of every project file except the one being
edited. `EditorModel.BuildOf` reuses a finished build only while those stamps are unchanged and
`Workspace.HoldsSourcesOf(project)` confirms the folder still contains exactly the same set of source
files.[^workbench][^editor-model][^workspace]

# Creating files

## Templates

`Workbench.TemplateFor(file)` picks the initial text by extension.[^workbench]

| extension | text |
| --- | --- |
| `.c` | `Workspace.NEW_KC_TEMPLATE` (below) |
| `.kasm`, or no extension | `Workspace.NEW_FILE_TEMPLATE` (below) |
| `.h` | `/* header */` and a line break |
| `.kinc` | `; kASM include` and a line break |
| other | empty |

`NEW_KC_TEMPLATE` (stored with CRLF line endings):[^workspace]

```c
/* New kC program. Build with kfs build (or Save in the editor), load with kfs load, run with kfs start.
   kC's log and trace bindings arrive with the services (MS6): watch globals on the oscilloscope instead
   (kfs watch wave). */
#include <kfs.h>
#include <math.h>
#pragma kfs tick_hz 10
#pragma kfs task step rate_hz=10

uint32_t count;
float wave;

void step(void) {
    count++;
    wave = sinf(0.1f * (float)count);
}
```

`NEW_FILE_TEMPLATE` (kASM, CRLF line endings):[^workspace]

```kasm
; New kASM program. Assemble with kfs build, load with kfs load, run with kfs start then kfs tick or kfs run.
.include <kfs.kinc>
.include <vehicle.kinc>
.tick_hz 10
.format f_hello "hello from tick %u"

.task main rate_hz=10
.func main
    .local lbuf, f_hello.size, 8
    ADDRL  lbuf
    LDG    sys.tick
    STW
    PUSH16 f_hello
    ADDRL  lbuf
    SYSCALL SVC_LOG
    RET    0
.endfunc
```

The kC template's comment says kC log and trace bindings "arrive with the services (MS6)", yet
`kfs.h` in this build already declares `kfs_logf` and `trace`, and the `kC_ascent_simple` example
calls `kfs_logf`.[^system-headers] The template comment is stale; whether both bindings work end to
end is covered in [kC builtins and intrinsics](/language/kc-builtins-and-intrinsics.md). For
`kfs_logf` this was verified by execution: a kC program's `kfs_logf` line appeared in the computer's
`ConsoleLog` when run offline against the real `KSA.dll`.[^offline-harness]

## Entry points and naming rules

| entry point | default extension | name rules | result |
| --- | --- | --- | --- |
| `kfs new <file>` → `Workbench.New` | `.kasm` | none (the argument is used as a workspace path; folders are created) | file at that path, opened |
| editor File > New File... / tree context menu → `Workbench.NewFile(project, name)` | `.c` | not empty; must not start with `.`; only ASCII letters, digits, `_`, `-`, `.` | `project/name`; with no project selected, a new project folder named after the file (which must not already exist) |
| editor File > New Project... → `Workbench.NewProject(name, kc)` | `main.c` or `main.kasm` | not empty; only ASCII letters, digits, `_`, `-`; must not already exist as a file or folder | `name/main.c` or `name/main.kasm` |
| `kfs disasm <function> --copy <file>` | `.kasm` | none | one function of the loaded image as a kASM file |

Error texts returned by `NewFile` / `NewProject`: `give the file a name`,
`the name can't start with '.'`, `use letters, digits, '_', '-' and '.' only`,
`no project is selected and <p> already exists in the workspace`,
`project <p> isn't in the workspace`, `<path> already exists`, `give the project a name`,
`use letters, digits, '_' and '-' only`, `<name> already exists in the workspace`.[^workbench]

## Deleting

`Workbench.DeleteFile(file)` deletes the file, forgets its stashed edits, closes it in the editor,
clears `_lastFile` when it pointed there, and removes the containing folder if that left it empty.
[^workbench] Only the editor reaches it (tree context menu > Delete, behind a confirmation popup).

# The editor's view of the workspace

- **Tree model.** `WorkspaceTree.Build(files)` turns the flat list into nested `WorkspaceFolder`
  records (`Name`, `Path`, `Folders`, `Files`), folders and files sorted case-insensitively then
  ordinally. `WorkspaceTreeState` holds the set of expanded folders and `Reveal(file)` expands the
  ancestors of the open file once per file.[^workspace-tree][^workspace-tree-state][^workspace-folder]
- **Refresh.** The editor window watches the workspace with a `FileSystemWatcher` (created, deleted,
  renamed; subdirectories included) and re-lists when it fires; without a watcher it re-lists once a
  second.[^editor-window]
- **Unsaved edits per file.** Opening another file stashes the current file's unsaved text in
  `EditorModel._unsaved` (keyed by full path, case-insensitive) and restores it on return; the tree
  marks such files with ` *`.[^editor-model][^editor-window]
- **External changes.** Twice a second `Workbench.Frame` calls `EditorModel.CheckExternalChange()`:
  a changed file is reloaded when there are no unsaved edits (`reloaded <file> (changed on disk)`),
  otherwise the model sets `ExternalChangePending` and the status
  `<file> changed on disk; Revert to load it`.[^editor-model][^workbench] Editing workspace files
  in an external editor therefore works while the in-game editor is open.

# `Workspace` API

| member | behaviour | Workspace.cs |
| --- | --- | --- |
| `Workspace(string folder)` | `Folder = Path.GetFullPath(folder)` | 27-30 |
| `string Folder { get; private set; }` | absolute workspace path | 25 |
| `void Set(string folder)` | changes `Folder` and creates it | 32-36 |
| `void Ensure()` | creates `Folder` | 38-41 |
| `List<string> List()` | root files, then `sub/file` for each direct subfolder; names starting with `.` skipped | 43-59 |
| `string Resolve(string file)` | rooted path unchanged, else `Folder` + file | 66-73 |
| `bool Exists(string file)` | `File.Exists(Resolve(file))` | 75-78 |
| `string Read(string file)` | size check, then `File.ReadAllText` | 80-93 |
| `static string? TooLarge(string path)` | message when the file exceeds 4 MiB | 95-103 |
| `void Write(string file, string text)` | creates parent folders, `File.WriteAllText` | 105-111 |
| `string? ProjectFile(string name)` | text of a workspace file or `null` | 113-121 |
| `static string FolderOf(string file)` | part before the last `/` (backslashes normalised), or `""` | 123-132 |
| `bool HoldsSourcesOf(BuildProject project)` | folder still holds exactly the project's source files | 134-158 |
| `static bool IsKc(string file)` | `.c` or `.h` | 160-167 |
| `BuildProject? ProjectOf(string file, string? openFile, string? openText, out string? why)` | see above | 176-235 |

# Modding notes

- **Everything here is public.** `Workspace`, `BuildProject`, `WorkspaceTree`, `WorkspaceTreeState`
  and `WorkspaceFolder` are public types; `KfsIntegration.Workbench.Workspace` is reachable without
  reflection.[^workspace][^kfs-integration]
- **Shipping source files from a mod.** `Workspace.Write("purrtty_lights/main.c", text)` creates the
  project folder and file; the editor tree picks it up through its file watcher. There is no
  manifest, index or registration step for workspace content.
- **Shipping a header usable from every kC project.** The workspace has no shared include path, so a
  header written to disk only helps the project folder it sits in. The compiler's system header table
  `SystemHeaders.Files` is a `public static readonly Dictionary<string, string>`, read on every
  `#include <name>` and as the fallback for `#include "name"`; an entry added there is visible to
  every build in the process.[^system-headers][^kc-front-end] How mod-declared functions then bind
  to services is covered in [kC builtins and intrinsics](/language/kc-builtins-and-intrinsics.md) and
  [kC generated headers](/language/kc-generated-headers.md).
- **Per-layout constants.** `vehicle.h` / `vehicle.kinc` are regenerated from the target's `IoLayout`
  and the tables, cached per layout object in `GeneratedHeaders`; anything a mod adds to the layout or
  message tables shows up in them only if it is in place before the first generation for that layout.
  [^generated-headers]
- **No sandbox.** Paths are not confined to the workspace (`Resolve` accepts rooted paths and `..`);
  a mod exposing `kfs` verbs to scripts should validate paths itself.
- **Main-thread file I/O.** All `Workspace` calls are synchronous; the stock callers are console
  commands and ImGui handlers on the main thread, plus `BuildText` on a thread-pool thread when the
  editor builds in the background (it calls `ProjectOf`, hence `Read`).

[^workspace]: Workspace.cs — `DEFAULT_FOLDER_NAME` :13; `Extensions` :15; `SourceExtensions` :17; `MAX_SOURCE_BYTES` :19; templates :21-23; constructor :27-30; `Set` :32-36; `Ensure` :38-41; `List` and `IsListed` :43-59; `IsSource` :61-64; `Resolve` :66-73; `Read` / `ReadSource` / `TooLarge` :80-103; `Write` :105-111; `FolderOf` :123-132; `HoldsSourcesOf` :134-158; `IsKc` :160-167; `QuotedInclude` regex :169-174; `ProjectOf` :176-235 (folder project :182-193, header refusal :194-199, quoted-include scan :201-225).
[^build-project]: BuildProject.cs:5-15.
[^workbench]: Workbench.cs — `WriteHeaders` :207-214; `ReportHeaders` :216-262; `Headers` :264-268; `Ws` :396-404; `Ls` (message) :406-418; `New` :453-476; kASM `ProjectFiles` callback :526-529; `SourceStampsOf` :589-610; `Build` (header refresh) :659-678; `LoadBuilt` :918-937; `Frame` (external-change check) :1174-1179; `TemplateFor` :2156-2179; `NewFile` :2181-2233; `DeleteFile` :2235-2261; `NewProject` :2263-2294. No method writes an image or report to disk.
[^editor-model]: EditorModel.cs — `SAVED_CAPACITY` :26; `_unsaved` stash :40, :315-328; `ReadSaved` (UTF-8, BOM handling) :168-203; `Open` :211-241; `Save` (no header refresh) :349-365; `BuildOf` :367-394; `CheckExternalChange` :490-534.
[^workspace-tree]: WorkspaceTree.cs — `Build` :32-59; `Ancestors` :61-70; `Leaf` :72-81; `Normalise` :83-86; sort order :27-28.
[^workspace-tree-state]: WorkspaceTreeState.cs — `IsOpen` / `Set` :14-29; `Reveal` :31-48.
[^workspace-folder]: WorkspaceFolder.cs:5.
[^kfs-integration]: KfsIntegration.cs — workspace path :99; `Workbench` property :93-121.
[^constants]: Constants.cs:289.
[^generated-headers]: GeneratedHeaders.cs — file names :19-21; banner lines :25-44; per-layout text cache :23, :51-61; `Write` (rewrite only on difference, UTF-8 without BOM) :228-251.
[^kc-front-end]: KcFrontEnd.cs — `.kasm` files assembled first, their headers kept in the in-memory `generated` map :61-66; project and system include resolvers :75-79; nested assembler `ProjectFiles` :334.
[^system-headers]: SystemHeaders.cs — `Files` :8-22 (`kfs.h` :20 declares `kfs_logf` and `trace`); `Builtins` :24-30; `Get` :34-41.
[^editor-window]: KfsEditorWindow.cs — `WorkspaceTreeNow` :1416-1433; `WatchWorkspace` (calls `Ensure`, creates the `FileSystemWatcher`) :1435-1460; unsaved marker in the tree :1524-1527.
[^offline-harness]: The complete spike program and its output (`program log k=1: lights: 3`) in the offline harness document.
[^text-box-widget]: KfsTextBoxWidget.cs — `BUFFER_CAPACITY` :11-13; `Draw` (`_buffer.SetValue`) :41-54. Brutal.ImGuiApi/ImInputString.cs — `SetValue` :52-68; `ValidateLength` :82-88.
[^workspace-include-scan]: Workspace.cs:169 (`[GeneratedRegex("^\\s*#\\s*include\\s*\"([^\"]+)\"\\s*", RegexOptions.Multiline)]`), :203-224 (the breadth-first walk over `QuotedInclude().Matches(…)`).
[^workspace-ensure]: Workspace.cs:38-41 (`Ensure`), :43-54 (`List` calls `Ensure()` first), :105-111 (`Write` calls `Ensure()` first), :66-78 (`Resolve`, `Exists`: no side effect).
