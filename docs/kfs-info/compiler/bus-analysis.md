---
type: Subsystem Reference
title: Bus analysis (static publish/subscribe attribution)
description: How BusAnalysis decides which task reads and writes which message buffer, the rules it enforces (read-only subscriptions, single publisher, ALLOCATION for the command block), the per-task start and completion costs, and how the result becomes the image's Messages and Links tables.
resource: ksa-decomp:KSA.Kfs.Compiler/BusAnalysis.cs
tags: [kfs, compiler, bus, messages, publish, subscribe, allocation, analysis]
sources:
  - id: bus-analysis
    resource: ksa-decomp:KSA.Kfs.Compiler/BusAnalysis.cs
    title: BusAnalysis (decompiled)
  - id: bus-types
    resource: ksa-decomp:KSA.Kfs.Compiler/BusAttribution.cs
    title: BusAttribution, TaskBus, BusAccess (decompiled; BusAttribution.cs, TaskBus.cs, BusAccess.cs)
  - id: ir-bus-decl
    resource: ksa-decomp:KSA.Kfs.Compiler.Ir/IrBusDecl.cs
    title: IrBusDecl (decompiled)
  - id: emitter
    resource: ksa-decomp:KSA.Kfs.Compiler.Backend/Emitter.cs
    title: Emitter (decompiled)
  - id: assembler
    resource: ksa-decomp:KSA.Kfs.KAsm/Assembler.cs
    title: kASM Assembler (decompiled)
  - id: kc-front-end
    resource: ksa-decomp:KSA.Kfs.KC/KcFrontEnd.cs
    title: KcFrontEnd (decompiled)
  - id: message-layout
    resource: ksa-decomp:KSA.Kfs.Vm.Tables/MessageLayout.cs
    title: MessageLayout (decompiled)
  - id: bus-steps
    resource: ksa-decomp:KSA.Kfs.Vm.Image/BusSteps.cs
    title: BusSteps (decompiled)
  - id: host-computer
    resource: ksa-decomp:KSA.Kfs.Host/HostComputer.cs
    title: HostComputer (decompiled)
  - id: vehicle-computer
    resource: ksa-decomp:KSA.Kfs.Host/VehicleComputer.cs
    title: VehicleComputer (decompiled)
  - id: messages-json
    resource: ksa-decomp:KSA.Kfs.Vm.Tables.Data.messages.json
    title: messages.json (embedded table)
generated: { by: claude-code/claude-fable-5-2, at: 2026-11-25T21:29:02Z }
status: stable
ksa_build: "2026.11.25.5949"
---

# The model being analysed

A program talks to the [message bus](/host/message-bus.md) through **buffers**: ordinary globals, one per
declaration, sized and laid out as the message's struct (16-byte header + body).

| language | declaration | effect in the IR |
| --- | --- | --- |
| kASM | `.subscribe MSG_X buf` / `.publish MSG_X buf` | defines the global `buf` (size `MessageLayout.Size`, align 8) and adds an `IrBusDecl`[^assembler-bus] |
| kC | `#pragma kfs subscribe MSG_X buf` / `#pragma kfs publish MSG_X buf` + a file-scope object `kfs_msg_x_t buf;` | adds an `IrBusDecl` after type checks[^kc-front-end-bus] |

```csharp
public sealed record IrBusDecl(bool Publish, ushort MessageId, string Message, string Buffer, Span At, string Prefix);
```

The VM copies the bus slot into each subscription buffer when a task starts and each publication
buffer out to its slot when the task completes. For that to be well defined the compiler has to know,
statically, **which task** uses each buffer. `BusAnalysis` answers that and enforces the ownership
rules.

```csharp
public sealed class BusAnalysis(GameTables tables, PartDescription part, IoLayout layout, DiagnosticBag diags, IrModule module) {
    public Dictionary<string, BusAttribution> Functions { get; }              // per function
    public Dictionary<string, TaskBus> Tasks { get; }                         // per task entry
    public List<(MessageLayout Message, byte Role)> Messages { get; }         // the image's Messages[], ascending id
    public bool Ok { get; }
    public bool Checks { get; init; } = true;
    public bool Run(IReadOnlyList<IrFunction> orderedTasks, uint budget);
    public List<BusAccess> Accesses();
}
```

