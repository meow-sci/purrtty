---
type: Subsystem Reference
title: kVM message-bus slots — the VM side
description: How KvmMachine stores, copies, stamps, latches and exposes message-bus slots — the mechanics underneath the host's message bus.
resource: ksa-decomp:KSA.Kfs.Vm/KvmMachine.cs
tags: [kfs, vm, kvm, message-bus, slots, publish-subscribe]
sources:
  - id: kvm-machine
    resource: ksa-decomp:KSA.Kfs.Vm/KvmMachine.cs
    title: KvmMachine (decompiled) — LoadBus, CopySubscriptions, CopyPublications, LatchSlots, WriteMessage, ReadMessage
  - id: image-records
    resource: ksa-decomp:KSA.Kfs.Vm.Image/MessageEntry.cs
    title: MessageEntry and LinkEntry (decompiled)
  - id: image-reader
    resource: ksa-decomp:KSA.Kfs.Vm.Image/ImageReader.cs
    title: ImageReader (decompiled) — message and link rules
  - id: bus-steps
    resource: ksa-decomp:KSA.Kfs.Vm.Image/BusSteps.cs
    title: BusSteps (decompiled)
  - id: host-computer
    resource: ksa-decomp:KSA.Kfs.Host/HostComputer.cs
    title: HostComputer (decompiled)
  - id: message-layout
    resource: ksa-decomp:KSA.Kfs.Vm.Tables/MessageLayout.cs
    title: MessageLayout (decompiled) — HostPublished list
generated: { by: claude-code/claude-fable-5-2, at: 2026-11-25T21:46:43Z }
status: stable
ksa_build: "2026.11.25.5949"
---

# Summary

The third program↔host channel, the message bus, is implemented inside the VM as plain memory copies. Each
message an image uses has a **slot** in the protected VM region of RAM; each task that subscribes to or
publishes a message has a **buffer** in ordinary RAM. The VM copies slot → buffer when a task starts, and
buffer → slot when it finishes, charging cycles for both. The host injects messages by writing slots and
reads published messages from a latched copy.[^kvm-machine]

Catalogue content (ids, names, bodies) is in [messages table](/tables/messages-table.md); the host-side
routing between computers is in [message bus](/host/message-bus.md). This document covers only what
`KvmMachine` does.

# Image records

```csharp
public sealed record MessageEntry(ushort MessageId, ushort BodySize, uint SlotOffset, byte Role);
public sealed record LinkEntry(ushort MessageIndex, uint BufferAddress);
```

[^image-records]

- `Role` bit 0 = this computer **subscribes**, bit 1 = it **publishes**. 0 and values above 3 are rejected
  at load.
- A slot is `16 + BodySize` bytes at RAM address `image.VmRegionBase + SlotOffset` (`KvmMachine.cs:278-279`).
- A link ties one task to one message through a buffer of the same size at `BufferAddress` in the globals
  region. `TaskEntry.SubFirst/SubCount` and `PubFirst/PubCount` select each task's links.

Loader rules that shape this (`ImageReader.cs:609-754`):[^image-reader] every message id is in the
`messages.json` catalogue; `Messages[]` is sorted by ascending id with no duplicates; `BodySize` equals the
size the catalogue or the vehicle's layout gives (`MessageLayout.BodySizeOf`); `SlotOffset % 8 == 0`; slots
lie in the VM region and overlap nothing; buffers are 8-aligned and inside the globals region; `Links[]`
holds each task's subscriptions then publications in task order, each group sorted by message index then
buffer address; the role bits agree with the links; **a published message has exactly one publishing task
and one buffer**.

# Slot format

The first 16 bytes of a slot (and of every buffer) are a header; the body follows.

| Offset | Size | Field | Written by `CopyPublications` |
| --- | --- | --- | --- |
| 0 | 4 | sequence number | previous slot value + 1, wrapping from `0xFFFFFFFF` to 1 — so 0 means "never published" |
| 4 | 2 | publisher | `ComputerIndex` (always 0 for in-game machines) |
| 6 | 2 | reserved | 0 |
| 8 | 8 | timestamp, µs | `k · 1,000,000 / tick_hz` (the same value as `sys.met_us`) |

(`KvmMachine.cs:324-342`.) For host-published messages the host supplies the whole slot, header included,
through `WriteMessage`.

# Copies during a tick

| Moment | Operation | Code |
| --- | --- | --- |
| A task starts fresh, or resumes | **Subscriptions**: for each subscribed link, copy the whole slot (header + body) into the task's buffer. | `CopySubscriptions`, `:314-322`; called at `:721, 769` |
| A task completes (`RET` of its entry function), or the program task executes `YIELD` | **Publications**: for each published link, read the slot's old sequence number, copy the buffer over the slot, then overwrite the slot header with the new sequence, publisher, 0 and timestamp. | `CopyPublications`, `:324-342`; called at `:1086, 1116` |
| End of a tick that latches | Copy every published slot to its latched copy and fold it into the command stream hash. | `LatchSlots`, `:344-353`; called at `:2191` |

