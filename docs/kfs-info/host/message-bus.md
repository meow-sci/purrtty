---
type: Subsystem Reference
title: Message bus (host side)
description: How the host packs its seven published messages into slot bytes and delivers them to a program each tick, how program publications come back, what BusTracker monitors, and which bus features are not active in this build.
resource: ksa-decomp:KSA.Kfs.Host/HostMessages.cs
tags: [kfs, host, message-bus, messages, bus-tracker, publish, subscribe]
sources:
  - id: host-messages
    resource: ksa-decomp:KSA.Kfs.Host/HostMessages.cs
    title: HostMessages (decompiled)
  - id: host-message
    resource: ksa-decomp:KSA.Kfs.Host/HostMessage.cs
    title: HostMessage (decompiled)
  - id: message-packer
    resource: ksa-decomp:KSA.Kfs.Host/MessagePacker.cs
    title: MessagePacker (decompiled)
  - id: bus-tracker
    resource: ksa-decomp:KSA.Kfs.Host/BusTracker.cs
    title: BusTracker (decompiled)
  - id: bus-row
    resource: ksa-decomp:KSA.Kfs.Host/BusRow.cs
    title: BusRow (decompiled)
  - id: vehicle-computer
    resource: ksa-decomp:KSA.Kfs.Host/VehicleComputer.cs
    title: VehicleComputer (decompiled)
  - id: host-computer
    resource: ksa-decomp:KSA.Kfs.Host/HostComputer.cs
    title: HostComputer (decompiled)
  - id: host-sample
    resource: ksa-decomp:KSA.Kfs.Host/HostSample.cs
    title: HostSample (decompiled)
  - id: ownership
    resource: ksa-decomp:KSA.Kfs.Host/Ownership.cs
    title: Ownership (decompiled)
  - id: kvm-machine
    resource: ksa-decomp:KSA.Kfs.Vm/KvmMachine.cs
    title: KvmMachine (decompiled)
  - id: message-layout
    resource: ksa-decomp:KSA.Kfs.Vm.Tables/MessageLayout.cs
    title: MessageLayout (decompiled)
  - id: image-records
    resource: ksa-decomp:KSA.Kfs.Vm.Image/MessageEntry.cs
    title: MessageEntry, LinkEntry, TaskEntry (decompiled)
  - id: events-json
    resource: ksa-decomp:KSA.Kfs.Vm.Tables.Data.events.json
    title: events.json (embedded resource)
  - id: image-reader
    resource: ksa-decomp:KSA.Kfs.Vm.Image/ImageReader.cs
    title: ImageReader (decompiled)
generated: { by: claude-code/claude-fable-5-2, at: 2026-11-25T21:30:51Z }
status: stable
ksa_build: "2026.11.25.5949"
---

# The bus in one paragraph

A kFS "message" is a fixed-size record: a 16-byte header followed by a body whose layout comes from
the message catalogue ([messages table](/tables/messages-table.md)). Each message an image
subscribes to or publishes has a **slot** in the VM's RAM. A program does not read or write slots
directly: when a task starts, the VM copies each subscribed slot into the task's own buffer; when
the task returns or yields, the VM copies each published buffer into its slot and stamps the
header. The host's part is to (a) write the slots of the messages *it* publishes before each tick
and (b) read back what the program published. In this build exactly one computer exists per
vehicle, so "bus" means the exchange between the host and that one program.

# Catalogue ids the host knows by number

```csharp
public const ushort NAV_STATE = 257;        // 0x0101
public const ushort FLIGHT_DATA = 258;      // 0x0102
public const ushort ATT_TARGET = 513;       // 0x0201
public const ushort GUIDANCE_STATUS = 514;  // 0x0202
public const ushort WRENCH_CMD = 769;       // 0x0301
public const ushort VEHICLE_PROPS = 1025;   // 0x0401
public const ushort BURN_STATUS = 1026;     // 0x0402
public const ushort PILOT_INPUT = 1027;     // 0x0403
public const ushort FC_SETTINGS = 1028;     // 0x0404
public const ushort BURN_PLAN = 1029;       // 0x0405
public const ushort HOST_SOURCE = ushort.MaxValue;   // header source of a host message
public const ushort HOST_FLAGS = 1;                  // header flags of a host message

public static readonly ushort[] Ms5Order  = { 257, 258, 1025 };
public static readonly ushort[] MsG3Order = { 257, 258, 1025, 1026, 1027, 1028, 1029 };
public static bool IsMsG3(ushort id) => id is >= 1026 and <= 1029;
```