`Emitter.Emit` runs it after program analysis with `budget = part.ClockHz / tickHz` and `Checks =
!AllowRecursion`. With `Checks == false` (`kvm-cli assemble --image`) every error and the 546 warning
are suppressed.[^emitter-bus-run]

# Step 1 — attribution per function

Each function gets a `BusAttribution`:[^bus-types]

| member | meaning |
| --- | --- |
| `Reads` | global symbols the function may read (set) |
| `Writes` | `(Symbol, At)` — global symbols it may write, with the position of each write |
| `CommandWrites` | positions of stores into the command window |
| `Unattributable` | positions of stores through an address the analysis could not tie to a global |
| `Marks` | `(Symbol, At, Write)` — every individual access, for editor markers |

Where it comes from:[^bus-analysis-attr]

- **kC functions** arrive with `IrFunction.BusReads` / `BusWrites` / `CommandWrites` / `BusMarks`
  already computed by the front end's semantic analysis (a command write is a write to the object at
  address `0x90000000`); they are copied over unchanged.
- **kASM functions** (`Language == "kasm"` and no precomputed lists) are abstractly interpreted by
  `AttributeKasm`.
- Any other language gets an empty attribution.

## The kASM abstract interpreter

It simulates the operand stack with one *provenance tag* per cell: a global's name, the special tag
`@commands`, or nothing.[^bus-analysis-kasm]

| instruction | effect on tags |
| --- | --- |
| any instruction with a `Symbol` operand | the symbol is added to `Reads`, plus a read mark |
| `PUSH32 global + off` with `0 ≤ off < max(1, size)` | push the tag `global` |
| `PUSH32 imm` with `0x90000000 ≤ imm < 0x90010000` | push `@commands` |
| other `PUSH32` | push nothing |
| `STG sym` / `STG64 sym` | **write** to `sym`; the stored cell *escapes* (`STG` only) |
| `STG imm` / `STG64 imm` into the command window | **write** to `@commands` |
| `STW` / `STH` / `STB` | value escapes; **write** to the address cell's tag |
| `ST64` | **write** to the address cell's tag |
| `STL` | the stored cell escapes |
| `COPY n` | **write** to the destination's tag; the source's tag (unless `@commands`) is a read |
| `ZERO n` | **write** to the address's tag |
| `ADD` / `SUB` | tag + untagged keeps the tag (`ADD` in either order, `SUB` only tag − untagged); otherwise nothing |
| `INDEX n` | the index is popped; the base keeps its tag |
| `DUP` / `OVER` / `SWAP` | tags follow the cells |
| `CALL` / `CALLF` | every argument cell escapes; results are untagged |
| `SYSCALL` | every argument cell escapes; results are untagged |
| anything else | pops and pushes untagged cells per the opcode table |
| a label some jump targets | all tags are forgotten (heights are kept) |

- **Write** with a global tag → `Writes` + a write mark; with `@commands` → `CommandWrites`; with no
  tag → `Unattributable`.
- **Escape** (an address leaves the analysable stack): `@commands` → `CommandWrites`; a *publication*
  buffer → counted as a write to it; any other global → a read.

So passing a publication buffer's address to a function, or saving it in a local, counts as writing
it; passing a subscription buffer's address counts as reading it.

# Step 2 — per-function rules

For every function, after attribution:[^bus-analysis-rules]

| number | condition | message / hint |
| --- | --- | --- |
| **540** | a write whose symbol is a subscription buffer | "'buf' is a subscription buffer; subscription buffers are read-only" — "Copy into a local." |
| **544** | any command write while the module does not claim `ALLOCATION` | "only the allocation owner may write the command block" — "Add `.claim ALLOCATION`." (kASM) / "Add `#pragma kfs claim ALLOCATION`." (kC) |
| **546** (warning, `KA-W546`) | each unattributable store in a non-library kASM function | "can't prove this store doesn't hit a bus buffer" — "Store through an address based on a named global, or keep bus buffers out of reach of this pointer." |

`ALLOCATION` is claimed when `IrModule.Claims` contains `"ALLOCATION"` (kASM `.claim ALLOCATION`, or
any kC translation unit with a claim pragma).

