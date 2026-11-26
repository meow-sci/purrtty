---
type: File Format Reference
title: Debug info (the image's Debug section and the DebugInfo model)
description: The optional Debug section of a kVM image — its record model, exact byte layout, how the emitter fills it, the consistency checks applied at load, and which tools consume it.
resource: ksa-decomp:KSA.Kfs.Vm.Debug/DebugInfo.cs
tags: [kfs, debug, image, file-format, tooling, symbols]
sources:
  - id: debug-info
    resource: ksa-decomp:KSA.Kfs.Vm.Debug/DebugInfo.cs
    title: DebugInfo (decompiled)
  - id: debug-records
    resource: ksa-decomp:KSA.Kfs.Vm.Debug/DebugFunction.cs
    title: DebugFile, DebugType, DebugField, DebugFunction, DebugLine, DebugGlobal, DebugLocal (decompiled)
  - id: debug-enums
    resource: ksa-decomp:KSA.Kfs.Vm/DebugTypeKind.cs
    title: DebugTypeKind, DebugLang, DebugLocalKind (decompiled)
  - id: emitter
    resource: ksa-decomp:KSA.Kfs.Compiler.Backend/Emitter.cs
    title: Emitter (decompiled)
  - id: image-reader
    resource: ksa-decomp:KSA.Kfs.Vm.Image/ImageReader.cs
    title: ImageReader (decompiled)
  - id: host-computer
    resource: ksa-decomp:KSA.Kfs.Host/HostComputer.cs
    title: HostComputer (decompiled)
  - id: sram-view
    resource: ksa-decomp:KSA.Kfs.Host/SramView.cs
    title: SramView (decompiled)
  - id: global-leaves
    resource: ksa-decomp:KSA.Kfs.Host.Views/GlobalLeaves.cs
    title: GlobalLeaves (decompiled)
  - id: live-lines
    resource: ksa-decomp:KSA.Kfs.Host.Views/LiveLines.cs
    title: LiveLines (decompiled)
  - id: listing
    resource: ksa-decomp:KSA.Kfs.Host.Workbench/Listing.cs
    title: Listing (decompiled)
  - id: global-dump
    resource: ksa-decomp:KSA.Kfs.Cli/GlobalDump.cs
    title: GlobalDump (decompiled)
  - id: kvm-machine
    resource: ksa-decomp:KSA.Kfs.Vm/KvmMachine.cs
    title: KvmMachine (decompiled)
  - id: disassembler
    resource: ksa-decomp:KSA.Kfs.KAsm/Disassembler.cs
    title: Disassembler (decompiled)
generated: { by: claude-code/claude-fable-5-2, at: 2026-11-25T21:29:02Z }
status: stable
ksa_build: "2026.11.25.5949"
---

# Purpose

The Debug section (section 7 of a [kVM image](/compiler/kvm-image-format.md)) maps code addresses and
RAM addresses back to source: files, functions, source lines, globals, locals and a small type graph.
It is optional — written when `Emitter.EmitDebug` is true (the default; `kvm-cli --no-debug` and the
kC-only CLI turn it off) — and the header's `FLAG_DEBUG` bit must agree with its presence. The VM does
not need it to run a program; tools do.

`DebugInfo` is both the in-memory model and the (de)serialiser:

```csharp
public sealed class DebugInfo {
    public const uint NONE = uint.MaxValue;            // "no file" / "no type" / "no ref"
    public const byte FN_TASK_ENTRY = 1, FN_SUSPENDS = 2, FN_LIBRARY = 4, FN_FAR = 8;
    public List<string> Strings { get; }               // Strings[0] is always ""
    public List<DebugFile> Files { get; }      public List<DebugType> Types { get; }
    public List<DebugField> Fields { get; }    public List<DebugFunction> Functions { get; }
    public List<DebugLine> Lines { get; }      public List<DebugGlobal> Globals { get; }
    public List<DebugLocal> Locals { get; }
    public uint String(string s);                      // intern, returns index
    public string Name(uint index);                    // "" when out of range
    public byte[] Write();
    public static DebugInfo? Parse(ReadOnlySpan<byte> s, uint codeSize, uint globalsStart, uint globalsEnd,
        uint maxRam, Func<uint, bool> isBoundary, Func<uint, int> enterFrameAt, out string? reason);
}
```

All names are indices into the section's own string table (not the image's Strings
section).[^debug-info]

# Records

| record | fields |
| --- | --- |
| `DebugFile` | `Name`, `Lang` (`DebugLang`), `SourceHash` |
| `DebugType` | `Kind` (`DebugTypeKind`), `Flags`, `Size`, `Ref`, `Count`, `Name`, `FirstField`, `Unit`, `Frame` |
| `DebugField` | `Name`, `Type`, `Offset` |
| `DebugFunction` | `Name`, `Start`, `End`, `Frame`, `NArgs`, `ResultCells`, `Lang`, `Flags`, `File`, `Line`, `FirstLocal`, `LocalCount`, `Type` |
| `DebugLine` | `Pc`, `File`, `Line`, `Col` |
| `DebugGlobal` | `Name`, `Type`, `Address`, `File` |
| `DebugLocal` | `Function`, `Name`, `Type`, `FrameOffset`, `ScopeStart`, `ScopeEnd`, `Kind` (`DebugLocalKind`) |