[^host-messages]

| id | name | who may publish it | host publishes it? |
| --- | --- | --- | --- |
| 0x0101 | `MSG_NAV_STATE` | host, or a program that owns NAVIGATION | yes, unless the program publishes it |
| 0x0102 | `MSG_FLIGHT_DATA` | host only | yes |
| 0x0401 | `MSG_VEHICLE_PROPS` | host only | yes |
| 0x0402 | `MSG_BURN_STATUS` | host only | yes, when the sample carries it |
| 0x0403 | `MSG_PILOT_INPUT` | host only | yes, when the sample carries it |
| 0x0404 | `MSG_FC_SETTINGS` | host only | yes, when the sample carries it |
| 0x0405 | `MSG_BURN_PLAN` | host only | yes, when the sample carries it |
| 0x0201 | `MSG_ATT_TARGET` | a program that owns GUIDANCE | never |
| 0x0202 | `MSG_GUIDANCE_STATUS` | a program | never |
| 0x0301 | `MSG_WRENCH_CMD` | a program that owns ATTITUDE_CONTROL | never |

"Host only" is enforced at load: an image with the publish role on any name in
`MessageLayout.HostPublished` is rejected with rule `host-message`.[^message-layout][^host-computer]
`Ms5Order` is not referenced anywhere else in the decompiled code.

# Roles come from the image

Each `MessageEntry(ushort MessageId, ushort BodySize, uint SlotOffset, byte Role)` in an image has
role bit 1 = subscribes, bit 2 = publishes. Each `TaskEntry` lists the `LinkEntry(MessageIndex,
BufferAddress)` records of its subscriptions and publications.[^image-records] The VM exposes this
as `KvmMachine.MessageRole(ushort id) → (bool Subscribes, bool Publishes)`.[^kvm-machine]

At `VehicleComputer.Load` the host derives:[^vehicle-computer]

```csharp
_hostPublished = HostMessages.MsG3Order
    .Where(id => { var r = Machine.MessageRole(id); return r.Subscribes && !r.Publishes; })
    .ToArray();
```

So the host delivers a message exactly when it is one of the seven **and** the image subscribes to
it **and** the image does not publish it itself. There is no dynamic subscription: roles are fixed
when the image is built.

# Per tick: from sample to slot bytes

`VehicleComputer.PlayerTick(k, sample)`:[^vehicle-computer]

```csharp
_tickMessages.Clear();
foreach (ushort id in _hostPublished)
    if (sample.Carries(id))
        _tickMessages.Add(new HostMessage(id,
            PackKept(_playerSlots, _hostMessages, id, sample, k, TickHz, Grid.ResetNs)));
HostTickResult result = RunTick(k, _sensors.Bytes, _tickMessages);
```

- `HostMessage` is `readonly record struct HostMessage(ushort Id, byte[] Slot)`.[^host-message]
- `PackKept` keeps one `byte[]` per id in `_playerSlots` (re-allocated only if the size changed)
  and calls `HostMessages.PackInto(slot, id, sample, k, tickHz, soiChanges: 0u, epochNs)`; no
  allocation per tick.
- `HostSample.Carries(id)` is always true for 0x0101, 0x0102 and 0x0401. For 0x0402–0x0405 it is
  true only when the sample's leaf layer (or its `Values` dictionary) has that message; a sample
  without leaves delivers none of them and the VM's slot simply keeps its last contents.[^host-sample]

## `HostMessages.PackInto`