# Step 3 — which task uses which buffer

For each task entry the analysis takes the set of functions reachable through `CALL`/`CALLF` operands
and unions their reads and writes:[^bus-analysis-tasks]

- `TaskBus.Subs` = subscription declarations whose buffer the task **reads**;
- `TaskBus.Pubs` = publication declarations whose buffer the task **writes**.

A subscription buffer no task reads, or one read only by code no task reaches, is linked to no task
and its message does not appear in the image.

Publication rules, all **error 542**:

| condition | message |
| --- | --- |
| a message has more than one publication declaration | "'buf2' is a second publication buffer for MSG_X" — "The program already publishes it through 'buf1'." |
| no task writes a publication buffer | "no task writes the publication buffer 'buf'" — "A publication buffer must be written by exactly one task; write it, or remove the declaration." |
| more than one task writes it | "only one task may write 'buf'; 'a' and 'b' both do" — "Publish from one task and pass the value to the other through a global or the bus." |

# Step 4 — messages and step costs

`Messages` is built from every task's `Subs`/`Pubs`: one entry per message id, in ascending id order,
with `Role` bit 0 (value 1) = some task subscribes, bit 1 (value 2) = some task publishes. Layouts come
from `MessageLayout.All(tables, layout)`, i.e. from `tables.MessagesData.Messages`.[^bus-analysis-messages]

Per task (`TaskBus`):

| field | value |
| --- | --- |
| `SubCopies` | Σ `BusSteps.CopyCost(layout.Size)` over `Subs` |
| `PubCopies` | Σ `BusSteps.CopyCost(layout.Size)` over `Pubs` |
| `Start` | `BusSteps.CallCost` + `SubCopies` |
| `Completion` | cost of `RET` (or max(`RET`, `YIELD`) for a program task) + `PubCopies` |

**Error 718** if `Start > budget`: "task 't''s start step (the task call and its subscription copies)
costs N cycles; a tick has B" — "Subscribe to fewer or smaller messages in this task, or lower
tick_hz." — and likewise for `Completion` ("its return and its publication copies").

The emitter adds `SubCopies + PubCopies` to each task's WCET and writes the image tables from
`Messages` and `Tasks` — see [backend emitter](/compiler/backend-emitter.md#bus-messages) and
[kVM image format](/compiler/kvm-image-format.md).

# Editor markers: `Accesses()`

```csharp
public sealed record BusAccess(string File, int Line, int Col, int EndCol, ushort MessageId,
                               string Message, string Buffer, bool Write);
```

`Accesses()` turns marks into a de-duplicated list sorted by file, line, column, then reads before
writes. A mark is kept only if its symbol is a declared buffer of the matching direction (read ↔
subscription, write ↔ publication) and neither the function nor the position is in a library file.
`EndCol` is `Col + symbol.Length` for kC marks and `Col + 1` for kASM marks. The list is attached to
the compile report as `BusAccesses` (not serialised to JSON) and drives the editor's bus gutter
markers.[^bus-analysis-accesses]

# Report shapes

`Emitter.BusReport` condenses the result for the [compile report](/compiler/compile-report.md):[^emitter-bus-report]

| record | fields |
| --- | --- |
| `ReportBus(Text, Subscribes, Publishes, Tasks)` | `Text` = `"none"` or e.g. `"subscribes NAV_STATE; publishes WRENCH_CMD (task slow)"`; message names lose their `MSG_` prefix |
| `ReportBusPublication(Message, Task)` | the publishing task per message |
| `ReportBusTask(Task, Subscribes, Publishes, StartStep, CompletionStep)` | buffer names and the two step costs per task |

# Related checks outside this class

| stage | check | number / rule |
| --- | --- | --- |
| kASM `BusDirective` | malformed directive; unknown message name; publishing a GNC message on the Sequencer | 12; 3; 520 |
| kC `Bus` | unknown message; one buffer in two declarations; Sequencer restriction; no file-scope object; wrong type, or a `double` field under `double_bits = 32` | 3; 16; 520; 3; 541 |
| kC `ServiceOwnership` | a service that needs a stock block the program owns, or any stock-steering service in a program that claims `ALLOCATION` | 501 |
| `ImageReader` | message ids in the catalogue, sizes, sort order, roles vs links, one publisher per message, step costs | load rules `messages`, `links`, `cost` |
| `HostComputer.Load` | the program publishes a message the host publishes (`MessageLayout.HostPublished`: `MSG_FLIGHT_DATA`, `MSG_VEHICLE_PROPS`, `MSG_BURN_STATUS`, `MSG_PILOT_INPUT`, `MSG_FC_SETTINGS`, `MSG_BURN_PLAN`) | load rule `host-message`[^host-computer-load] |
| `VehicleComputer.Load` | owning GNC blocks without `ALLOCATION`, or subscribing to stock-only messages | load rule `needs-stock`[^vehicle-computer-load] |

Note that publishing a host-published message is **not** a compile error; it is refused only at load.
`MessageLayout.SequencerForbidden` = `MSG_NAV_STATE`, `MSG_ATT_TARGET`, `MSG_WRENCH_CMD`.[^message-layout-lists]

Stock message ids (`messages.json`, `version` 4): `0x0101 MSG_NAV_STATE`, `0x0102 MSG_FLIGHT_DATA`,
`0x0201 MSG_ATT_TARGET`, `0x0202 MSG_GUIDANCE_STATUS`, `0x0301 MSG_WRENCH_CMD`,
`0x0401 MSG_VEHICLE_PROPS` (per-vehicle), `0x0402 MSG_BURN_STATUS`, `0x0403 MSG_PILOT_INPUT`,
`0x0404 MSG_FC_SETTINGS`, `0x0405 MSG_BURN_PLAN`. See [messages table](/tables/messages-table.md).

# Modding notes

- **Message lookup is table-driven and uncached.** `MessageLayout.Find`/`All`/`BodySizeOf` rebuild
  layouts from `tables.MessagesData.Messages` on every call. A `MessageRow` a mod appends to that list
  is immediately usable as `.subscribe MSG_NEW buf` / `#pragma kfs subscribe MSG_NEW buf`, is costed,
  and passes `ImageReader`'s catalogue check. (Only the per-vehicle element shapes are cached, in a
  static `ConcurrentDictionary` keyed by group, field spec and a flag.)
