---
type: File Format Reference
title: kVM image format (KVM1) and load-time validation
description: The binary kVM image byte by byte — header, section directory, messages, tasks, links, strings, enums, code, data and debug sections, the trailing CRC — plus every version and fingerprint baked into an image and every check ImageReader performs against the running tables, part and layout, with each LoadResult rule.
resource: ksa-decomp:KSA.Kfs.Vm.Image/ImageReader.cs
tags: [kfs, image, file-format, kvm, validation, fingerprint, versions, syscall, extension]
sources:
  - id: image-format
    resource: ksa-decomp:KSA.Kfs.Vm.Image/ImageFormat.cs
    title: ImageFormat (decompiled)
  - id: image-builder
    resource: ksa-decomp:KSA.Kfs.Vm.Image/ImageBuilder.cs
    title: ImageBuilder (decompiled)
  - id: image-reader
    resource: ksa-decomp:KSA.Kfs.Vm.Image/ImageReader.cs
    title: ImageReader (decompiled)
  - id: kvm-image
    resource: ksa-decomp:KSA.Kfs.Vm.Image/KvmImage.cs
    title: KvmImage (decompiled)
  - id: entries
    resource: ksa-decomp:KSA.Kfs.Vm.Image/TaskEntry.cs
    title: TaskEntry, MessageEntry, LinkEntry, StringEntry, DataRecord, EnumTable, FormatInfo, FunctionRange, Instr (decompiled)
  - id: load-result
    resource: ksa-decomp:KSA.Kfs.Vm.Image/LoadResult.cs
    title: LoadResult (decompiled)
  - id: log-format
    resource: ksa-decomp:KSA.Kfs.Vm.Image/LogFormat.cs
    title: LogFormat (decompiled)
  - id: cost-model
    resource: ksa-decomp:KSA.Kfs.Vm.Image/CostModel.cs
    title: CostModel and BusSteps (decompiled)
  - id: crc32
    resource: ksa-decomp:KSA.Kfs.Vm/Crc32.cs
    title: Crc32 (decompiled)
  - id: game-tables
    resource: ksa-decomp:KSA.Kfs.Vm.Tables/GameTables.cs
    title: GameTables (decompiled)
  - id: message-layout
    resource: ksa-decomp:KSA.Kfs.Vm.Tables/MessageLayout.cs
    title: MessageLayout (decompiled)
  - id: service-ids
    resource: ksa-decomp:KSA.Kfs.Vm/ServiceIds.cs
    title: ServiceIds (decompiled)
  - id: kvm-machine
    resource: ksa-decomp:KSA.Kfs.Vm/KvmMachine.cs
    title: KvmMachine (decompiled)
  - id: host-computer
    resource: ksa-decomp:KSA.Kfs.Host/HostComputer.cs
    title: HostComputer (decompiled)
  - id: vehicle-computer
    resource: ksa-decomp:KSA.Kfs.Host/VehicleComputer.cs
    title: VehicleComputer and Ownership (decompiled)
  - id: emitter
    resource: ksa-decomp:KSA.Kfs.Compiler.Backend/Emitter.cs
    title: Emitter (decompiled)
  - id: ir-validator
    resource: ksa-decomp:KSA.Kfs.Compiler.Ir/IrValidator.cs
    title: IrValidator (decompiled)
  - id: analysis
    resource: ksa-decomp:KSA.Kfs.Compiler/ProgramAnalysis.cs
    title: ProgramAnalysis (decompiled)
  - id: workbench
    resource: ksa-decomp:KSA.Kfs.Host.Workbench/Workbench.cs
    title: Workbench (decompiled)
generated: { by: claude-code/claude-fable-5-2, at: 2026-11-25T21:29:02Z }
status: stable
ksa_build: "2026.11.25.5949"
---

# Overview

A kVM image is one self-contained byte array: an 80-byte header, an 8-entry section directory, up to
eight sections, and a trailing CRC-32. It is produced by `ImageBuilder.Build()` (driven by the
[emitter](/compiler/backend-emitter.md)) and consumed by `ImageReader.Read`, which validates it against
**the part, the game tables and the I/O layout of the machine it is being loaded into** and returns a
decoded `KvmImage`.

- All multi-byte integers are **little-endian**.
- An image is **specific to one part and one I/O layout**: the header carries the part's parameters,
  the cost-table hash and the layout fingerprint, and the reader rejects any mismatch with "rebuild
  from source". Images are not portable between vehicles with different layouts.