```csharp
public void PackInto(byte[] slot, ushort id, HostSample sample, ulong k, int tickHz,
                     uint soiChanges, long epochNs = 0L)
```

[^host-messages]

1. The slot length must equal the message's size for this layout, otherwise
   `ArgumentException("0x…: a N-byte buffer for a M-byte slot")`.
2. `Array.Clear(slot)` — every delivery starts from zeros.
3. Header: `hdr.seq = Seq(k)`, `hdr.source = 0xFFFF`, `hdr.flags = 1`, `hdr.t_us = MetUs(k, tickHz)`.
   - `Seq(k)` is `(uint)(k + 1)`, or `1` if that wraps to `0` — a host sequence number is never 0.
   - `MetUs(k, tickHz)` is `k * 1_000_000 / tickHz`.
4. Typed body: id 257 → `PackNav`, 258 → `PackFlight`, 1025 → `PackProps`; ids 1026–1029 have no
   typed body; **any other id throws**
   `ArgumentException("0x… is not a host message (integration §7.4)")`.
5. If `sample.Values` has an entry for the id, `MessagePacker.WriteLeaves(layout, values, slot)`.
6. `sample.Leaves?.Apply(id, slot, epochNs)`.

Field offsets are resolved **by leaf path** from the layout the first time each message is packed
(the private `HeaderAt`, `NavAt`, `FlightAt`, `PropsAt` helpers). They throw
`InvalidOperationException("<path> is <type>, not <expected>")` on a type mismatch and
`KeyNotFoundException` when a field the packer expects is missing from the catalogue row — the
typed packers depend on the exact field names of `messages.json`.

Other public members: `Layout(ushort id)`, `Read(ushort id, ReadOnlySpan<byte> slot)` (a
`SlotReader` indexed by leaf path), `Pack(...)` (allocating variant of `PackInto`),
`NewSlot(ushort id)`. The constructor builds `MessageLayout.All(tables, layout)` once.

The full field list, with which layer writes each field, is in
[host sampling and leaves](/host/host-sampling-and-leaves.md).

## Into the VM

`HostComputer.RunTick` hands each message to `machine.WriteMessage(id, slot)`:[^host-computer][^kvm-machine]

```csharp
public void WriteMessage(ushort id, ReadOnlySpan<byte> msg)
```

- throws `InvalidOperationException` when the machine is not booted, or when the image does not
  subscribe to the id or publishes it itself;
- throws `ArgumentException` when `msg.Length != 16 + body_size`;
- otherwise copies the bytes to the slot at `VmRegionBase + SlotOffset` in VM RAM.

Then, inside `KvmMachine.RunTick`, each task that starts or resumes pays its subscription copy cost
and `CopySubscriptions(task)` copies every subscribed slot into that task's buffer at the linked
`BufferAddress`. The program reads its buffer as an ordinary struct (the generated header gives it
a type; see [kC generated headers](/language/kc-generated-headers.md)).

## End to end

```text
game state ──sampler.Sample()──▶ HostSample (typed values + leaves)        once per dispatch
HostSample ──HostMessages.PackInto──▶ byte[] slot (header + body)          per tick, per message
slot ──KvmMachine.WriteMessage──▶ VM RAM slot                              per tick, before tasks
VM RAM slot ──CopySubscriptions──▶ task's subscription buffer              at each task start/resume
buffer ──ordinary loads──▶ the program's variables
```

A program sees data sampled at the start of the dispatch in which its tick runs; with catch-up,
several consecutive ticks see the same sample with advancing `hdr.seq` and `hdr.t_us`.

# The other direction: program publications

When a task returns or yields, the VM's `CopyPublications(task)` copies each published buffer into
its slot and overwrites the header: `seq` is the slot's previous `seq + 1` (wrapping to 1, never
0), `source` is the computer index (0), `flags` is 0, `t_us` is `k * 1_000_000 / tick_hz`. At the
end of a tick that latches, every published slot is copied to a per-message **latched slot** and
folded into the command stream hash.[^kvm-machine]