Enumerations (byte values):

| enum | values |
| --- | --- |
| `DebugLang` | 0 `KASM`, 1 `KC`, 2 `KScript`, 3 `Generated` |
| `DebugTypeKind` | 0 `Void`, 1 `Bool`, 2 `Int`, 3 `Float`, 4 `Pointer`, 5 `Array`, 6 `Struct`, 7 `Enum`, 8 `Vector`, 9 `Matrix`, 10 `Record`, 11 `Function` |
| `DebugLocalKind` | 0 `Local`, 1 `Argument`, 2 `Temporary` |
| type `Flags` | bit 0 signed, bit 1 const (bits 3+ must be 0) |
| function `Flags` | bit 0 `FN_TASK_ENTRY`, bit 1 `FN_SUSPENDS`, bit 2 `FN_LIBRARY`, bit 3 `FN_FAR` (bits 4+ must be 0) |

# Byte layout

Little-endian throughout; produced by `DebugInfo.Write`.[^debug-info-write]

```text
u16  debug_version = 1
u16  reserved = 0
u32  string_table_bytes                         (a multiple of 4)
     strings: { u16 length, UTF-8 bytes, zero padding so the entry is a multiple of 4 } …
u32  file_count      Files[]      12 bytes each
u32  type_count      Types[]      32 bytes each
u32  field_count     Fields[]     16 bytes each
u32  function_count  Functions[]  40 bytes each
u32  line_count      Lines[]      16 bytes each
u32  global_count    Globals[]    16 bytes each
u32  local_count     Locals[]     28 bytes each
```

| record | layout |
| --- | --- |
| File (12) | `u32 name`, `u8 lang`, `u8 0`, `u16 0`, `u32 source_hash` |
| Type (32) | `u8 kind`, `u8 flags`, `u16 0`, `u32 size`, `u32 ref`, `u32 count`, `u32 name`, `u32 first_field`, `u32 unit`, `u32 frame` |
| Field (16) | `u32 name`, `u32 type`, `u32 offset`, `u32 0` |
| Function (40) | `u32 name`, `u32 start`, `u32 end`, `u32 frame`, `u8 nargs`, `u8 result_cells`, `u8 lang`, `u8 flags`, `u32 file`, `u32 line`, `u32 first_local`, `u32 local_count`, `u32 type` |
| Line (16) | `u32 pc`, `u32 file`, `u32 line`, `u16 col`, `u16 0` |
| Global (16) | `u32 name`, `u32 type`, `u32 address`, `u32 file` |
| Local (28) | `u32 function`, `u32 name`, `u32 type`, `u32 frame_offset`, `u32 scope_start`, `u32 scope_end`, `u8 kind`, `u8 0`, `u16 0` |

The parts must sum exactly to the section size.

# How the emitter fills it

`Emitter.BuildDebug` (kC and kASM alike):[^emitter-debug]

- **Files** — one per entry of the module's file list plus any file first seen on an instruction's
  span. `Lang` = `IrModule.FileLanguages[file]`, defaulting to `KASM`. `SourceHash` =
  `Crc32.Compute(UTF8(file))` — the CRC-32 of the file **name** string, not of its contents.
- **Functions** — in placement order: address range (`End` = the next function's start or the code
  size), `Frame`, `NArgs` = argument cells, `ResultCells`, `Lang` from the function's language, source
  file index and line, the range of its locals, `Type` = `NONE`. `Flags`: `FN_TASK_ENTRY` if it is a
  task entry, `FN_SUSPENDS` if it can reach a `YIELD`, `FN_FAR` if it starts at or above 0x10000.
  `FN_LIBRARY` is defined but **never set** in this build.
- **Locals** — one per *named* frame slot: frame offset, kind (argument / temporary / local), and a
  scope equal to the **whole function** (`ScopeStart` = function start, `ScopeEnd` = function end).
- **Lines** — one per instruction whose source line differs from the previous entry's `(file, line)`,
  plus one for each function's `ENTER`; `Col` is clamped to 0–65,535. Lines are ascending by `Pc`.
- **Globals** — one per `IrGlobal`: `Name` = `DebugName ?? Name`; `File` = `NONE` for generated globals
  (string literals) and unknown files.
- **Types** — interned. An `IrType` becomes `(Kind, Flags, Size, Ref, Count, Name, FirstField)` with
  `Ref` = element type for arrays and pointers, `Count` = array length or struct field count,
  `FirstField` = index of the struct's first `DebugField`. A slot or global with no `IrType` gets a
  synthetic type by size: 4 → signed `int32_t`, 8 → signed `int64_t`, 1 → unsigned `uint8_t`, anything
  else → an array of `uint8_t` of that length. `Unit` and `Frame` are always 0 (the empty string):
  `Inferred:` reserved for physical units and reference frames; unused in this build, as are the
  `Vector`, `Matrix`, `Record` and `Function` kinds.