- Maximum size `ImageFormat.MAX_IMAGE_BYTES` = 16,777,216 bytes; minimum 180 (header + directory +
  checksum).

Field names in `snake_case` below are the names the reader's own error messages use; where the code
never names a field, the `KvmImage` property name is given instead.

# Constants (`ImageFormat`)

| constant | value | | constant | value |
| --- | --- | --- | --- | --- |
| `Magic` | `"KVM1"` (4B 56 4D 31) | | `SEC_MESSAGES` … `SEC_DEBUG` | 0 … 7 |
| `FORMAT_VERSION` | 1 | | `FLAG_DEBUG` | 1 |
| `HEADER_SIZE` | 80 | | `FLAG_FAR_CALLS` | 2 |
| `DIRECTORY_ENTRIES` | 8 | | `FLAG_DOUBLE32` | 4 |
| `DIRECTORY_ENTRY_SIZE` | 12 | | `FEATURE_MASK` | 7 |
| `DIRECTORY_END` | 176 | | `CLAIM_ALLOCATION` | 1 |
| `MESSAGE_ENTRY_SIZE` | 12 | | `SENSOR_BASE` | `0x80000000` |
| `TASK_ENTRY_SIZE` | 40 | | `COMMAND_BASE` | `0x90000000` |
| `LINK_ENTRY_SIZE` | 8 | | `SYSTEM_BASE` | `0xA0000000` |
| `MAX_IMAGE_BYTES` | 16,777,216 | | `NV_BASE` | `0xB0000000` |
| `NULL_GUARD` | 16 | | `WINDOW_SIZE` / `SYSTEM_SIZE` | 65,536 / 256 |

`SectionNames` = `Messages`, `Tasks`, `Links`, `Strings`, `Enums`, `Code`, `Data`, `Debug` (index =
section number).[^image-format]

# File layout

```text
0x000  header (80 bytes)
0x050  directory: 8 × { u32 offset, u32 size, u32 count }        (ends at 0x0B0 = 176)
0x0B0  sections, in directory order, each starting on a multiple of 4; gaps are zero bytes
 …
end−4  u32 checksum = CRC-32 of bytes [0, end−4)
```

The checksum is the standard reflected CRC-32 (polynomial `0xEDB88320`, initial value and final XOR
`0xFFFFFFFF`).[^crc32]

## Header

Written by `ImageBuilder.Build`, read by `ImageReader.ReadCore`.[^image-builder-header]

| offset | size | field | source when built | reader check |
| --- | --- | --- | --- | --- |
| 0 | 4 | magic `KVM1` | constant | rule `magic` |
| 4 | 2 | `format_version` | 1 | rule `version` |
| 6 | 2 | flags | bit 0 = Debug section non-empty, bit 1 = at least one `CALLF`, bit 2 = `double` is 32-bit | bits 3–15 must be 0 (`reserved`); bit 0 must agree with the Debug section (`directory`) |
| 8 | 4 | `code_size` | `Code.Length` | = Code section size |
| 12 | 4 | `data_init_size` | Σ data record lengths | = Σ record lengths (`data`) |
| 16 | 4 | `runtime_lib_size` | `RuntimeLibCharge(tables, part.Costs, Code)` | recomputed and compared (`runtime-lib`) |
| 20 | 4 | `ram_required` | emitter: stack-area end + VM region bytes | ≤ `machine_ram` (`ram`) |
| 24 | 4 | cost-table hash (`CostTableHash`) | `part.Costs.Hash` | = `part.Costs.Hash` (`part`) |
| 28 | 4 | layout fingerprint (`LayoutFingerprint`) | `layout.Fingerprint` | = `layout.Fingerprint` (`layout`) |
| 32 | 2 | `service_table_version` | `tables.ServiceTableVersion` | = `tables.ServiceTableVersion` (`service-table`) |
| 34 | 2 | `message_catalogue_version` | `tables.MessageCatalogueVersion` | = `tables.MessageCatalogueVersion` (`message-catalogue`) |
| 36 | 1 | core class (`CoreClass`) | `part.CoreClass` (0 K8, 1 K32, 2 K32F, 3 K32D) | = part (`part`) |
| 37 | 1 | max return entries | `part.MaxReturnEntries` | = part (`part`) |
| 38 | 2 | max operand cells | `part.MaxOperandCells` | = part (`part`) |
| 40 | 4 | `clock_hz` | `part.ClockHz` | = part (`part`) |
| 44 | 2 | `tick_hz` | program's tick rate | in `tables.AllowedTickHz` (`tick-hz`) |
| 46 | 2 | `claims` | bit 0 = `ALLOCATION` | bits 1–15 must be 0 (`reserved`) |
| 48 | 4 | `machine_ram` | `part.RamBytes` | = part (`part`) |
| 52 | 4 | `machine_code` | `part.CodeBytes` | = part (`part`); ≥ code + data + runtime lib (`program-memory`) |
| 56 | 4 | `budget_cycles` | `clock_hz / tick_hz` (0 if `tick_hz` is 0) | = `clock_hz / tick_hz` (`budget`) |
| 60 | 4 | init budget cycles | `part.InitBudgetCycles` | = part (`part`) |
| 64 | 4 | NV bytes | `part.NvBytes` | = part (`part`) |
| 68 | 4 | features | `part.Features` (0 for every stock part) | bits 3–31 must be 0 (`reserved`); = part (`part`) |
| 72 | 4 | `vm_region_base` | emitter | < `machine_ram` (`ram`) |
| 76 | 4 | reserved | 0 | must be 0 (`reserved`) |

