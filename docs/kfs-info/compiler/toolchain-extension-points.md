---
type: Extension Point
title: Toolchain extension points (what a mod must touch so programs can use new services, messages, enums or I/O fields)
description: A consolidated view, from the compiler, assembler and image loader's side, of which symbol tables and checks are derived from GameTables and which are hard-coded, where ids and fingerprints are verified, and the hooks and risks for a mod that adds game features to kFS.
resource: ksa-decomp:KSA.Kfs.Vm.Tables/GameTables.cs
tags: [kfs, extension, modding, compiler, kasm, image, services, messages, harmony]
sources:
  - id: game-tables
    resource: ksa-decomp:KSA.Kfs.Vm.Tables/GameTables.cs
    title: GameTables (decompiled)
  - id: service-info
    resource: ksa-decomp:KSA.Kfs.Vm.Tables/ServiceInfo.cs
    title: ServiceInfo and ServiceRow (decompiled)
  - id: builtin-includes
    resource: ksa-decomp:KSA.Kfs.KAsm/BuiltinIncludes.cs
    title: kASM BuiltinIncludes (decompiled)
  - id: catalogue-enums
    resource: ksa-decomp:KSA.Kfs.Vm.Tables/CatalogueEnums.cs
    title: CatalogueEnums (decompiled)
  - id: message-layout
    resource: ksa-decomp:KSA.Kfs.Vm.Tables/MessageLayout.cs
    title: MessageLayout (decompiled)
  - id: ir-validator
    resource: ksa-decomp:KSA.Kfs.Compiler.Ir/IrValidator.cs
    title: IrValidator (decompiled)
  - id: analysis
    resource: ksa-decomp:KSA.Kfs.Compiler/ProgramAnalysis.cs
    title: ProgramAnalysis (decompiled)
  - id: cost-model
    resource: ksa-decomp:KSA.Kfs.Vm.Image/CostModel.cs
    title: CostModel (decompiled)
  - id: image-reader
    resource: ksa-decomp:KSA.Kfs.Vm.Image/ImageReader.cs
    title: ImageReader (decompiled)
  - id: kvm-machine
    resource: ksa-decomp:KSA.Kfs.Vm/KvmMachine.cs
    title: KvmMachine (decompiled)
  - id: compiler
    resource: ksa-decomp:KSA.Kfs.Compiler/Compiler.cs
    title: Compiler (decompiled)
  - id: kc-parser
    resource: ksa-decomp:KSA.Kfs.KC/Parser.cs
    title: kC Parser (decompiled)
  - id: kc-system-headers
    resource: ksa-decomp:KSA.Kfs.KC/SystemHeaders.cs
    title: kC SystemHeaders (decompiled)
  - id: kc-front-end
    resource: ksa-decomp:KSA.Kfs.KC/KcFrontEnd.cs
    title: KcFrontEnd (decompiled)
  - id: kfs-integration
    resource: ksa-decomp:KSA.Kfs/KfsIntegration.cs
    title: KfsIntegration (decompiled)
  - id: offline-harness
    resource: /modding/offline-harness.md
    title: Offline harness experiments E1–E12
generated: { by: claude-code/claude-fable-5-2, at: 2026-11-25T21:29:02Z }
status: stable
ksa_build: "2026.11.25.5949"
---

# Scope

This page gathers the extension-relevant facts from the other `/compiler/` documents into one place.
It covers only the **toolchain and image path** — getting a program that uses a new feature to
compile, assemble, load and reach the service dispatcher. What the game does with a request, how the
host publishes messages and how layouts are generated are in [/host/](/host/index.md) and
[/integration/](/integration/index.md). The kC-side items, first written from a partial reading of
the front end, have since been checked against `Parser.cs`, `KcFrontEnd.cs` and `SystemHeaders.cs`
and exercised outside the game ([offline harness](/modding/offline-harness.md)).

# The central fact: one `GameTables` instance drives everything

Every toolchain stage receives a `GameTables` object as an argument and reads it live:

| stage | reads |
| --- | --- |
| kC parser | `ServicesData.Services` (rows with a `kc` signature → service bindings), `EventsData` |
| assembler | `OpcodesByMnemonic`, `AllowedTickHz`, `CatalogueData.DefaultTickHz`; `<kfs.kinc>` / `<vehicle.kinc>` via `BuiltinIncludes` |
| `IrValidator`, `ProgramAnalysis`, `BusAnalysis` | `Opcodes`, `Services` |
| `CostModel` | `Services`, the part's `CoreCostTable` |
| `Emitter` / `ImageBuilder` | `Opcodes`, `ServiceTableVersion`, `MessageCatalogueVersion`, `CatalogueData.DefaultTickHz` |
| `ImageReader` | `Opcodes`, `Services`, `MessagesData.Messages`, `ServiceTableVersion`, `MessageCatalogueVersion`, `AllowedTickHz` |
| `KvmMachine` | `Services` (`IsWiredService`), opcode table |

There is no private copy of any table inside the compiler, the assembler or the image reader. The
game's instance is `KfsIntegration.Tables`, created lazily by
`LazyInitializer.EnsureInitialized(ref _tables, GameTables.LoadEmbedded)`.[^kfs-integration]

Two public loaders exist:[^game-tables-load]

```csharp
public static GameTables LoadEmbedded();                                  // manifest resources "KSA.Kfs.Vm.Tables.Data.<file>"
public static GameTables Load(string folder);                             // the same 14 files from a folder
public static GameTables Load(Func<string, Stream?> open, string origin); // any source; runs the full Build() validation
```

`Load` deserialises 14 JSON files and then runs `Build()`, which validates versions, opcodes, costs,
parts, **services** (kind, cell types, cost class, symbol, summary, argument domains), layout, traps
and message ids.

# Table-derived versus hard-coded

| what a program needs | derived from tables? | where |
| --- | --- | --- |
| a service id accepted by validation, analysis, costing, load and the VM | **yes** — key of `GameTables.Services` (`Dictionary<ushort, ServiceInfo>`, public getter, mutable) | `IrValidator`, `ProgramAnalysis`, `CostModel`, `ImageReader`, `KvmMachine.IsWiredService` |
| kASM `SVC_*` symbol | **yes** — `ServicesData.Services` rows, `Symbol` → `Id` | `BuiltinIncludes` (`kfs.kinc`), recomputed per include |
| kC service function binding | **partly** — the binding comes from the row's `kc` signature, but the function must also be declared in a system header and be listed in the hard-coded `SystemHeaders.Builtins` set | `Parser.Bindings`, `SystemHeaders`[^kc-parser-bindings] |
| a message id accepted at assembly/compile and load | **yes** — `MessagesData.Messages` | `MessageLayout.Find/All/BodySizeOf`, `ImageReader` |
| kASM `MSG_*` constant and `kfs_msg_*_t` type | **yes** | `BuiltinIncludes` (`vehicle.kinc`) |
| message field types | **no** — closed set (`bool`, `uint32`, `int64`, `float`, `vec3`, `quat`, `mat3`, `dvec3`) | `MessageLayout.TypeShape` |
| enum constants `KFS_*` | **no** — seven hard-coded groups | `CatalogueEnums.Groups` / `.ServiceGroups` |
| `sys.*`, `TRAP_*` | yes (symbols only) | `BuiltinIncludes` |
| sensor/command field symbols and offsets | **yes**, from the `IoLayout` | `BuiltinIncludes` (`vehicle.kinc`), kC `vehicle.h` |
| `<GROUP>_COUNT` constants | **no** — `thruster`, `engine`, `gimbal` only | `BuiltinIncludes` |
| command-window bounds for the ALLOCATION rule | **no** — literals `0x90000000`…`0x9000FFFF` | `BusAnalysis.InCmd`, `Disassembler` |
| opcodes | table rows, but the VM interpreter is a compiled `switch` over the `Op` enum | not extensible in practice |
| diagnostics numbers | **no** — literals at call sites; the table is documentation | [diagnostics infrastructure](/compiler/diagnostics-infrastructure.md) |

# Where ids and fingerprints are baked and checked