The host can read a latched publication with:

```csharp
public void ReadMessage(ushort id, Span<byte> msg)   // throws unless the image publishes id
```

In this build the only host-layer caller is the bus monitor. `Not active in this build:` nothing
consumes a program's `MSG_NAV_STATE`, `MSG_ATT_TARGET`, `MSG_GUIDANCE_STATUS` or `MSG_WRENCH_CMD`
to fly the vehicle — there is no stock guidance or allocation computer to receive them (the
`needs-stock` load rule exists for that reason; see
[ownership and control modes](/host/ownership-and-control-modes.md)). A program that publishes them
and claims ALLOCATION drives the actuators itself through the command block.

# Ownership and arbitration

There is no run-time arbitration between publishers. Who publishes a message is settled when the
image loads:

- host-only messages cannot be published by a program (load rule `host-message`);
- `MSG_NAV_STATE` goes to the program if it publishes it (the host then omits it from
  `_hostPublished`), otherwise to the host;
- publishing `MSG_NAV_STATE`, `MSG_ATT_TARGET` or `MSG_WRENCH_CMD` makes the program the owner of
  NAVIGATION, GUIDANCE or ATTITUDE_CONTROL respectively (`Ownership.Of`), which is only accepted
  together with the ALLOCATION claim.[^ownership]

# Staleness

The host has no staleness logic of its own; a slot holds whatever was last written.

- Host messages that are delivered are rewritten every tick, so their `seq` advances by one per
  tick and `t_us` equals the tick's `met_us`.
- A host message that is *not* carried on a tick is not rewritten; the program keeps seeing the
  previous record with its old `seq`/`t_us`, or all zeros (`seq == 0`) if it was never delivered.
- A program can therefore test freshness itself by comparing `hdr.seq` with the last value it saw
  or `hdr.t_us` with `sys.met_us`.

The bus monitor reports age the same way (below).

# `MessagePacker`

A small, layout-driven codec, independent of the typed packers:[^message-packer]

```csharp
public static byte[] Pack(MessageLayout m, IReadOnlyDictionary<string, double> values, uint seq, long tUs);
public static void   WriteLeaves(MessageLayout m, IReadOnlyDictionary<string, double> values, byte[] slot);
public static Dictionary<string, double> Unpack(MessageLayout m, ReadOnlySpan<byte> slot);
public static double Read(MessageLeaf l, ReadOnlySpan<byte> slot);
```

- `Pack` allocates a slot of `m.Size`, writes a **host** header (`seq`, source `0xFFFF`, flags `1`,
  `tUs`) at the fixed offsets 0, 4, 6, 8 and then the given leaves.
- `WriteLeaves` writes each `path → value` by the leaf's type (same conversions as
  `HostLeafValues.Set`, in a `checked` context); an unknown path throws `KeyNotFoundException` via
  `MessageLayout.Leaf`.
- `Unpack` returns every header and body leaf as a `double`; `Read` decodes one leaf (`bool` reads
  as its `uint32`).

`Pack` and `Unpack` work for **any** catalogue message, not only the seven host messages.

# `BusTracker`: the monitor

`BusTracker` is created by `HostComputer.Load` for every image and is purely observational — it
never changes what a program sees.[^bus-tracker][^host-computer]

- **Rows.** One private `Row` per image message (subscribed and/or published), sorted by id, with
  the message layout, a slot-sized byte copy and a one-second history of sequence numbers. The
  constructor throws `InvalidOperationException("message 0x… is not in the catalogue")` if an image
  names an id the tables do not contain — a defensive check, since the image validator has already
  rejected such an image with rule `messages`.[^image-reader] For published rows it records the
  name of the first task that publishes the message.
- **`AfterTick(KvmMachine vm, ulong k, IReadOnlyList<HostMessage> hostMessages)`** runs after every
  tick: published rows are refreshed with `vm.ReadMessage`; other rows are refreshed from the host
  messages delivered this tick (matching id and length). The rate is the growth of `hdr.seq` over
  the last `tick_hz` ticks, scaled to per second.