## Directory

Eight entries at `80 + 12·i`: `u32 offset`, `u32 size` (bytes), `u32 count`.

| i | section | count | size rule |
| --- | --- | --- | --- |
| 0 | Messages | number of entries | `count × 12` |
| 1 | Tasks | number of entries | `count × 40` |
| 2 | Links | number of entries | `count × 8` |
| 3 | Strings | number of strings | variable |
| 4 | Enums | number of enum tables | variable |
| 5 | Code | **0** | = `code_size` |
| 6 | Data | number of records | variable |
| 7 | Debug | **0** | variable |

The builder places non-empty sections consecutively from offset 176, rounding the running offset up to
a multiple of 4 after each; an empty section has offset 0 and size 0.[^image-builder-dir]

## Section 0 — Messages (12 bytes each)

| offset | size | field |
| --- | --- | --- |
| 0 | 2 | `message_id` |
| 2 | 2 | `body_size` — message bytes without the 16-byte header |
| 4 | 4 | `slot_offset` — offset of the message's slot from `vm_region_base` |
| 8 | 1 | role: bit 0 = subscribed by some task, bit 1 = published by some task |
| 9 | 3 | reserved, 0 |

Entries are in strictly ascending `message_id` order. Record: `MessageEntry(ushort MessageId, ushort
BodySize, uint SlotOffset, byte Role)`.

## Section 1 — Tasks (40 bytes each)

| offset | size | field | notes |
| --- | --- | --- | --- |
| 0 | 4 | `name_index` | index of a kind-0 string |
| 4 | 3 | entry | `u24` code address of the entry function's `ENTER` |
| 7 | 1 | kind | 0 `Init`, 1 `Periodic`, 2 `Program` |
| 8 | 2 | `rate_divisor` | run every N ticks; 1 for non-periodic |
| 10 | 2 | max operand cells | this task's operand-stack reservation |
| 12 | 4 | RAM stack base | 8-aligned RAM address |
| 16 | 4 | RAM stack size | bytes |
| 20 | 4 | VM stack base | 4-aligned RAM address inside the VM region |
| 24 | 1 | `max_call_depth` | return-stack entries reserved (≥ 1) |
| 25 | 1 | `program_share` | 0–100; non-zero only for a program task |
| 26 | 2 | reserved, 0 | |
| 28 | 4 | first subscription link | index into Links |
| 32 | 2 | subscription count | |
| 34 | 2 | publication count | |
| 36 | 4 | first publication link | index into Links |

Record: `TaskEntry(uint NameIndex, uint Entry, TaskKind Kind, ushort RateDivisor, ushort
MaxOperandCells, uint StackBase, uint StackSize, uint VmStackBase, byte MaxCallDepth, byte
ProgramShare, uint SubFirst, ushort SubCount, ushort PubCount, uint PubFirst)` with the computed
`VmStackSize = 4·MaxOperandCells + 8·MaxCallDepth`. Order: init, periodic by ascending divisor (then
priority, then declaration), program.

## Section 2 — Links (8 bytes each)

| offset | size | field |
| --- | --- | --- |
| 0 | 2 | `message_index` — index into Messages |
| 2 | 2 | reserved, 0 |
| 4 | 4 | buffer address — 8-aligned RAM address of the program's buffer global |