kASM slots carry only a type *name* (`.args p : vec3*`), which is not turned into an `IrType`, so kASM
locals and globals appear with the synthetic size-based types.

# Checks at load

`ImageReader` calls `DebugInfo.Parse(section, codeSize, globalsStart: 16, globalsEnd: <lowest task
stack base, or vm_region_base>, maxRam: 1,048,576, isBoundary, enterFrameAt, out reason)`; any failure
becomes load rule `debug` with the reason text.[^debug-info-parse]

Structural (reason prefix `debug:`): section too small; `debug_version is not 1`; reserved field not 0;
string table size not a multiple of 4 or overrunning; string entries not ending at the table's size;
invalid UTF-8; non-zero string padding; `string 0 is not the empty string`; each of
`Files[]`/`Types[]`/`Fields[]`/`Functions[]`/`Lines[]`/`Globals[]`/`Locals[]` overrunning the section;
reserved bytes not 0; `parts don't sum to the section size`.

Consistency (`DebugInfo.Check`):

| subject | requirement |
| --- | --- |
| files | name is a valid string index; `Lang ≤ 3` |
| types | `Kind ≤ 11`; no flag bits above 2; `Name`/`Unit`/`Frame` valid strings; `Size ≤ maxRam`; `Ref` is `NONE` or a valid type (not checked for enums); struct/record field ranges inside `Fields[]` |
| fields | valid name and type |
| type graph | no cycle through array, vector, matrix or field edges (pointers do not count) |
| functions | valid name, file (or `NONE`) and type (or `NONE`, else a `Function`-kind type); `Start < End ≤ codeSize`; sorted and non-overlapping; `Lang ≤ 3`; no flag bits above 3; `Start` is an `ENTER` whose frame equals `Frame`; local range inside `Locals[]` and each local pointing back at the function |
| lines | `Pc` on an instruction boundary; strictly ascending; valid file |
| globals | valid name, type, file (or `NONE`); `globalsStart ≤ Address < globalsEnd` |
| locals | valid function, name, type; `Kind ≤ 2`; `FrameOffset + type size ≤ function frame`; scope inside the function |

# Consumers

| consumer | what it reads | for |
| --- | --- | --- |
| `KvmMachine` | `Functions` by `Start` | function names in profiles and traps[^kvm-machine-names] |
| `HostComputer` | `Lines`, `Files`, `Functions` | turning a pc into `file:line` and a function name for console output and snapshots[^host-computer-lines] |
| `SramView` | `Globals` | naming the global that contains a RAM address |
| `GlobalLeaves` (views) | `Globals`, `Types`, `Fields`, `Files` | flattening globals into typed scalar leaves for watch lists and plots |
| `LiveLines` (views) | `Lines` | marking source lines that executed, from the execution trace |
| `Listing` (workbench) | `Lines`, `Files` | the source-interleaved listing |
| `Workbench` | `Functions`, `Globals` | function names; looking a global up by name for console commands |
| `DisassemblyView.CopyAsKasm` | `Globals`, `Types` | restoring global names in copied kASM |
| `Disassembler` | `Functions` | function names in the disassembly |
| `kvm-cli --dump-globals` (`GlobalDump`) | `Globals`, `Types`, `Fields` | printing typed global values; requires a Debug section[^global-dump] |

See [debug window](/tooling/debug-window.md) and [views](/host/views.md).

# Modding notes

- **The Debug section is the only symbol table in an image.** With it a mod can resolve
  `global name → RAM address → typed value` on a running computer (`image.Debug.Globals`, then read RAM
  through the host) without the source. Without it (`--no-debug`) there are no names at all.
- `DebugInfo` is public, with public mutable lists, `Write()` and `Parse()`; a tool can read, extend
  and rewrite a Debug section, then splice it back with `ImageBuilder`-style serialisation — the
  result must still satisfy `Check` (sorted functions and lines, frames matching `ENTER`s).
- `SourceHash` cannot be used to detect a stale source file: it hashes the name only.
- Globals named in debug info use the *link* name unless a `DebugName` was set; kC statics that
  collided are `name.N`, string literals are `.strN` with no file.
- Nothing in the VM's execution path depends on debug info, so stripping it changes no behaviour, only
  tooling.

[^debug-info]: DebugInfo.cs:11-60, 589-605
[^debug-info-write]: DebugInfo.cs:62-160
[^debug-info-parse]: DebugInfo.cs:162-587; ImageReader.cs:479-489
[^emitter-debug]: Emitter.cs:203-211, 283, 461-464, 468-497, 767-873
[^kvm-machine-names]: KvmMachine.cs:1944-1949
[^host-computer-lines]: HostComputer.cs:833-860
[^global-dump]: GlobalDump.cs:18-37; Cli.cs:446