- **Plots.** `Plot("MSG_NAME.field")` adds a body leaf to the plotted set, at most
  `MAX_PLOTTED_FIELDS = 16` per computer; each tick the leaf's value is appended to the
  `Samples` `TraceStore` under that name. `Unplot`, `CopyPlotted`, `IsPlotted`, `Plotted` manage
  the set; header fields are refused (`header fields aren't plotted; the monitor shows seq, age and rate`).
  `Value(slot, leaf)` is the static decoder.
- **`Rows(computerName, computerIndex, hostPublished, withSlots, slotId)`** builds the snapshot
  rows:

```csharp
public sealed record BusRow(ushort Id, string Name, bool Subscribed, bool Published,
    string Publisher, string? Task, uint Seq, ushort Source, ushort Flags, long TUs,
    long? AgeUs, long? AgeTicks, double Rate)
{
    public byte[]? Slot { get; init; }
}
```

[^bus-row]

| field | value |
| --- | --- |
| `Publisher` | `computer <index> (<name>)` for a published row; `host` if the id is in `hostPublished`; otherwise `none` |
| `Task` | the publishing task's name, for published rows |
| `Seq`, `Source`, `Flags`, `TUs` | the header of the tracked slot copy |
| `AgeUs` | `MetUs(lastK, hz) − TUs`, or `null` while `Seq == 0` |
| `AgeTicks` | `AgeUs` converted to ticks, rounded to the nearest tick |
| `Rate` | records per second over the last second of ticks |
| `Slot` | a clone of the slot bytes, only while a bus lease is active (`HostComputer.WantBus`), optionally for one id only |

`Reset()` clears slots, histories and plotted samples; `HostComputer` calls it on `Start` and `Load`.
The rows are published in `MachineSnapshot.Bus`; the bus monitor view renders them
([views](/host/views.md)).

# Features that exist in the tables but not in this build

- `Not active in this build:` **multi-computer delivery.** Headers carry a `source` computer index
  and the event table speaks of messages "published by another computer", but every `HostComputer`
  constructs its `KvmMachine` with computer index `0`, one `VehicleComputer` exists per vehicle,
  and no code forwards one computer's latched publications into another's slots.[^host-computer]