`Links[]` holds, for each task in `Tasks[]` order, its subscriptions and then its publications; each
run is sorted by message index, then buffer address. Record: `LinkEntry(ushort MessageIndex, uint
BufferAddress)`.

## Section 3 — Strings

A sequence of `u8 kind`, `u16 length`, `length` bytes of UTF-8 — no terminator, no padding between
entries. Kinds (`StringKind`): 0 `Name`, 1 `Format`, 2 `Channel`. A string's **index** is its position
in the sequence; code refers to formats and channels by index, and task entries and enum tables refer
to names by index. The emitter writes the program's format and channel strings first (in module
order), then one name string per task.[^emitter-strings]

## Section 4 — Enums

A sequence of tables: `u16 count`, `u16 reserved (0)`, then `count` × (`u32 value`, `u32 name_index`),
each `name_index` naming a kind-0 string. A format conversion `%{E:n}` selects table `n`.

`Not active in this build:` nothing in the compiler or assembler ever adds an `EnumTable` to an
`ImageBuilder`, so built images have no enum tables and a format that uses `%{E:n}` fails to load
("names a missing enum table"). The reader fully supports the section.

## Section 5 — Code

`code_size` bytes of kVM instructions: an opcode byte followed by its immediates as listed in the
opcode table (`i8`/`u8` 1 byte, `i16`/`u16`/`rel16` 2, `u24` 3, `u32` 4, `u64` 8). The code is a
sequence of functions, each beginning with `ENTER ncells, frame`; a function extends to the next
`ENTER`. Address 0 is the first function. See [kVM opcodes](/vm/opcodes.md).

## Section 6 — Data

A sequence of records: `u32 address`, `u32 length`, `length` bytes, zero padding to a multiple of 4.
At load each record is copied to RAM at `address`; RAM not covered by a record starts as zero. Record:
`DataRecord(uint Address, byte[] Bytes)`. The emitter writes one record per initialised global, with
relocations already applied.

## Section 7 — Debug

Optional (`EmitDebug`); see [debug info](/compiler/debug-info.md). Its own version word is 1.

# Format strings

`LogFormat` defines what a kind-1 string may contain; the same code validates formats at assembly,
at image load and when computing the argument-buffer layout.[^log-format]

- Limits: `MAX_VALUES` = 8 conversions, `MAX_BYTES` = 64 buffer bytes, `MAX_WIDTH` = 99 for width and
  precision. `%%` is a literal percent.
- Conversion syntax: `%` [flags `-` `+` space `0` `#`] [width] [`.` precision] conversion.

| conversion | buffer size | argument |
| --- | --- | --- |
| `%d` `%u` `%x` | 4 | 32-bit integer |
| `%lld` `%llu` `%llx` | 8 | 64-bit integer |
| `%e` `%f` `%g` | 8 | float64 |
| `%he` `%hf` `%hg` | 4 | float32 |
| `%{B}` | 4 | boolean; only the `-` flag and a width are allowed |
| `%{E:n}` | 4 | value looked up in enum table `n`; only `-` and a width; `n` must be < the image's enum-table count |

- `#` with `d` or `u` is rejected ("undefined in C99").
- **Buffer layout** (`LogFormat.Layout`): values in order, each aligned to its own size; `Offsets[i]`
  is where value *i* goes and `Size` the total. `FormatInfo(Values, BufferBytes)` is what the reader
  keeps per format string (`KvmImage.Formats`, keyed by string index).
- Rejections (reason text): "format ends in '%'", "format ends inside a conversion", "a width or
  precision over 99", "unterminated '%{'", "unknown conversion", "unknown conversion '%{…}'",
  "'%{…}' takes only the '-' flag and a width", "%{E:n} names a missing enum table", "more than 8
  values", "buffer over 64 bytes".

# What is baked into an image

