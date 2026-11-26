---
type: Subsystem Reference
title: kVM memory map, sensor/command windows and the system block
description: The kVM address space — RAM regions, the read-only sensor window, the command window, the 256-byte system block (sysblock.json in full) — and the bounds, alignment and write checks applied to every access.
resource: ksa-decomp:KSA.Kfs.Vm/KvmMachine.cs
tags: [kfs, vm, kvm, memory-map, sysblock, sensors, commands]
sources:
  - id: kvm-machine
    resource: ksa-decomp:KSA.Kfs.Vm/KvmMachine.cs
    title: KvmMachine (decompiled) — Resolve, RefreshSystemBlock, latch
  - id: image-format
    resource: ksa-decomp:KSA.Kfs.Vm.Image/ImageFormat.cs
    title: ImageFormat constants (decompiled)
  - id: image-reader
    resource: ksa-decomp:KSA.Kfs.Vm.Image/ImageReader.cs
    title: ImageReader (decompiled) — RAM layout rules
  - id: sysblock-json
    resource: ksa-decomp:KSA.Kfs.Vm.Tables.Data.sysblock.json
    title: sysblock.json (embedded)
  - id: sysblock-cs
    resource: ksa-decomp:KSA.Kfs.Vm/SysBlock.cs
    title: SysBlock offset constants (decompiled)
  - id: io-layout
    resource: ksa-decomp:KSA.Kfs.Vm.Tables/IoLayout.cs
    title: IoLayout and IoField (decompiled)
  - id: builtin-includes
    resource: ksa-decomp:KSA.Kfs.KAsm/BuiltinIncludes.cs
    title: BuiltinIncludes (decompiled) — sys.*, sensors, commands constants
  - id: host-computer
    resource: ksa-decomp:KSA.Kfs.Host/HostComputer.cs
    title: HostComputer (decompiled)
generated: { by: claude-code/claude-fable-5-2, at: 2026-11-25T21:46:43Z }
status: stable
ksa_build: "2026.11.25.5949"
---

# Summary

A kVM program sees a flat 32-bit address space with four mapped regions; everything else traps
`BAD_ADDRESS`. Code is **not** in the address space (it cannot be read or written), and neither are the VM's
own operand and return stacks, although those physically live at the top of the RAM array.[^kvm-machine]

| Region | Address range | Backing array | Program access | Size |
| --- | --- | --- | --- | --- |
| Null guard | `0x00000000`–`0x0000000F` | — | none: `NULL_ACCESS` | 16 bytes (`ImageFormat.NULL_GUARD`) |
| RAM (globals + task RAM stacks) | `0x00000010` … `image.VmRegionBase − 1` | `_ram` | read / write | up to the part's RAM |
| VM region (operand + return stacks, bus slots) | `image.VmRegionBase` … `MachineRam − 1` | `_ram` | none: `BAD_ADDRESS` | rest of the part's RAM |
| Sensor window | `0x80000000` … `0x8000FFFF` | `_sensors` | read only | `layout.SensorSize` (≤ 64 KB; 0 for a vehicle layout in this build) |
| Command window | `0x90000000` … `0x9000FFFF` | `_cmd` | read / write | `layout.CommandSize` (≤ 64 KB) |
| System block | `0xA0000000` … `0xA00000FF` | `_sys` | read only | 256 bytes |
| Non-volatile | `0xB0000000` (`ImageFormat.NV_BASE`) | — | `Not active in this build:` unmapped → `BAD_ADDRESS` | — |

The base addresses are the `ImageFormat` constants `SENSOR_BASE = 2147483648`, `COMMAND_BASE = 2415919104`,
`SYSTEM_BASE = 2684354560`, `NV_BASE = 2952790016`, with `WINDOW_SIZE = 65536` and
`SYSTEM_SIZE = 256`.[^image-format] `KvmMachine.Resolve` uses the literal values.

# How an access is checked — `Resolve`