- **But field types are a closed set.** `MessageLayout.TypeShape` accepts only `bool`, `uint32`,
  `int64`, `float`, `vec3`, `quat`, `mat3`, `dvec3` and throws `InvalidDataException` otherwise.
- **A host-published message needs host support.** For a program to *subscribe* to a mod's message,
  something must publish it into the slot each tick; the stock list of host-published names is the
  hard-coded `MessageLayout.HostPublished` array plus `VehicleComputer`'s `_hostPublished` ids — see
  [message bus](/host/message-bus.md).
- **`messages.json`'s `version` is in every image header** (`message_catalogue_version`) and compared
  at load. Adding rows without changing `MessagesData.Version` keeps stock-built images loadable.
- **Writing the command block always needs `ALLOCATION`** and the window bounds are literals in this
  class (`InCmd`). A mod that wants programs to drive new actuators without taking allocation should
  use a service or a published message, not new command fields.
- All state is per `BusAnalysis` instance; the class and its result types are public.

[^assembler-bus]: Assembler.cs:893-921
[^kc-front-end-bus]: KcFrontEnd.cs:246-317
[^emitter-bus-run]: Emitter.cs:151-159; BusAnalysis.cs:265-272
[^bus-types]: BusAttribution.cs:6-17; TaskBus.cs:6-21; BusAccess.cs:3
[^bus-analysis-attr]: BusAnalysis.cs:63-101; KcFrontEnd.cs:779-789
[^bus-analysis-kasm]: BusAnalysis.cs:274-539
[^bus-analysis-rules]: BusAnalysis.cs:102-125
[^bus-analysis-tasks]: BusAnalysis.cs:126-188
[^bus-analysis-messages]: BusAnalysis.cs:189-225
[^bus-analysis-accesses]: BusAnalysis.cs:227-263
[^emitter-bus-report]: Emitter.cs:738-765
[^host-computer-load]: HostComputer.cs:302-313
[^vehicle-computer-load]: VehicleComputer.cs:313-324; Ownership.cs:48-66
[^message-layout-lists]: MessageLayout.cs:27-29
