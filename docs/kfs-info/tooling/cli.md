---
type: API Reference
title: kFS command-line tools (kvm-cli, kc-cli)
description: The KSA.Kfs.Cli namespace — the kvm-cli and kc-cli entry points compiled into the game assembly, their arguments, files, exit codes and JSON outputs, and the finding that nothing in the shipped game calls them.
resource: ksa-decomp:KSA.Kfs.Cli/Cli.cs
tags: [kfs, tooling, cli, json, compile, extension]
sources:
  - id: cli
    resource: ksa-decomp:KSA.Kfs.Cli/Cli.cs
    title: Cli (decompiled)
  - id: kc-cli
    resource: ksa-decomp:KSA.Kfs.Cli/KcCli.cs
    title: KcCli (decompiled)
  - id: cli-json
    resource: ksa-decomp:KSA.Kfs.Cli/CliJson.cs
    title: CliJson source-generated context (decompiled)
  - id: kc-cli-json
    resource: ksa-decomp:KSA.Kfs.Cli/KcCliJson.cs
    title: KcCliJson source-generated context (decompiled)
  - id: json-records
    resource: ksa-decomp:KSA.Kfs.Cli/RunJson.cs
    title: DiagJson, RunJson, TaskJson records (decompiled)
  - id: global-dump
    resource: ksa-decomp:KSA.Kfs.Cli/GlobalDump.cs
    title: GlobalDump (decompiled)
  - id: program
    resource: ksa-decomp:KSA/Program.cs
    title: KSA Program (decompiled)
  - id: ksa-csproj
    resource: ksa-decomp:KSA.csproj
    title: KSA.csproj (ILSpy project file)
  - id: disassembler
    resource: ksa-decomp:KSA.Kfs.KAsm/Disassembler.cs
    title: Disassembler (decompiled)
  - id: workbench
    resource: ksa-decomp:KSA.Kfs.Host.Workbench/Workbench.cs
    title: Workbench (decompiled)
  - id: offline-harness
    resource: /modding/offline-harness.md
    title: Offline harness experiments E1–E12
generated: { by: claude-code/claude-fable-5-2, at: 2026-11-25T21:56:52Z }
status: stable
ksa_build: "2026.11.25.5949"
---

# Summary

`KSA.Kfs.Cli` contains two command-line front ends for the kFS toolchain:[^cli][^kc-cli]

| class | program name in messages | entry point |
| --- | --- | --- |
| `public static class Cli` | `kvm-cli` | `public static int Run(string[] args, TextWriter output, TextWriter error)` |
| `public static class KcCli` | `kc-cli` | `public static int Run(string[] args, TextWriter output, TextWriter error)` |

Both are ordinary library methods that take an argument vector and two writers and return an exit
code. They do the same jobs as the in-game [Workbench](/tooling/workbench.md) — compile, assemble,
run, disassemble — but on files, without a workspace, and they can write a `.kvm` image and a
`.report.json` to disk, which the workbench never does.

# Reachability in the shipped game

**Not active in this build:** nothing calls either `Run` method.

- The namespace is compiled into the main game assembly (the decompiled sources sit under
  `KSA.csproj`, and the shipped `dll/` folder has no separate CLI assembly or executable).[^ksa-csproj]
- A search of every decompiled source file finds no reference to `KSA.Kfs.Cli`, `Cli.Run` or
  `KcCli.Run` outside the namespace itself. The only other mention of the name is a comment string
  the disassembler emits (`; disassembled by kvm-cli; ...`).[^disassembler]
- The game executable's own argument handling (`Program.Main` → `ProcessCliArgs`) knows exactly five
  switches — `-build-info`, `-fixed-viewport`, `-force-x11`, `-wayland`, `-load` — none of which
  reaches kFS.[^program]
- No `kfs` console verb forwards to it (see [console commands](/tooling/console-commands.md)).

`Inferred:` the developers build `kvm-cli` and `kc-cli` as thin executables around these methods
outside the shipped game (the usage strings and the test-oriented options such as `--sensors`,
`--dump-each-tick` and `--peephole-off` point that way), and the classes ship only because they live
in the same project. A mod can call them directly; see "Modding notes".

# `kvm-cli`

Usage text, verbatim:[^cli]