Every load, store, `COPY` and `ZERO` (and the `log` service's argument buffer) goes through
`private Span<byte> Resolve(uint addr, uint size, bool write, uint align)` (`KvmMachine.cs:488-550`). The
checks run in this order:

1. `size == 0` → returns an empty span **without any check** (so `COPY 0` / `ZERO 0` never trap).
2. `addr < 16` → `NULL_ACCESS`.
3. Region selection, with `end = addr + size` computed in 64 bits:
   - `end <= image.VmRegionBase` → RAM, offset `addr`, writable;
   - `addr >= 0x80000000 && end <= 0x80010000` → sensors, offset `addr − 0x80000000`, read-only, bounded by
     the sensor array length;
   - `addr >= 0x90000000 && end <= 0x90010000` → commands, offset `addr − 0x90000000`, writable, bounded by
     the command array length;
   - `addr >= 0xA0000000 && end <= 0xA0000100` → system block, read-only. Before returning, the current
     cycle count is written to offset 0 so `sys.cycles` is live (`:531`);
   - anything else → `BAD_ADDRESS`. This is where an access that touches the VM region, straddles a region
     boundary, or targets the unimplemented NV window ends up.
4. `write` to a read-only region → `READ_ONLY`.
5. Sensor / command only: `offset + size > array length` → `BAD_ADDRESS` (past the generated I/O layout).
6. `align > 1 && addr % align != 0` → `MISALIGNED`.

Alignment used by the callers: 4 for 32-bit loads/stores, 2 for 16-bit, 1 for bytes and block operations, 8
for all 64-bit accesses and for the `log` buffer (`:906-927, 934-942, 575-583, 1157`).

`Load32`/`Store32` have an inlined fast path for RAM: `addr >= 16`, `(addr & 3) == 0` and
`addr + 4 <= VmRegionBase` read or write the array directly; everything else falls back to `Resolve`
(`:553-573`). The outcome is identical.

The host-side accessors (`ReadRam`, `WriteRam`, `ReadCommandBlock`, `WriteCommandBlock`, `ReadSensorBlock`,
`ReadSystemBlock`) bypass `Resolve` and index the arrays directly (`:2042-2070`).

# RAM layout

`_ram` is `image.MachineRam` bytes, which the loader requires to equal the part's `RamBytes`
(`ImageReader.cs:773`).[^image-reader] The loader fixes this layout:

| Sub-region | Range | Loader rule (`LoadResult.Rule`) |
| --- | --- | --- |
| Null guard | `[0, 0x10)` | — |
| Globals | `[0x10, G)` where `G` = lowest task `StackBase` (or `VmRegionBase` with no tasks) | `data`: every initialised data record lies inside it; `links`: every bus link buffer (16 + body bytes, 8-aligned) lies inside it |
| Task RAM stacks | `[StackBase, StackBase + StackSize)` per task | `ram`: 8-aligned, inside `[0x10, VmRegionBase)`; non-`init` tasks' stacks may not overlap each other |
| VM region | `[VmRegionBase, MachineRam)` | `ram`: `ram_required <= machine_ram` and `vm_region_base < machine_ram` |
| — per-task VM stacks | `[VmStackBase, VmStackBase + 4·MaxOperandCells + 8·MaxCallDepth)` | `ram`: 4-aligned, inside the VM region |
| — bus slots | `VmRegionBase + SlotOffset`, `16 + BodySize` bytes each, `SlotOffset % 8 == 0` | `messages`: inside the VM region; no overlap with VM stacks or other slots |

`Overlaps` ignores ranges belonging to the `init` task (`ImageReader.cs:875-889`), so the `init` task's
stacks may share memory with another task's — it only runs before the others start.

`Reset` zeroes all of RAM and then copies each `DataRecord` (address + bytes) into place
(`KvmMachine.cs:1958-1963`). Parts' RAM sizes are in [processor catalogue](/tables/catalogue-processors.md)
(2,048 bytes for the `K8` Sequencer up to 262,144 for the Mk3).

# Sensor window (`0x80000000`)

The sensor block is a byte array whose layout comes from the `IoLayout` the image was loaded with
(`IoLayout.SensorFields`, each an
`IoField(Name, Type, Offset, Size, OneShot)`).[^io-layout] The host overwrites the **whole** block before
every tick with `WriteSensors`, which throws unless the span length equals `layout.SensorSize`
(`KvmMachine.cs:2028-2035`, called from `HostComputer.cs:419`).[^host-computer] A field's address is `0x80000000 + Offset`. Programs cannot write it
(`READ_ONLY`), and reading past `SensorSize` traps `BAD_ADDRESS` even though the window is 64 KB wide.

`Not active in this build:` **vehicles have no sensor block.** `LayoutGenerator.Vehicle` builds a layout
with no sensor fields and `SensorSize` 0 (`KSA.Kfs.Host/LayoutGenerator.cs:82-84`), so on a vehicle the
block is zero-length, the host passes an empty array that nothing fills (`VehicleComputer.cs:331, 570`), and
any read of the window traps `BAD_ADDRESS`; every program input on a vehicle is a host-published message.
Only the bench layout has sensor fields (`imu.*`, 32 bytes), and `BenchMachine` writes them as all zeros
(`BenchMachine.cs:51, 97`).

How the layout is generated and what the fields mean is in
[I/O layout generation](/host/io-layout-generation.md) and [I/O layout tables](/tables/io-layout-tables.md).

# Command window (`0x90000000`)

The command block (`_cmd`) is ordinary read/write memory for the program. Its contents **persist from tick
to tick**; the VM changes it only in two ways (`KvmMachine.cs:2181-2198`):

- **Latch.** At the end of a tick that neither overran nor trapped, the whole block is copied to
  `_latched`. The host reads only the latched copy (`ReadCommands`). After an overrun or a trap the latched
  copy keeps its previous contents ("commands held").
- **One-shot fields.** After the latch decision, on every executed tick, a 32-bit zero is written at the
  offset of every command field whose `OneShot` flag is set (`IoLayout.OneShotFields`). A one-shot command
  (for example a thruster pulse) therefore has to be rewritten each tick it should apply.

`Reset` clears both the live and the latched block (`:1964-1965`).

# System block (`0xA0000000`)

A 256-byte read-only block describing the machine. `sysblock.json` (version 1, `size` 256) defines 16
fields; bytes 68–255 are reserved and read 0.[^sysblock-json]

| Offset | Address | Name | Type | Meaning (table text) |
| --- | --- | --- | --- | --- |
| 0 | `0xA0000000` | `cycles` | `uint32` | Cycles used so far in the current tick |
| 4 | `0xA0000004` | `budget` | `uint32` | Cycle budget per tick |
| 8 | `0xA0000008` | `tick` | `uint32` | Low 32 bits of the grid index k (below) |
| 12 | `0xA000000C` | `t_s` | `float` | Seconds since reset, `k / tick_hz` (float32; loses precision over long missions) |
| 16 | `0xA0000010` | `dt_s` | `float` | Base tick period |
| 20 | `0xA0000014` | `overruns` | `uint32` | Overrun count since reset |
| 24 | `0xA0000018` | `task_id` | `uint32` | Index of the running task in the image's Tasks[] (§7) |
| 28 | `0xA000001C` | `task_dt_s` | `float` | Period of the running task: `dt_s` × its rate divisor for a periodic task; `dt_s` for `init` and `program` |
| 32 | `0xA0000020` | `wake_reason` | `uint32` | Event that last woke the machine, 0 after reset; values are the flight interface spec's event ids (`events.json`, FI §10; P-018) |
| 36 | `0xA0000024` | `tier` | `uint32` | The part's `part_index` (§2.2) |
| 40 | `0xA0000028` | `core` | `uint32` | Core class, numbered as §2.2 |
| 44 | `0xA000002C` | `soi_changes` | `uint32` | Parent-body changes since reset |
| 48 | `0xA0000030` | `met_us` | `int64` | Microseconds since reset, `floor(k · 10⁶ / tick_hz)` |
| 56 | `0xA0000038` | `nv_writes` | `uint32` | Non-volatile writes since reset, if present |
| 60 | `0xA000003C` | `reset_cause` | `uint32` | Cause of the last reset: 0 power-on, 1 player reset, 2 watchdog, 3 load (`kFS_ksa_integration_spec.md` §2.6) |
| 64 | `0xA0000040` | `rejected_requests` | `uint32` | Service requests rejected for bad argument values since reset: by the VM at issue, or by the host when it applies them (§4.8; D-116 (11)) |

`"reserved": { "from": 68, "to": 255, "meaning": "Reads return 0" }`.

## Who writes each field

`RefreshSystemBlock(task)` clears the block and rewrites it **at the start of every task run** (fresh or
resumed), so the values describe the task that is about to execute (`KvmMachine.cs:463-486, 678`).
Three fields are also updated mid-task.

| Offset | Name | Value written | Notes for this build |
| --- | --- | --- | --- |
| 0 | `cycles` | `_cycles` | Also refreshed on every program access to the system block (`Resolve`, `:531`), so it is exact at the reading instruction (including that instruction's own cost, which is charged before it executes). |
| 4 | `budget` | `_budgetCycles` = `clock_hz / tick_hz` | |
| 8 | `tick` | `(uint)k` | `k` is whatever the host passed to `SetSystem`. |
| 12 | `t_s` | `(float)(k / tick_hz)` | |
| 16 | `dt_s` | `(float)(1 / tick_hz)` | |
| 20 | `overruns` | `_overruns` | As of the task start. |
| 24 | `task_id` | task index in `image.Tasks` | |
| 28 | `task_dt_s` | `RateDivisor / tick_hz` for a periodic task, `1 / tick_hz` for `init` and `program` | |
| 32 | `wake_reason` | `_wakeReason` | `Not active in this build:` only `Wake()` sets it and nothing calls `Wake()`, so it is always 0. |
| 36 | `tier` | `_part.PartIndex` | Index of the part in `catalogue.json` `parts` (the bench part keeps the index of the part it is based on). |
| 40 | `core` | `_part.CoreClass` | 0 `K8`, 1 `K32`, 2 `K32F`, 3 `K32D`. |
| 44 | `soi_changes` | `_soiChanges` | The host always calls `SetSystem(k, 0)` (`HostComputer.cs:418`), so this is always 0 in this build. |
| 48 | `met_us` | `(long)(k · 1,000,000 / tick_hz)` (integer division) | |
| 56 | `nv_writes` | *never written* | `Not active in this build:` always 0. |
| 60 | `reset_cause` | `_resetCause` | 0 power-on, 1 player, 2 watchdog, 3 load; see [VM enums](/vm/vm-enums.md). |
| 64 | `rejected_requests` | `_rejected` | Updated immediately when a `SYSCALL` is rejected or dropped in the same task (`:1136, 1171`); a host-side rejection (`CountRejectedRequest`) becomes visible at the next task start. |

`SysBlock` is a static class of `public const int` offsets with the same numbers (`SIZE = 256`,
`CYCLES = 0`, `BUDGET = 4`, `TICK = 8`, `T_S = 12`, `DT_S = 16`, `OVERRUNS = 20`, `TASK_ID = 24`,
`TASK_DT_S = 28`, `WAKE_REASON = 32`, `TIER = 36`, `CORE = 40`, `SOI_CHANGES = 44`, `MET_US = 48`,
`NV_WRITES = 56`, `RESET_CAUSE = 60`, `REJECTED_REQUESTS = 64`).[^sysblock-cs] `RefreshSystemBlock` uses
literal offsets, so the constants document the layout rather than drive it.

## How programs name these addresses

The kASM built-in include `kfs.kinc` defines `sys` = `0xA0000000` and one constant `sys.<name>` =
`0xA0000000 + offset` per `sysblock.json` field; `vehicle.kinc` defines `sensors` = `0x80000000` and
`commands` = `0x90000000` plus a struct type for each block — each only when the layout has fields in that
block, so a vehicle layout yields no `sensors` symbol (`BuiltinIncludes.cs:47-51, 68-75`).[^builtin-includes]
The kC view of the same data is in [generated headers](/language/kc-generated-headers.md).

# Modding notes

- **The system block cannot be extended from a table.** `sysblock.json` is documentation and include-file
  input only; the values are written by hard-coded offsets in `RefreshSystemBlock`. Bytes 68–255 are free,
  but anything a mod writes into `_sys` (a private field) is wiped at the next task start, and only
  `RefreshSystemBlock`/`Resolve` write it.
- **The sensor block is not a usable read channel on vehicles in this build.** Its size and field list come
  from the generated `IoLayout`, whose fingerprint is baked into every image (`layout` rule) and into saved
  state, and a vehicle layout has no sensor fields. Adding some means changing layout generation (which
  changes the fingerprint of every image) *and* adding a per-tick writer, since the stock vehicle host
  fills nothing; see [I/O layout generation](/host/io-layout-generation.md). New game state reaches a
  program more cheaply as a service result — see [adding a service](/modding/adding-a-service.md).
- **The command block is the cheapest write channel**, with the same layout constraint. `OneShot` is just a
  flag on an `IoField`; the VM zeroes 4 bytes at such an offset each tick regardless of the field's declared
  size.
- A mod can read or write machine memory at any time through the public raw accessors
  (`ReadRam`/`WriteRam`/`WriteCommandBlock`/…), which skip the read-only and VM-region protections. They are
  not synchronised with `RunTick`.
- A service handler that receives a `ptr` argument can dereference it with `ReadRam` only for RAM addresses
  (`0x10 … VmRegionBase`); it must do its own range check, because `ReadRam` will happily read the VM region
  and throws a .NET exception out of range.

[^kvm-machine]: KvmMachine.cs:73-81 (backing arrays), 463-486 (`RefreshSystemBlock`), 488-550 (`Resolve`), 553-583 (`Load32`/`Store32`/`Load64`/`Store64`), 1958-1965 (reset), 2028-2070 (host accessors), 2181-2198 (latch and one-shot zeroing).
[^image-format]: ImageFormat.cs:53-65.
[^image-reader]: ImageReader.cs:479-480 (globals region bound), 590-608 (RAM and stack rules), 609-641 (slots, overlap, data records), 666-676 and 694-700 (link buffers), 773 (part match), 875-889 (`Overlaps`).
[^sysblock-json]: KSA.Kfs.Vm.Tables.Data.sysblock.json:1-24.
[^sysblock-cs]: SysBlock.cs:3-38.
[^io-layout]: IoLayout.cs:6-9; IoField.cs:3.
[^builtin-includes]: BuiltinIncludes.cs:43-51 (service symbols, `sys.*`, `sys`), 64-75 (`vehicle.kinc`: `sensors`, `commands`).
[^host-computer]: HostComputer.cs:416-419 (`SetSystem(k, 0u)`, `WriteSensors`), 434-438 (`ReadCommands` when latched).
