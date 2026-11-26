# Tables — the embedded game tables and their loader

kFS is table-driven: fourteen JSON files embedded in `KSA.dll` under
`KSA.Kfs.Vm.Tables.Data.*` define opcodes, costs, traps, the system block, soft routines, the
processor catalogue, the message catalogue, the service (syscall) table, events, enums, the compiler
diagnostics, floating-point self-test vectors, arithmetic edge cases and the bench I/O layout.
`KSA.Kfs.Vm.Tables.GameTables` loads and cross-checks them once and hands one shared instance to
every other layer.

## Loader and extension point

- [GameTables loader — loading, validation, derived data, consumers](/tables/game-tables-loader.md) —
  How KSA.Kfs.Vm.Tables.GameTables reads the 14 embedded JSON tables, what it validates and derives,
  who consumes the result, and how a mod could inject extra rows.

## Flight-interface tables (full contents)

- [services.json — the kFS service (syscall) table](/tables/services-table.md) — Schema, vocabulary
  and complete contents of the embedded services table that defines every SYSCALL id, its argument
  cells, domains, cost class and language bindings.
- [messages.json — the kFS message catalogue and its layout rules](/tables/messages-table.md) —
  Schema, packing rules and complete contents of the embedded message catalogue (ten bus messages),
  with every field's offset, size, type and meaning.
- [events.json — sleep events and wake reasons](/tables/events-table.md) — Schema and complete
  contents of the embedded events table, and how its ids tie to the sleep_until_event service and the
  system block's wake_reason.
- [enums.json and CatalogueEnums — enumerations and the generated KFS_* constants](/tables/enums-table.md) —
  Schema and full contents of the embedded enums table, and the exact CatalogueEnums rules that turn
  table text into the KFS_* constants of kfs.kinc, with every generated name and value.
- [I/O layout tables — IoLayout, LayoutFile, the bench layout and the fingerprint](/tables/io-layout-tables.md) —
  Schema of the memory-mapped I/O layout records, the validation and exact CRC-32 fingerprint
  algorithm, the complete bench_layout.json, and the SensorFile test-vector format.

## Processor and compiler tables (full contents)

- [catalogue.json — processor parts, tick rates and the bench part](/tables/catalogue-processors.md) —
  Schema and full contents of the embedded processor catalogue, how each PartDescription is derived,
  what the bench part is, and how a part is chosen for the bench and for vehicles.
- [diagnostics.json — the compiler diagnostic rule registry](/tables/diagnostics-table.md) — Schema
  and complete contents of the embedded diagnostics table (126 numbered rules for kC, kScript and
  kASM), and how the game uses it.

## VM tables (schemas only; contents are under `/vm/`)

- [Row schemas of the VM tables — opcodes, costs, traps, sysblock, routines, fpcheck, edgecases](/tables/opcode-cost-trap-tables-schema.md) —
  The record types, JSON keys, optionality and load-time constraints of the seven VM-facing embedded
  tables, with pointers to the /vm/ documents that reproduce their contents.

## Provenance

- [Index of internal spec, section and decision references in the kFS tables and code](/tables/spec-and-decision-references.md) —
  Every RocketWerkz-internal spec document, section number, decision, proposal, open item and
  milestone cited in the embedded tables and in kFS string literals, with what each is cited for and
  where.