```text
usage:
  kvm-cli compile [<project folder>] [--out out.kvm] [--part NAME] [--layout FILE] [--game DIR] [--bounds on|off] [--optimize 0|1]
                  [--double-bits 32|64] [-D NAME[=value]] [--peephole-off N[,N...]] [--no-debug] [--json] [--ir] [--report]
  kvm-cli assemble <file.kasm>... [-o out.kvm] [--part NAME] [--game DIR] [--no-debug] [--image] [--json] [--report]
  kvm-cli run <image.kvm> --ticks N [--part NAME] [--layout FILE] [--sensors FILE] [--game DIR] [--json] [--profile] [--dump-globals] [--dump-each-tick]
  kvm-cli disasm <image.kvm> [--part NAME] [--game DIR]
```

## Common behaviour

`Cli.Run`:[^cli]

1. No arguments: prints the usage to `error`, returns 2.
2. Parses `args[1..]` with one shared option parser (below), whatever the subcommand.
3. Tables: `GameTables.LoadEmbedded()` — a **fresh** instance per call — or `GameTables.Load(dir)` with
   `--game DIR`.
4. Part: `tables.Part(--part ?? bench part name)`. Layout: `tables.BenchLayout`, or
   `GameTables.LoadLayout(file)` with `--layout FILE`.
5. Dispatches on `args[0]`; anything else is `kvm-cli: unknown command '<x>'` plus the usage (2).
6. `ArgumentException`, `IOException`, `GameTablesException`, `UnauthorizedAccessException` and
   `FormatException` are caught and reported as `kvm-cli: <message>` (2).

### Options

| option | value | default | used by |
| --- | --- | --- | --- |
| `-o`, `--out` | output path | see each command | compile, assemble |
| `--part` | part name (full, short or bench) | the bench part | all |
| `--game` | folder with the table JSON files | embedded tables | all |
| `--layout` | I/O layout JSON file | bench layout (`compile` also auto-loads `layout.json` from the project folder) | all (the parser accepts it for every command) |
| `--ticks` | non-negative integer | — (required by `run`) | run |
| `--sensors` | sensor input file (`SensorFile`) | all-zero sensors | run |
| `--json` | flag | off | compile, assemble (diagnostics), run (result) |
| `--no-debug` | flag: do not emit the debug section | off | compile, assemble |
| `--report` | flag: also print the compile report text | off | compile, assemble |
| `--ir` | flag: print the IR | off | compile |
| `--image` | flag: `Assembler.ImageMode` | off | assemble |
| `--profile` | flag: enable the VM profiler | off | run |
| `--dump-globals` | flag: print globals as JSON instead of the summary | off | run |
| `--dump-each-tick` | flag: dump after every tick (needs `--dump-globals`) | off | run |
| `--bounds` | `on` \| `off` | `on` | compile |
| `--optimize` | `0` \| `1` | `1` | compile |
| `--double-bits` | `32` \| `64` | `64` | compile |
| `-D` | `NAME[=value]`, repeatable | none | compile |
| `--peephole-off` | `N[,N...]`, repeatable (accumulates) | none | compile |

A value-taking option at the end of the line fails with `<option> needs a value`; an unknown `-x`
fails with `unknown option '<x>'`; `--bounds`, `--optimize` and `--double-bits` reject other values
with `<option> takes ... , not '<v>'`. Every other argument is a file.[^cli]

### Exit codes

| code | meaning |
| --- | --- |
| 0 | success |
| 1 | the build failed, the image is invalid, or the run ended in a trap |
| 2 | usage error or one of the caught exceptions |

## `compile`

1. At most one positional argument: the project folder (default `.`); otherwise
   `compile takes one project folder`. It must exist (`'<path>' is not a folder`).
2. Without `--layout`, a `layout.json` inside the folder is used as the I/O layout.
3. Input files: every file directly in the folder ending in `.c`, `.h`, `.kasm` or `.py`
   (case-insensitive), in ordinal order.
4. `Compiler.Compile` with `ProjectName` = the folder name and `CompileOptions` built from
   `--bounds`, `--optimize`, `--double-bits`, `-D`, `--peephole-off` and `EmitDebug = !--no-debug`.
5. Diagnostics go to **`error`**, one per line: JSON with `--json`, else `Diagnostic.ToString()`.
6. With `--ir` and a module, `IrPrinter.Print(module)` goes to `output`.
7. On failure returns 1. Otherwise writes the image to `--out`, or `<current directory>/<project>.kvm`;
   writes the compile report next to it (below); and, unless `--ir`, prints `<path>: <n> bytes`.

Differences from an in-game build of the same folder (see
[workspace and files](/tooling/workspace-and-files.md)): the CLI includes `.py` files, does **not**
include `.kinc` files, does **not** skip `vehicle.h`, and lets the caller choose compile options and
the layout.[^cli][^workbench]