| value | baked as | checked by |
| --- | --- | --- |
| service id | `u16` immediate of `SYSCALL` | `IrValidator` (kC → `KC-E900`), `ProgramAnalysis` (`KA-E101` / `KC-E900`), `ImageReader` rule `syscall`, VM `IsWiredService` (trap `BAD_SYSCALL`) |
| service table version | header +32 | `ImageReader` rule `service-table` |
| message id + body size | Messages section | `ImageReader` rule `messages` |
| message catalogue version | header +34 | `ImageReader` rule `message-catalogue` |
| layout fingerprint | header +28 | `ImageReader` rule `layout` |
| part + cost-table hash | header | `ImageReader` rule `part` |
| enum values, I/O offsets, trap codes, string indices | plain immediates | not checked individually |

Full detail: [kVM image format](/compiler/kvm-image-format.md#what-is-baked-into-an-image).

**Answer to "when is an unknown service id rejected?"** At compile time (kC) and at assembly time
(normal kASM) by the analysis passes; if those are bypassed (`--image` reassembly, a build against
different tables), at **image load** by `ImageReader`; the VM's own run-time check only catches ids
whose `Kind` is `query`.

# Adding a service — toolchain checklist

1. **Make the id valid.** Add a `ServiceInfo` to `tables.Services`:

   ```csharp
   new ServiceInfo(Id, Name, Kind, ArgTypes, ArgCells, ResultCells, CostClass, PerValue, RequiresStock,
                   Domains, ArgumentOf, ArgumentNames, Summary)
   ```

   - `Kind` must be `"request"` or `"request+result"` or the VM traps on the call.[^kvm-machine-wired]
   - `CostClass` must be `"Light"`, `"Medium"` or `"Heavy"`: `CostModel.Cost` indexes
     `ServiceClassCycles[CostClass]` with no fallback.[^cost-model]
   - `ArgCells` / `ResultCells` are what every stage uses for stack accounting; they must match what
     the request handler consumes and returns.
   - `Domains` feed the VM's argument-domain check — see
     [services syscall ABI](/vm/services-syscall-abi.md).
2. **Give kASM a symbol.** Append a `ServiceRow` with `Reserved = false`, `Symbol = "SVC_…"` and the
   same `Id` to `tables.ServicesData.Services` (a mutable `List<ServiceRow>`).
3. **Give kC a binding** (`Inferred:` from the parser's call path): the row's `Kc` text, e.g.
   `` "`kfs_light_set(i, on)`" ``, makes `Parser.Bindings` map `kfs_light_set` to the service; the
   function also needs a prototype in a system header (`SystemHeaders.Files["kfs.h"]` is a public
   mutable dictionary; `CompileInput.SystemFiles` can override headers per compile) and its name in
   `SystemHeaders.Builtins` (a public mutable `HashSet<string>`). See
   [kC builtins and intrinsics](/language/kc-builtins-and-intrinsics.md). `Verified:` run against
   the real `KSA.dll`, a row alone fails with `KC-E003` "'<name>' is not declared", a prototype in a
   `<…>` header without the `Builtins` entry fails with `KC-E304` "'<name>' is declared but never
   defined", and with both the call binds through the row's `kc` column.[^offline-harness]
4. **Leave `ServicesData.Version` alone** unless existing ids change meaning; it is the
   `service_table_version` every image carries.
5. **Handle the request** on the host/game side —
   [service request handling](/host/service-request-handling.md),
   [kFS services, game side](/integration/kfs-services-game-side.md).

Doing steps 1–3 by supplying modified JSON through `GameTables.Load(open, origin)` instead of mutating
objects gets the stock `BuildServices` validation for free and keeps `Services` and `ServicesData` in
step automatically. That is the route the executed experiments took — a postfix on
`GameTables.LoadEmbedded` that returns such tables, never mutating the live instance — and the
complete tested recipe is [adding a service](/modding/adding-a-service.md).[^offline-harness]

# Adding a message

Append a `MessageRow(Id: "0xNNNN", Name: "MSG_…", Section, Fields, PerVehicle)` to
`tables.MessagesData.Messages`. From then on `.subscribe`/`.publish`, `#pragma kfs subscribe/publish`,
`<vehicle.kinc>`'s `MSG_*` and struct type, the compile-time copy costs and `ImageReader`'s catalogue
and size checks all work with it — layouts are rebuilt from the list on every call. Constraints: ids
are `0x` + four hex digits and unique (`CheckMessages` enforces this only at load); field types come
from the closed set above; keep `MessagesData.Version` unchanged. Publishing into the slot, or
consuming what a program publishes, is the host's job — [message bus](/host/message-bus.md).

# Enum constants

`CatalogueEnums.All` cannot be extended by adding a new `enums.json` list: its seven groups are
hard-coded. Options: pass plain integers (kASM `.equ`, a kC header with `#define`s or an `enum`), or
patch `CatalogueEnums.All` with a postfix that appends pairs. The values a program passes are ordinary
immediates; nothing checks them against a table except the VM's per-argument domain check.

# New I/O fields

A new sensor or command field is picked up automatically by `<vehicle.kinc>` and the kC `vehicle.h`
once the `IoLayout` contains it (stock vehicle layouts carry command fields only; the sensor block
exists on the bench alone — see [I/O layout generation](/host/io-layout-generation.md)), but:

- the layout's CRC changes, so **every image built for that vehicle becomes unloadable** (rule
  `layout`) until rebuilt;
- writing any command field requires the program to claim `ALLOCATION` (bus analysis rule 544), which
  makes the program the vehicle's actuator owner;
- only `thruster`/`engine`/`gimbal` groups get `_COUNT` constants and per-vehicle message elements.

For on/off, colour or angle control of parts, a **service** (request with a part index and values) or
a **message** avoids all three costs.

# Hooks

| hook | kind | use |
| --- | --- | --- |
| `GameTables.LoadEmbedded` (public static) | Harmony postfix/prefix | mutate or replace the tables before anything else sees them — the single most complete hook, since the workbench, machines and compiler all take the instance from `KfsIntegration.Tables` |
| `GameTables.Load(Func<string, Stream?>, string)` | public API | build a fully validated tables object from patched JSON |
| `Compiler.KcFrontEnd` | public static settable `Func<IFrontEnd>` | wrap the front end to inspect or edit the `IrModule`; note `Workbench`'s constructor resets it |
| `CompileInput.SystemFiles` | public input | inject or override `<…>` headers for one compile (the stock workbench build leaves it empty) |
| `SystemHeaders.Files`, `SystemHeaders.Builtins` | public static mutable collections | add kC declarations and builtin names globally |
| `BuiltinIncludes` constructor, `CatalogueEnums.All` | Harmony postfix | add kASM constants/types that tables cannot express |
| `ImageReader.Read` | Harmony prefix/postfix | load-time policy, diagnostics, or substituting tables |
| `ImageBuilder.Patch` | public API | post-process an image and fix its CRC |

# Risks and caches

- **Mutate before first use.** `Parser` caches the kC service bindings in a private static dictionary
  keyed by the `GameTables` *reference*; rows added to the same instance after the first kC compile
  are not seen by kC (kASM and all validation see them immediately).[^kc-parser-bindings] `MessageLayout`
  caches only per-vehicle element shapes. `BuiltinIncludes` is uncached.
- **Keep `Services` and `ServicesData.Services` consistent.** They are separate structures after
  `Build()`; validation uses the dictionary, symbols and kC bindings use the row list.
- **Threading.** Builds run on a dedicated thread; machines tick on the simulation's threads. Table
  mutation after start-up is a data race on plain `Dictionary`/`List` objects.
- **Image portability.** An image that calls a mod's service fails to load without the mod (rule
  `syscall`) — safe. An image built *with* the mod but using only stock features loads without it,
  provided the version words were not changed.
- **Game updates.** kFS is a prototype in this build (`KfsPrototype`); every type named here is public
  today, but ids, table schemas and the image format version can change between builds.

[^kfs-integration]: KfsIntegration.cs:85-99 (`Tables` lazy property; `new Workbench(Tables, …)`)
[^game-tables-load]: GameTables.cs:75-127, 171-206, 538-577
[^kc-parser-bindings]: Parser.cs:141-143, 3085-3103, 3415-3421; SystemHeaders.cs:8-30; KcFrontEnd.cs:143-146
[^kvm-machine-wired]: KvmMachine.cs:1122-1128, 2248-2258
[^cost-model]: CostModel.cs:26-37
[^offline-harness]: Experiments E2, E3 and E10 in the offline harness document.