Consequences:

- A task sees a consistent snapshot of each subscribed message for the whole of its run (or, for the
  program task, for the part of its run within one tick).
- Whatever a publishing task writes into the first 16 bytes of its buffer is replaced in the slot by the
  VM's header; the buffer itself is not modified.
- A task that runs later in the same tick sees what an earlier task published in that tick. The host sees
  it only after the latch, and not at all if the tick overran or trapped.
- A periodic task that overruns never reaches its `RET`, so it publishes nothing.

Copy cycles are charged with each of these steps; see [cost model](/vm/cost-model-and-budgets.md).
`BusCopyCycles(task)` returns the two per-task totals.[^bus-steps]

# Host access

| Method | Preconditions (else exception) | Effect |
| --- | --- | --- |
| `void WriteMessage(ushort id, ReadOnlySpan<byte> msg)` | Image loaded **and booted** (`InvalidOperationException` "WriteMessage needs a booted machine (kVM §8.2)"); the image subscribes to `id` and does not publish it (`InvalidOperationException` naming which condition failed); `msg.Length == 16 + BodySize` (`ArgumentException`). | Copies `msg` over the live slot. |
| `void ReadMessage(ushort id, Span<byte> msg)` | Image loaded; the image publishes `id`; exact length. | Copies the **latched** slot. |
| `(bool Subscribes, bool Publishes) MessageRole(ushort id)` | — | `(false, false)` without an image or for an unknown id. |
| `void ReadSlot(ushort id, Span<byte> msg)` | A slot exists for `id` (`InvalidOperationException` "no slot for 0x…"). | Copies up to `min(msg.Length, slot size)` bytes of the **live** slot. |

(`KvmMachine.cs:368-426`.) `HostComputer.RunTick` calls `WriteMessage` for each `HostMessage` it was given
before running the tick (`HostComputer.cs:420-423`), and `HostComputer.Load` refuses an image that publishes
one of the host-published messages (`MSG_FLIGHT_DATA`, `MSG_VEHICLE_PROPS`, `MSG_BURN_STATUS`,
`MSG_PILOT_INPUT`, `MSG_FC_SETTINGS`, `MSG_BURN_PLAN`) with rule `host-message`
(`HostComputer.cs:305-315`, `MessageLayout.cs:27`).[^host-computer][^message-layout] That list has six
names; the vehicle host packs a seventh message, `MSG_NAV_STATE` (`0x0101`), whenever the image subscribes
to it without publishing it, and the `host-message` rule does not cover that one
(`KSA.Kfs.Host/HostMessages.cs:364`, `VehicleComputer.cs:338-342`).

# Lifecycle

- `LoadImage` → `LoadBus` builds the slot address/size tables, the per-task link lists, the per-task copy
  costs and one latched buffer per published message (`:267-303`).
- `Reset` zeroes RAM (all slots, hence all sequence numbers) and the latched copies (`:1958, 1966`).
- The latched copies of published slots are part of the saved state; the live slots are saved with RAM
  (see [state save format](/vm/machine-state-format.md)).

# Modding notes

- The bus is the channel for **structured, sequence-stamped records pushed into a program without the
  program asking** (the sensor block would be the unstructured one, but vehicle layouts have no sensor
  fields in this build, so on a vehicle the bus is the only such channel), but the set of messages is closed by
  the catalogue: an image is
  rejected if it names an id not in `messages.json`, and each body size is computed by `MessageLayout`.
  Adding a message means extending the catalogue and its layout code (see
  [messages table](/tables/messages-table.md)), not the VM.
- `WriteMessage` is public and needs no patch. `Inferred:` (from its signature and checks; not exercised) a
  mod that has added a catalogue message can deliver it to
  any machine that subscribes, on the tick thread before `RunTick`. It must build the 16-byte header itself,
  and must first test `MessageRole(id).Subscribes` on a booted machine — `WriteMessage` throws otherwise.
- `MessageRole(id)` lets a mod test cheaply whether a loaded program cares about a message at all.
- There is no VM-side notion of "new message arrived": subscribers compare the sequence number in the
  header.

[^kvm-machine]: KvmMachine.cs:31-49 (bus tables), 267-303 (`LoadBus`), 305-366 (`ClearLatchedSlots`, `CopySubscriptions`, `CopyPublications`, `LatchSlots`, `MessageIndex`), 368-431 (`WriteMessage`, `ReadMessage`, `MessageRole`, `ReadSlot`, `BusCopyCycles`).
[^image-records]: MessageEntry.cs:3; LinkEntry.cs:3.
[^image-reader]: ImageReader.cs:609-634 (message records, slots, overlap), 646-657 (catalogue membership), 658-700 (links, ordering, body size, alignment), 701-754 (link grouping, roles, single publisher).
[^bus-steps]: BusSteps.cs:9-44.
[^host-computer]: HostComputer.cs:305-315 (`host-message` rule), 420-423 (`WriteMessage` per host message).
[^message-layout]: MessageLayout.cs:27 (`HostPublished`).