## `assemble`

1. At least one `.kasm` file (`assemble needs at least one .kasm file`). Each becomes a
   `SourceFile(<file name>, <text>)`.
2. `new Assembler(tables, part, layout) { EmitDebug = !--no-debug, ImageMode = --image, ProjectFiles = ... }`;
   quoted includes are read from the folder of the **first** file.
3. Diagnostics to `error` as for `compile`; failure returns 1.
4. The image is written to `-o`, or the first file's path with the extension `.kvm`; the report is
   written next to it; `<path>: <n> bytes` is printed.

## The report file

Whenever a build produced a `CompileReport`, `WriteReport` writes `report.ToJson()` to the image
path with its extension replaced by `.report.json` (so `out.kvm` → `out.report.json`), and prints
`report.Text` to `output` when `--report` is given.[^cli] The report's content is in
[compile report](/compiler/compile-report.md).

## `run`

1. Requires `--ticks N`: `run needs --ticks N (recorded streams arrive with B17, MS7)`.
2. Exactly one image file (`kvm-cli: give one image`). It is read with `ImageReader.ReadFile`, a
   `KvmMachine(part, tables, 0)` is created and `LoadImage(bytes, layout)` is called. An invalid
   image prints `kvm-cli: <file>: invalid image (<rule>: <message>)` and returns 1; an
   `UnsupportedHostException` from the machine prints its message and returns 1.
3. `--dump-globals` needs a debug section
   (`--dump-globals needs an image with a Debug section (tooling §8)`); `--dump-each-tick` needs
   `--dump-globals`.
4. `vm.Profiling = --profile`; `vm.Reset(ResetCause.PowerOn)`.
5. For `k` from 0 to `N - 1`: `vm.SetSystem(k, 0)`, the sensor block is written (from the sensor
   file bound to the layout, else zeros), `vm.RunTick()`. Cycles, bytecodes and overruns are summed.
   The loop ends early on a trap or when the tick reports `NothingLeftToRun`.
6. Output, one of:
   - `--dump-globals`: the globals JSON (below) on `output`; a trap is reported on `error` as
     `trap: <code> at 0x<pc> in task <n>`.
   - `--json`: one `RunJson` line (below).
   - default text:

     ```text
     ticks <n>, cycles <n>, bytecodes <n>, overruns <n>, command stream 0x<hash>
     trap: <code> at 0x<pc> in task <n>
       <kind> <task>: RAM stack <n> B, return depth <n>, operand depth <n>
       <function>: self <n>, total <n>, calls <n>
     ```

     (trap line only on a trap; one line per task; function lines only with `--profile`, sorted by
     self cycles.)
7. Returns 1 when the run trapped, else 0.

`run` drives a bare `KvmMachine`; no `HostComputer` is involved. `Inferred:` service requests issued
by the program are therefore not acted on by any host in this mode (see
[services syscall ABI](/vm/services-syscall-abi.md)).

## `disasm`

Loads one image as `run` does and writes `Disassembler.Disassemble(image, tables)` to `output`. The
listing starts with the comment line

```text
; disassembled by kvm-cli; `kvm-cli assemble --image` reassembles it to a byte-identical code section (kASM §6.3, P-041)
```

which the in-game `kfs disasm` prints as well, because it calls the same method.[^cli][^disassembler]
See [kASM disassembler](/compiler/kasm-disassembler.md).

# `kc-cli`

Usage text, verbatim:[^kc-cli]

```text
usage:
  kc-cli check [<project folder>] [--part NAME] [--game DIR] [--bounds on|off] [--optimize 0|1] [--double-bits 32|64] [-D NAME[=value]] [--json]
```

A compile-only checker with one subcommand, `check`:

1. Any other first argument prints `kc-cli: unknown command '<x>'` and the usage; no argument prints
   the usage. Both return 2.
2. Options as for `kvm-cli compile`, minus `--out`, `--layout`, `--peephole-off`, `--no-debug`,
   `--ir`, `--report`. `--strict` is recognised only to fail with
   `--strict (Power of Ten mode) arrives with milestone MS13`. A second positional argument fails
   with `check takes one project folder`.
3. Input files: `.c` and `.h` files of the folder only (no `.kasm`).
4. Compiles against the bench layout with `EmitDebug = false`; nothing is written to disk.
5. Diagnostics go to **`output`** (not `error`), as JSON with `--json`, else as text.
6. Returns 1 when the compile failed, else 0; caught exceptions print `kc-cli: <message>` and return 2.

# JSON outputs