| baked value | where | compared at load with | mismatch |
| --- | --- | --- | --- |
| format version (1) | header +4 | constant | `version` |
| **service table version** | header +32 | `tables.ServiceTableVersion` = `(ushort)ServicesData.Version` — `services.json` `"version"`, stock **1** | `service-table` |
| **message catalogue version** | header +34 | `tables.MessageCatalogueVersion` = `(ushort)MessagesData.Version` — `messages.json` `"version"`, stock **4** | `message-catalogue` |
| **layout fingerprint** | header +28 | `layout.Fingerprint` — CRC-32 of the layout's canonical text (one `block name type offset[ oneshot]` line per field, then `sensor_size N`, `command_size N`) | `layout` |
| **cost-table hash** | header +24 | `part.Costs.Hash` — CRC-32 of the core's canonical cost text (`category=cycles` or `category=base+per_word`, `,S` for soft, then `opcode:NAME=cycles[,S]` override lines) | `part` |
| core class, max return entries, max operand cells, clock, RAM, program memory, init budget, NV bytes, features | header | the `PartDescription` | `part` |
| tick rate, budget | header | allowed tick list; `clock_hz / tick_hz` | `tick-hz`, `budget` |
| claims | header +46 | (read by the host's ownership logic) | — |
| **service ids** | `SYSCALL` immediates in code | each must be a key of `tables.Services` | `syscall` |
| **message ids** and body sizes | Messages section | id in `tables.MessagesData.Messages`; size = `MessageLayout.BodySizeOf(tables, layout, id)` | `messages` |
| I/O field addresses | absolute immediates in code | not checked individually — only through the layout fingerprint | — |
| enum values, trap codes | plain immediates in code | not checked | — |
| string indices | immediates in code | a log call with an index that is not a format traps at run time | — |
| debug version (1) | Debug section | constant | `debug` |

Not baked and not checked: the `version` of `opcodes.json`, `traps.json`, `enums.json`, `events.json`,
`sysblock.json`, `costs.json` (covered indirectly by the cost hash), `catalogue.json` (covered by the
part fields) and `diagnostics.json`. There is no single "tables version". `KSA.Kfs.Vm.ServiceIds`
declares `TABLE_VERSION = 1` and the `SVC_*` ids as C# constants; being `const`, they are inlined by
the compiler and no reference to them survives in the decompiled code. `Inferred:` they mirror the
JSON for the game's own `switch` statements; nothing compares `ServiceIds.TABLE_VERSION` with the
JSON at run time.[^game-tables-versions]

# `ImageReader`

```csharp
public static class ImageReader {
    public static LoadResult ReadFile(string path, out byte[]? bytes);      // size check + File.ReadAllBytes
    public static LoadResult Read(byte[] bytes, PartDescription part, GameTables tables, IoLayout layout, out KvmImage? image);
}
public sealed record LoadResult(bool Ok, string Rule, string Message) {
    public static readonly LoadResult Success;        // ("", "") — ToString() == "ok"
    public static LoadResult Fail(string rule, string message);   // ToString() == "<rule>: <message>"
}
```

`Read` stops at the **first** failure. `ArgumentOutOfRangeException`, `IndexOutOfRangeException` and
`OverflowException` escaping the validator are caught and reported as rule `internal`, "validator bound
check missed: <exception type>".[^image-reader-read]

## Every check, in execution order

| # | rule | condition for failure |
| --- | --- | --- |
| 1 | `size` | fewer than 180 bytes; more than 16,777,216 bytes |
| 2 | `magic` | first four bytes are not `KVM1` |
| 3 | `version` | `format_version` ≠ 1 |
| 4 | `checksum` | CRC-32 of everything before the last 4 bytes ≠ the last 4 bytes |
| 5 | `reserved` | flag bits above 2 set; `claims` bits above 0 set; header +76 non-zero; feature bits above 2 set |
| 6 | `directory` | a section offset not a multiple of 4; a field over `int.MaxValue`; an empty section with a non-zero offset; Messages/Tasks/Links/Code size inconsistent with its count or `code_size`; Code or Debug count non-zero; a section before offset 176 or past the checksum; two sections overlap; a non-zero byte in a gap; Debug flag and Debug section disagree |
| 7 | `strings` | table does not end exactly at the section end; kind > 2; invalid UTF-8; count ≠ directory count |
| 8 | `enums` / `reserved` / `names` | tables do not end at the section end; reserved word non-zero; a `name_index` that is not a kind-0 string; count mismatch |
| 9 | `data` | records do not end at the section end; non-zero padding; count mismatch; Σ lengths ≠ `data_init_size` |
| 10 | `code` | an opcode byte with no row in `tables.Opcodes`; a truncated last instruction; code not starting with `ENTER`; a function not ending in `RET` or `JMP` |
| 11 | `reserved` / `tasks` | task reserved word non-zero; kind > 2 |
| 12 | `entry` / `call-target` / `enter` | a task entry not on an instruction boundary; not an `ENTER`; an entry `ENTER` with `ncells > 0` |
| 13 | `jump-target` | a jump target not an instruction boundary, outside the jump's own function, or equal to its function's `ENTER` |
| 14 | `call-target` | a near `CALL` to an address ≥ 0x10000; a call target that is not an `ENTER` |
| 15 | `enter` | frame not a multiple of 8, or `4·ncells > frame` |
| 16 | `ret` | `RET n` with `n > 8` |
| 17 | `debug` | any Debug-section parse or consistency failure |
| 18 | `tasks` | more tasks than `part.MaxTasks`; more than one init or more than one program task; `rate_divisor` 0; `program_share` > 100 or set on a non-program task; task stack limits over the part's; `max_call_depth` 0; link ranges outside Links |
| 19 | `names` | a task `name_index` that is not a kind-0 string |
| 20 | `task-order` | periodic tasks not in ascending `rate_divisor` order |
| 21 | `format` | a kind-1 string that `LogFormat.Parse` rejects |
| 22 | **`syscall`** | a `SYSCALL` whose id is not a key of `tables.Services` — "SYSCALL at 0x…: unknown service id 0x…" |
| 23 | `cost` | an instruction whose cost on this part exceeds `budget_cycles`, or is below 1 |
| 24 | `runtime-lib` | `runtime_lib_size` ≠ the recomputed soft-routine charge |
| 25 | `program-memory` | code + data init + runtime library > `machine_code` |
| 26 | `ram` | `ram_required > machine_ram`; `vm_region_base ≥ machine_ram`; a task RAM stack not 8-aligned or outside `[0x10, vm_region_base)`; a task VM stack not 4-aligned or outside `[vm_region_base, machine_ram]` |
| 27 | `reserved` / `messages` | message reserved bytes non-zero; role 0 or > 3; slot (16 + body) past `machine_ram` |
| 28 | `ram` | non-init task RAM stacks overlap; non-init VM stacks and message slots overlap |
| 29 | `data` | a non-empty data record outside `[0x10, lowest task stack base)` (or `vm_region_base` when there are no tasks) |
| 30 | **`service-table`** | `service_table_version` ≠ `tables.ServiceTableVersion` — "service_table_version differs from the game's" |
| 31 | **`message-catalogue`** | `message_catalogue_version` ≠ `tables.MessageCatalogueVersion` |
| 32 | `messages` | a message id not in `tables.MessagesData.Messages` — "message 0x… not in the catalogue" |
| 33 | `reserved` / `links` | link reserved word non-zero; `message_index` out of range; buffer (16 + body) outside the globals region |
| 34 | `messages` | ids not strictly ascending; `body_size` ≠ the size the catalogue (or, for a per-vehicle message, this vehicle's layout) gives; `slot_offset` not a multiple of 8 |
| 35 | `links` | a buffer address not a multiple of 8; a task's subscription or publication run not starting where the previous run ended; a run not sorted by message index then buffer address; Links has entries no task uses |
| 36 | `messages` | role bits disagree with the links; a published message without exactly one publishing task and one buffer |
| 37 | `cost` | a task's start or completion step (`BusSteps.Of`) over `budget_cycles` |
| 38 | `tick-hz` | `tick_hz` not in `tables.AllowedTickHz` |
| 39 | `budget` | `budget_cycles` ≠ `clock_hz / tick_hz` |
| 40 | **`part`** | core class, cost hash, clock, RAM, program memory, NV bytes, return entries, operand cells, init budget or features differ from the part — "the image was built for another part or cost table; rebuild from source" |
| 41 | **`layout`** | layout fingerprint ≠ `layout.Fingerprint` — "the image was built for another I/O layout; rebuild from source" |

Source for the table: `ImageReader.ReadCore`.[^image-reader-core]

Two consequences of the order: an unknown service id (22) is reported before a service-table version
mismatch (30), and both before a part or layout mismatch (40, 41).

## Checks made by the loaders around the reader

| loader | rule | condition |
| --- | --- | --- |
| `ImageReader.ReadFile` | `size` | file larger than 16,777,216 bytes |
| `KvmMachine.LoadImage(image, layout)` | (all of the above) | calls `ImageReader.Read` with the machine's own part and tables, then allocates RAM and task state[^kvm-machine-load] |
| `HostComputer.Load` | `host-message` | the image publishes a message whose name is in `MessageLayout.HostPublished`[^host-computer-load] |
| `VehicleComputer.Load` | `needs-stock` | the program owns GNC blocks without claiming `ALLOCATION`, or subscribes to a stock-only message without publishing it[^vehicle-computer-load] |
| workbench build | diagnostic `KC-E900` / `KA-E900` | a freshly built image fails `ImageReader.Read` — reported as "the built image fails validation: `<rule>: <message>`"[^workbench-validate] |

See [host computer](/host/host-computer.md) and [vehicle computer](/host/vehicle-computer.md).

# Where an unknown service id is rejected

"Unknown" means: the id is not a key of the `GameTables.Services` dictionary of the `GameTables`
instance doing the checking.

| stage | code | result |
| --- | --- | --- |
| kC source | the parser binds service functions from table rows that have a `kc` signature; a name with no row is an ordinary undeclared function | a kC error before any IR exists — see [kC builtins](/language/kc-builtins-and-intrinsics.md) |
| kC, after lowering | `IrValidator.Check` — "unknown service N" | `KC-E900` internal compiler error[^ir-validator-svc] |
| kASM symbol | `SVC_X` not defined by `<kfs.kinc>` | `KA-E003` unknown symbol |
| kASM / kC, program analysis | `ProgramAnalysis.Analyse` — "SYSCALL N is not a service in the table" | `KA-E101` (kASM) or `KC-E900` (kC)[^analysis-svc] |
| kASM `ImageMode` | no analysis runs | **not rejected at assembly** |
| image load | `ImageReader` rule `syscall` | load fails; the VM keeps its previous state |
| run time | `KvmMachine.IsWiredService(id)` — the id must be in the table **and** its `Kind` must be `request` or `request+result` | otherwise trap `BAD_SYSCALL`[^kvm-machine-wired] |
| game side | the host/game service handler | see [service request handling](/host/service-request-handling.md) and [kFS services, game side](/integration/kfs-services-game-side.md) |

So with stock tables an image calling an unlisted id cannot be produced by the compiler or the normal
assembler, and one produced by other means (`--image` reassembly, hand-built bytes, a build against
modified tables) is refused **at image load**. The run-time trap exists only as a second line for ids
that are listed but not "wired" (`Kind == "query"`); no stock row has that kind — the 20 non-reserved
rows are 18 `request` and 2 `request+result`.

# `KvmImage`

The decoded image the reader hands back (all `required init` properties unless noted).[^kvm-image]

| group | members |
| --- | --- |
| raw | `Bytes`, `Checksum`, `Flags`, `HasDebug` (bit 0), `Double32` (bit 2) |
| header | `CodeSize`, `DataInitSize`, `RuntimeLibSize`, `RamRequired`, `CostTableHash`, `LayoutFingerprint`, `ServiceTableVersion`, `MessageCatalogueVersion`, `CoreClass`, `MaxReturnEntries`, `MaxOperandCells`, `ClockHz`, `TickHz`, `MachineRam`, `MachineCode`, `BudgetCycles`, `InitBudgetCycles`, `NvBytes`, `Features`, `VmRegionBase`, `Claims`, `ClaimsAllocation` |
| tables | `Tasks`, `Messages`, `Links`, `Strings`, `Enums`, `Data` |
| code | `CodeOffset`, `Code` (span), `Instrs`, `IndexOfPc` (pc → instruction index, −1 off-boundary; one extra entry for `code_size`), `Functions`, `FunctionOfInstr(index)` |
| formats | `Formats` — string index → `FormatInfo(Values, BufferBytes)` |
| debug | `DebugOffset`, `DebugSize`, `Debug` (`DebugInfo?`) |

`Instr { byte Op; uint Pc; long A; int B; int Cost; short Soft; }` is pre-decoded: `A`/`B` are the
immediates (for jumps and calls `A` is rewritten to the target's instruction index), `Cost` is the
instruction's cycle cost on this part, `Soft` is the cost-category index when the instruction is a soft
routine on this core, else −1.

# `ImageBuilder`

```csharp
public sealed class ImageBuilder(PartDescription part, GameTables tables, IoLayout layout) {
    public ushort TickHz { get; set; } = 50;   public bool Double32 { get; set; }   public bool FarCalls { get; set; }
    public ushort Claims { get; set; }         public uint VmRegionBase { get; set; }   public uint RamRequired { get; set; }
    public List<TaskEntry> Tasks { get; }      public List<MessageEntry> Messages { get; }   public List<LinkEntry> Links { get; }
    public List<StringEntry> Strings { get; }  public List<EnumTable> Enums { get; }         public List<DataRecord> Data { get; }
    public byte[] Code { get; set; }           public byte[]? Debug { get; set; }            public PartDescription Part { get; }
    public uint AddString(byte kind, string text);                 // returns the new string's index
    public byte[] Build();
    public static uint RuntimeLibCharge(GameTables tables, CoreCostTable costs, ReadOnlySpan<byte> code);
    public static void SetChecksum(byte[] image);                  // rewrites the trailing CRC in place
    public static byte[] Patch(byte[] image, Action<byte[]> edit); // clone, edit, re-checksum
    public static int SectionOffset(byte[] image, int section);    // directory offset of a section
}
```

`Build` performs **no validation**; it serialises whatever it was given and stamps the part, tables
and layout identity into the header.[^image-builder] `RuntimeLibCharge` walks the code, collects the
soft-routine families of every instruction that is soft on the core, and sums their `FamilyBytes`.

# Modding notes

- **Images are tied to the tables they were built with through four values:** the two version words,
  the layout fingerprint and the cost hash. A mod that *adds* services or messages and leaves
  `ServicesData.Version` / `MessagesData.Version` unchanged keeps every stock-built image loadable;
  images that use the additions load only where the additions are present (rule `syscall` or
  `messages` otherwise) — a clean failure with a readable message.
- **Bumping a version is the "break all old images" switch.** Changing `ServicesData.Version` (a
  `record` property; `ServicesData` itself is an `init`-only property on `GameTables`) makes every
  image built before the change fail `service-table`. Do it only if the meaning of existing ids
  changes.
- **`GameTables.Services` has a public getter returning a mutable `Dictionary<ushort, ServiceInfo>`**
  (the setter is private). `ImageReader`, `IrValidator`, `ProgramAnalysis`, `CostModel` and the VM all
  consult that dictionary, so one `tables.Services[id] = new ServiceInfo(…)` on the instance the game
  uses makes an id valid at compile, load and run time. The `ServiceInfo.Kind` must be `request` or
  `request+result` or the VM traps; `CostClass` must be `Light`, `Medium` or `Heavy`. The route that
  was actually run replaces the instance with augmented tables instead of mutating it (kASM symbols
  and kC bindings read the separate `ServicesData` row list) — see
  [adding a service](/modding/adding-a-service.md).
- **Timing and identity matter.** The check uses the `GameTables` passed to each call. The workbench,
  each machine and the compiler must all see the same mutated instance, and it must be mutated before
  the first build or load. Which instance the game uses is in
  [game tables loader](/tables/game-tables-loader.md).
- **I/O layout changes are the expensive kind.** Any new sensor or command field changes the layout
  CRC; every image for that vehicle must be rebuilt, and rule `layout` enforces it.
- **Mods can post-process images safely** with `ImageBuilder.Patch`, which fixes the CRC — but the
  result still has to pass every reader check.
- `ImageReader`, `ImageBuilder`, `KvmImage` and all entry records are public; `ImageReader` is static
  and stateless, so a Harmony postfix on `ImageReader.Read` (or a prefix that swaps `tables`) is a
  single choke point for load-time policy.

[^image-format]: ImageFormat.cs:5-68
[^crc32]: Crc32.cs:5-40; ImageBuilder.cs:225-228; ImageReader.cs:74-79
[^image-builder-header]: ImageBuilder.cs:178-204; ImageReader.cs:54-103
[^image-builder-dir]: ImageBuilder.cs:87-177, 205-212
[^image-builder]: ImageBuilder.cs:11-253
[^emitter-strings]: Emitter.cs:350-362
[^log-format]: LogFormat.cs:8-297
[^game-tables-versions]: GameTables.cs:69-71, 171-196, 387, 392-419, 830-851; ServiceIds.cs:3-46
[^image-reader-read]: ImageReader.cs:29-52; LoadResult.cs:3-20
[^image-reader-core]: ImageReader.cs:54-851
[^kvm-machine-load]: KvmMachine.cs:1881-1903
[^host-computer-load]: HostComputer.cs:302-320
[^vehicle-computer-load]: VehicleComputer.cs:313-328; Ownership.cs:48-66
[^workbench-validate]: Workbench.cs:551-570
[^ir-validator-svc]: IrValidator.cs:93-103; Compiler.cs:108-113
[^analysis-svc]: ProgramAnalysis.cs:143-157
[^kvm-machine-wired]: KvmMachine.cs:1122-1128, 2248-2258
[^kvm-image]: KvmImage.cs:7-114; Instr.cs:3-16; FunctionRange.cs:3; FormatInfo.cs:3