- `Not active in this build:` **the `MESSAGE` wake event.** `events.json` defines event 8
  (`MESSAGE`, parameter: message id, "wakes when that message is next published by another computer
  or the host").[^events-json] The VM validates the parameter, but `sleep_until_event` is only
  logged by the host (`…requested (sleeping and waking are MS7; the computer keeps running)`), and
  `KvmMachine.Wake(uint reason)` has no caller. See
  [service request handling](/host/service-request-handling.md).
- `Not active in this build:` `HostSample.Values` — honoured by `PackInto`, never set by the game.
- The SOI-change counter in `MSG_NAV_STATE.soi_changes` and the system block is always 0.

# Modding notes

- **Public:** all of `HostMessages` except its private offset helpers; `MessagePacker`;
  `HostMessage`; `BusTracker` (monitor API); `VehicleComputer.Messages`, `.HostPublished`,
  `.LastHostMessages`; `KvmMachine.WriteMessage`, `.ReadMessage`, `.ReadSlot`, `.MessageRole`.
- **The host's publication list is closed.** `MsG3Order` is a `static readonly` array consulted in
  three places (`VehicleComputer.Load`, `HostLeafMap`'s constructor, the game's `KfsHostSampler`),
  `IsMsG3` is a range test, and `PackInto` throws for any other id. Publishing an eighth message
  through the same path means patching `VehicleComputer.Load` (to add the id to the private
  `_hostPublished`) **and** `HostMessages.PackInto` (to pack it), and adding its row to
  `GameTables.MessagesData.Messages`.
- **A shorter path exists:** `KvmMachine.WriteMessage` is public and is all that delivery really
  is. Code running on the dispatch thread before the tick — for example an action passed to
  `HostComputer.Enqueue`, which `Dispatch` drains before any tick — can write a slot built with
  `MessagePacker.Pack(layout, values, seq, tUs)` directly. The image must subscribe to the id (and
  not publish it), the byte length must match, and the machine must be booted, or `WriteMessage`
  throws — check `Machine.IsBooted` and `Machine.MessageRole(id)` first, because an exception in a
  queued action escapes `Dispatch` and faults the computer. A message delivered this way is invisible to the bus monitor's "host" publisher label and
  its tracked copy is not refreshed (the tracker only looks at the tick's `HostMessage` list).
  `Inferred:` a Harmony prefix on `HostComputer.RunTick` that appends to the `messages` argument
  would refresh the tracked copy (not tested); the "host" label would still be missing, because it
  comes from `_hostPublished`.
- **Size.** A slot is `16 + body_size` bytes and `body_size` is a `ushort`: at most 65 535 body
  bytes. Slots live in VM RAM inside the VM region, and every subscribing task has its own
  full-size buffer, so the practical limit is the part's RAM; each copy also costs cycles from the
  tick budget (see [bus analysis](/compiler/bus-analysis.md)).
- **Variable-length data.** The catalogue's only variable-length construct is the per-vehicle array
  `name[i]`, whose count is the number of distinct `name[n]` indices among the layout's command
  fields. Top-level fields cannot be fixed arrays; arrays (`type[n]`) are parsed only inside an
  element type.[^message-layout]
- Exceptions thrown while packing or writing a message propagate out of `Dispatch` and fault the
  vehicle's computer (the stock flight computer takes over).

[^host-messages]: HostMessages.cs — `SlotReader` :13–26; offset helpers :28–279; id constants :338–360; `Ms5Order`/`MsG3Order` :362–364; `IsMsG3` :378–385; constructor :387–391; `Layout`/`Read` :393–401; `Seq` :403–411; `MetUs` :413–416; `Pack`/`NewSlot` :418–428; `PackInto` :430–471.
[^host-message]: HostMessage.cs:3.
[^message-packer]: MessagePacker.cs — `Pack` :11–20; `WriteLeaves` :22–60; `Unpack` :62–70; `Read` :72–84.
[^bus-tracker]: BusTracker.cs — `Row` :14–29; `MAX_PLOTTED_FIELDS` :31; constructor :74–111; `Reset` :113–124; `AfterTick` :126–169; `Rows` :171–213; `Value` :215–226; plot API :228–286; `Find` :288–312.
[^bus-row]: BusRow.cs:3–6.
[^vehicle-computer]: VehicleComputer.cs — `_hostPublished` derived at `Load` :338–342; `PlayerTick` :554–573; `PackKept` :575–583.
[^host-computer]: HostComputer.cs — computer index 0 :222; host-message load rule :305–315; `WriteMessage` per host message :420–423; `BusTracker` created at `Load` :333, reset :534, fed :442, rendered into the snapshot :921.
[^host-sample]: HostSample.cs — `Carries` :27–38.
[^ownership]: Ownership.cs — `Of` :10–37.
[^kvm-machine]: KvmMachine.cs — `LoadBus` :267–303; `CopySubscriptions` :314–322; `CopyPublications` :324–342; `LatchSlots` :344–353; `WriteMessage` :368–384; `ReadMessage` :386–402; `MessageRole` :404–416; `ReadSlot` :418–426; subscription copy at task start/resume :719–723, :762–769; `Wake` :2212–2216.
[^message-layout]: MessageLayout.cs — `HostPublished` :27; `TypeShape` (top-level field types) :137–151; `Element` (array members) :169–228; per-vehicle arrays in `Build` :245–285.
[^image-records]: MessageEntry.cs:3; LinkEntry.cs:3; TaskEntry.cs:3–6.
[^events-json]: events.json — event id 8.
[^image-reader]: ImageReader.cs — message not in the catalogue :655; `body_size` mismatch :684–687.