## `DiagJson` — one line per diagnostic

```csharp
public sealed record DiagJson(string Severity, string Code, string File, int Line, int Col, string Message, string? Hint);
```

Serialised with a source-generated context using `JsonKnownNamingPolicy.SnakeCaseLower`
(`CliJson.Default.DiagJson` in `kvm-cli`, `KcCliJson.Default.DiagJson` in `kc-cli`).[^cli-json][^kc-cli-json][^json-records]

```json
{"severity":"error","code":"<code>","file":"<file>","line":0,"col":0,"message":"<text>","hint":null}
```

`severity` is `error` or `warning`; `hint` is `null` when the diagnostic has none (the generated
writer emits the property unconditionally).

## `RunJson` and `TaskJson` — `kvm-cli run --json`

```csharp
public sealed record RunJson(long Ticks, long Cycles, long Bytecodes, long Overruns, string? Trap, string CommandStreamHash, List<TaskJson> Tasks);
public sealed record TaskJson(string Name, string Kind, uint MaxRamStackBytes, int MaxReturnDepth, int MaxOperandDepth);
```

```json
{"ticks":0,"cycles":0,"bytecodes":0,"overruns":0,"trap":null,"command_stream_hash":"0x00000000",
 "tasks":[{"name":"<task>","kind":"<kind>","max_ram_stack_bytes":0,"max_return_depth":0,"max_operand_depth":0}]}
```

(Shown wrapped; the tool writes one line. Values are placeholders.) `trap` is `null` or
`<code> at 0x<pc> in task <n>`; `command_stream_hash` is `0x` plus eight hex digits; `kind` is the
task kind in lower case; the maxima are the VM's measured per-task maxima.[^cli][^json-records]

## `CliJson` and `KcCliJson`

Both are `System.Text.Json` source-generation contexts
(`[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]`).
`CliJson` declares `DiagJson` and `RunJson` (and so `TaskJson` and `List<TaskJson>`); `KcCliJson`
declares `DiagJson` only. They contain no hand-written logic.[^cli-json][^kc-cli-json]

## `GlobalDump` — `kvm-cli run --dump-globals`

`GlobalDump.Render(KvmImage image, Func<int, int, byte[]> readRam)` builds JSON by hand from the
image's debug information.[^global-dump]

```json
{"globals":[
{"name":"<global>","type":"<type>","value":<value>,"bits":<bits>},
...
]}
```

- **Which globals:** debug globals whose file index is not `uint.MaxValue`, ordered by address.
- **`type`:** `bool`; `f32` / `f64`; `i8` .. `u64` (`i` when the signed flag is set); pointers as
  `u32`; enums as `i32` or `u32`; arrays as `<element>[n][m]`; structs as `{field:type,field:type}`.
- **`value`:** arrays as JSON arrays, structs as objects, booleans as `true` / `false`, integers in
  decimal, floats in round-trip (`R`) format; non-finite floats are the **strings** `"nan"`, `"inf"`,
  `"-inf"`.
- **`bits`:** present only when the type contains a float; the raw bit pattern of each float as a
  lower-case hex string without prefix, in the same array / object shape (struct members without
  floats are omitted, other non-float leaves are `null`).

With `--dump-each-tick` the tool wraps the per-tick dumps:

```json
{"ticks":[
{"tick":0,"globals":[
...
]},
{"tick":1,"globals":[
...
]}
]}
```

`GlobalDump.RenderOne`, `TypeName` and the two `FloatText` overloads are public and usable on their
own.

# Files the CLI reads and writes

| file | direction | meaning |
| --- | --- | --- |
| `<project folder>/*.c`, `*.h`, `*.kasm`, `*.py` | read by `compile` | sources |
| `<project folder>/layout.json` | read by `compile` when present and no `--layout` | I/O layout (`GameTables.LoadLayout`), see [I/O layout tables](/tables/io-layout-tables.md) |
| `--game DIR` | read | a folder holding the table JSON files (`GameTables.Load`), see [game tables loader](/tables/game-tables-loader.md) |
| `--sensors FILE` | read by `run` | per-tick sensor values (`SensorFile.Load`, `Bind`, `Write`) |
| `<out>.kvm` | written by `compile`, `assemble`; read by `run`, `disasm` | the kVM image, see [kVM image format](/compiler/kvm-image-format.md) |
| `<out>.report.json` | written by `compile`, `assemble` | the compile report as JSON |

# Modding notes

- **Callable from a mod, no reflection:** `KSA.Kfs.Cli.Cli.Run(new[] { "compile", folder, "--out", path }, stdout, stderr)`
  and `KcCli.Run(...)`. They are self-contained: no game state, no workbench, no main-thread
  requirement in the code read here.[^cli][^kc-cli]
- **A terminal mod can expose them as shell commands** by passing its own writers; the exit code and
  the stdout / stderr split map directly onto a shell command. Note the stream asymmetry: `kvm-cli`
  writes diagnostics to `error`, `kc-cli` writes them to `output`.
- **Relative paths resolve against the process directory**, which `Program.Main` sets to the game's
  base directory (`Directory.SetCurrentDirectory(AppDomain.CurrentDomain.BaseDirectory)`); the default
  `compile` output (`<cwd>/<project>.kvm`) would land in the game folder. Pass absolute paths.
  [^program][^cli]
- **The CLI does not share the game's tables.** It calls `GameTables.LoadEmbedded()` on every run
  rather than using `KfsIntegration.Tables`, so services, messages or parts that a mod added to the
  live tables are unknown to it. A mod that wants CLI builds of programs using its extensions must
  either provide a patched table folder through `--game DIR` or patch `GameTables.LoadEmbedded` /
  `GameTables.Load`.[^cli] The tested way to add services is such a patch — a Harmony postfix on
  `GameTables.LoadEmbedded`, verified to replace the tables `KfsIntegration.Tables` returns (see
  [adding a service](/modding/adding-a-service.md)).[^offline-harness] `Inferred:` with that postfix
  in place every CLI run without `--game` gets its own augmented instance, since it calls the same
  method; `--game DIR` goes through `GameTables.Load(folder)` and bypasses the postfix. No CLI call
  was run.
- **Images built by the CLI for the bench layout will not load on a vehicle** (different layout
  fingerprint) unless built with `--layout` matching that vehicle; the in-game path
  ([build and load flow](/tooling/build-and-load-flow.md)) handles this automatically.
- **Unique capabilities compared with the workbench:** compile options (`-D`, `--optimize`,
  `--bounds`, `--double-bits`, `--peephole-off`), IR printing, writing `.kvm` and `.report.json`,
  deterministic headless runs with scripted sensors, and JSON output. For automated testing of a
  mod's kC library these are the relevant entry points. A test process outside the game first has
  to load `KSA.dll` itself (IL-only, PE machine x64); how is shown in
  [the offline harness](/modding/offline-harness.md), which drives the compiler and a host directly
  rather than through these methods.

[^cli]: Cli.cs — `Options` record and parser :20-260; usage text :262; `Run` :264-291; `UsageError` :293-298; `Compile` :300-360; `Assemble` :362-389; `WriteReport` :391-401; `LoadForTools` :403-433; `RunImage` :435-571; `Disasm` :573-582.
[^kc-cli]: KcCli.cs — usage text :13; `Run` :15-171 (option parsing :33-121, `--strict` :107-108, file selection :129-131, compile :132-151, diagnostics to `output` :152-155).
[^cli-json]: CliJson.cs — attributes :11-13; property names :40-76; `DiagJsonSerializeHandler` :288-304; `RunJsonSerializeHandler` :562-578; `TaskJsonSerializeHandler` :793-807; `ListTaskJsonSerializeHandler` :877.
[^kc-cli-json]: KcCliJson.cs — attributes :10-11; property names :28-40; `DiagJsonSerializeHandler` :242-258.
[^json-records]: DiagJson.cs:3; RunJson.cs:5; TaskJson.cs:3.
[^global-dump]: GlobalDump.cs — `Render` :16-35; `RenderOne` :37-54; `TypeName` :56-104; `Value` :117-168; `Bits` :170-217; `FloatText` :219-251; `IntText` :253-284; `Escape` :286-314.
[^program]: Program.cs — `CliArgs` table :105-112; `Main` (sets the current directory, calls `ProcessCliArgs`) :2034-2044; `ProcessCliArgs` :2062-2086.
[^ksa-csproj]: KSA.csproj — the kFS embedded resources are declared in this single project (:18-51); the snapshot's `dll/` folder contains `KSA.dll` and no kFS-specific assembly.
[^disassembler]: Disassembler.cs — `Disassemble` :26; header comment :30.
[^offline-harness]: Experiment E10 in the offline harness document; `LoadEmbedded` and `Load(folder)` are GameTables.cs:75-84, the CLI's call is Cli.cs:274.
[^workbench]: Workbench.cs — in-game builds use default `CompileOptions` :507-514; Workspace.cs `ProjectOf` (file selection for comparison) :176-235.
