# Full native storage-layout component (opt-in)

This directory owns only native storage classification, independent probes and exact
**declaration-only** source patches. It does not implement ABI forwarding, production
bitfield accessors, C# helpers, managed qualification, packaging or runtime promotion.
Ordinary .NET builds never invoke this toolchain.

## Pinned inputs and safety boundary

- Managed contract SHA-256: `20e417b6312f8d28ba282bd0dc641e7b2dbea4b5d378b40c42a50c390b0af89e`.
  It records the selected unchanged ImGui DLL
  `b76777a4ef3399d6353b9dba1982e3afb84b5da2aa2500da1c062db0bfad827c` and its metadata closure.
- Source/archive/configuration/target triples/Zig 0.17.0 come from the existing
  `native/source.lock.json`; this component does not change that lock or configuration.
- Every mapping is a closed, assembly-qualified graph identity; no runtime simple-name
  normalization or permissive unknown-type fallback. `mapping.json`, `bitfields.json`
  and `enum-transport.json` have source-pinned hashes in `generate.py`.
- `patch_source.prepare_source(archive, new_destination)` verifies the archive, safely
  extracts **a new scratch copy**, applies six ordered exact hunks, verifies each patch
  hash and complete pre/postimage, and publishes only after every check succeeds.
  Existing output trees, symlinks, path escapes, extra/missing/reordered/repeated steps,
  changed preimages and already-applied inputs fail. The pristine cache is never edited.
- Only `imgui.h` and `imgui_internal.h` change. No packing switch, obsolete-function
  enablement, state removal, widget algorithm change, upstream type duplication or
  fake native measurement is used.

Final patched header hashes:

| Header | SHA-256 |
|---|---|
| `imgui.h` | `95a0ff5ccf138f015e72dcd3cdabd5e0ea7ee887c3db7820dbec19297d13115f` |
| `imgui_internal.h` | `b2776134aa9d987af6c3125b77a6c50cad9fe75d75453da7e332d2afa6abf07e` |

The ordered patch manifest hash is
`060599ff6f13e95c5df83610b26c336c3e2ba44e46cde67f9806f24427eaf36d`.
The six steps promote isolated BoxSelect.KeyMods, StackLevel.DataType and
Context.ActiveIdMouseButton, insert a zero-width unsigned allocation boundary after
DockNode's three authority fields, and widen **both** SortDirection's forward
declaration and definition to `int`. Packed aliases are not converted into unions.

### SortDirection cross-component integration contract

The exact `imgui.h` edits are:

- Forward declaration: `enum ImGuiSortDirection : ImU8;` becomes
  `enum ImGuiSortDirection : int;`.
- Definition: `enum ImGuiSortDirection : ImU8` becomes
  `enum ImGuiSortDirection : int` immediately before its existing enum body.

These are enum declarations, not a replacement of the global `ImU8` typedef;
`ImU8` itself remains unchanged. Independently observed post-patch widths are
`sizeof(ImGuiSortDirection) == 4` and SortSpecs.SortDirection member width4 at
offset8; `sizeof(ImGuiTableColumnSortSpecs) == 12` remains unchanged. MacOS executes
these observations; all three target compilers enforce the widths statically.

The API lane's pristine-source width1 observation affects
`TableGetColumnNextSortDirection_internal` and
`TableSetColumnSortDirection_internal`. It must not become a final width1 invariant
in the common ABI header. Integration must rerun API generation and typed compile
gates against this patched tree and retain source-selected typed scalar casts for
the named0/1/2 values. This storage patch does not authorize a new out-of-range
conversion policy, widget behavior, or a managed ABI qualification claim.

## Reproduce (from repository/copy root)

Use explicit Python and the selected Zig/macOS SDK. No downloads are performed.
Every subprocess uses the shared process-tree bounded runner: compiler 240 seconds,
negative compiler fixture 120 seconds, native process 30 seconds, discovery 15 seconds.
Output is quiet on success; no sleeps. Choose a new output directory for each run.

```sh
export PYTHONDONTWRITEBYTECODE=1
LAYOUT=ImGuiPlayground/native/full/layout
CONTRACT=ImGuiPlayground/.tmp/native/binding-contract.json
ARCHIVE=ImGuiPlayground/.tmp/native/imgui-031a18c417158427217bc5890e0ec0cb7e7b4b63.tar.gz
ROOT=$(mktemp -d ImGuiPlayground/.tmp/native/full-layout-run.XXXXXX)
python3 "$LAYOUT/run.py" --contract "$CONTRACT" --archive "$ARCHIVE" \
  --out "$ROOT/patched" --targets osx-arm64 linux-x64 win-x64
python3 "$LAYOUT/run.py" --contract "$CONTRACT" --archive "$ARCHIVE" \
  --out "$ROOT/pristine" --baseline --targets osx-arm64
python3 "$LAYOUT/self_test.py" --contract "$CONTRACT" --archive "$ARCHIVE" \
  --evidence "$ROOT/patched" --scratch "$ROOT/tests" --report "$ROOT/self-test.json"
```

Baseline mode deliberately retains mismatches; it is **not** a parity pass. Patched
mode fails on any unclassified ordinary storage mismatch. Cross targets compile the
whole executable plus an independently checked object; they are **never executed on
macOS**. To obtain foreign runtime evidence, a qualification owner must execute the
foreign probe with the corresponding target CLR contract and complete the separate
managed/helper gates. This component does not claim that host CLR observations are
Windows/Linux CLR observations.

## Consumer interfaces

### `mapping.json` / `bitfields.json` / `enum-transport.json`

- All **1,095 graph nodes / 2,247 fields**, including all **318 Context fields**,
  have individual dispositions. Retains managed static/private/open-generic/byref-like
  helper fields, pointer wrappers, concrete vectors, legitimate unions and inherited
  storage; it never deletes a difficult record. Reflection's raw overlapping aliases
  remain in the original contract and the closed map.
- 57 concrete vector shapes have actual native element sizeof/alignof/stride evidence,
  including pointer-wrapper elements and nested text ranges. Header equality is not
  used as a substitute for element-stride equality.
- 22 inline arrays, 16 fixed-buffer records, their parent fields, numeric scalar-array
  aliases and actual native arrays have native count/stride/size evidence. Formula:
  `elementOffset(i) = parentOffset + i * stride`, `0 <= i < count`; count * stride
  equals array width and the entire declared extent is checked. This proves storage,
  not arbitrary C++ inactive-union reads or array mutation behavior.
- `ImGuiViewportP` retains a separate actual `ImGuiViewport` base-subobject observation;
  every derived member is measured by addresses on a real constructed object on macOS.
  Compiler `offsetof`/record dumps are only corroborating evidence for derived types.
- All **47 original bitfields in 12 types** retain original widths and signedness.
  Three are now ordinary promoted fields, 44 remain unsafe raw aliases. Native
  `decltype(member)` width/signedness corroboration is emitted independently; that
  width is the **declared scalar type**, not an invented address/size of a bitfield.
  Masks, signed-value semantics and neighbor-preserving helper behavior are pending.
- The two old CustomRect bitfields are explicitly recorded as block-comment-only
  upstream documentation, not counted as active fields. Reverse declaration scanning
  verifies all 47 active members against pristine pinned headers.
- 24 individually approved enum exceptions have unsigned native backing vs managed
  int. The result includes both full declared value inventories, independently compiled
  native values/width/signedness, exact native declaration references, managed import
  uses and actual member `decltype` measurements. Private constant enums are not
  substituted for fields declared with signed `int` aliases. Same byte storage does
  **not** qualify enum domain/transport; next owners must prove named values, flag
  combinations, valid negative sentinels and bit-preserving high-bit transport.

### Generator and build outputs

`generate.load_inputs(contract_path)` verifies pins and complete consumer inventory.
`generate.generate(contract, mapping, output, patched=True)` writes:

- `probe.cpp`: includes actual scratch `imgui.cpp` once, then owned probe code. This
  sees CPP-local DockRequest/Preview/Settings definitions. Build **must omit the
  separate upstream imgui.cpp TU** for this driver. Actual configured STB headers
  provide rect-pack/text-edit declarations; no copied record definitions.
- `interface.json`: hashes, exact required numeric table keys and column descriptions.
  `typeId` indexes the closed map; `fieldId=-1` is a type; vector element records use
  `typeId + 10000`. Missing/extra/duplicate producer keys fail against the independently
  required map, even if the producer also deletes its interface key.
- `original_bitfields.inc`: macro rows
  `PLAYGROUND_ORIGINAL_BITFIELD(index, native_type, native_member, original_bits, signed, promoted)`.
  This is an interface for the next accessor owner, **not generated accessors**.

`run.py` writes `source-record.json`, source/tool/compiler hashes and commands in each
RID's `build-record.json`, `build.log`, `static-check.log` (full compiler record layouts),
`checked.o`, and the real executable. All header search paths use the same verified
scratch source and pinned configuration. It keeps `-nostdlib++`; Zig's bundled
`type_traits` headers are used only for compile-time observations, not a new C++
runtime dependency. On macOS it also writes `native.json`, `runtime.log` and
`comparison.json`. JSON streams to stdout/files rather than a fixed 32 KiB buffer;
the current measured payload is **76,732 bytes / 3,102 records**, bounded at 64 MiB
on ingestion. No managed expected offsets/sizes are embedded as native observations.

`generate.compare` produces separate ordinary storage parity, known conflicts,
raw-alias and pending semantics results. `alignof(member type)` is not effective
member alignment. Native type alignment and CLR byte-prefix embedding are retained
as independent observations, not interchangeable definitions. Conflict waivers are
property-specific and shared by comparison and assertion generation:

- `opaque-placeholder` retains its reviewed size, stride and native-alignof-versus-host-
  embedding differences for all 13 Size1 records.
- `bitfield-record-stride-conflict` waives **only size and stride**. StyleVarInfo and
  RectEntry still require native alignment4 against the selected macOS host embedding
  observation4, in both the comparator and generated compiler assertions. Their
  native size/stride4 versus actual CLR6/7 remains an explicit conflict. Changing
  native alignment4 to1 must produce an unexpected mismatch, not a new waived one.

This corrects the independently reproduced overly broad alignment waiver; it does not
change source declarations, pinned metadata or the macOS whole-contract-hash policy,
and does not turn CLR embedding into a universal native alignment definition.

## Checked results and exact limits

Current macOS native execution: **6,050 comparisons, zero unclassified ordinary
mismatches** after patches. Windows x64 GNU and Linux x64 glibc 2.17 target compilation
and static checks pass, not runtime validation. All-target record-layout dumps show
DockNode authorities at 200, first bool at 204, last bool at 205, sizeof 208/align 8.
MacOS additionally observes those bool bytes by native assignment/snapshot, without
claiming an exhaustive mask/semantic test.

Pristine macOS records **259 ordinary mismatch observations** (including 250 member
offsets), plus the same known conflicts. Context is 11312 before / 11320 after;
StackLevelInfo is 64 / 72; SortDirection is 1 / 4 despite SortSpecs remaining 12;
BoxSelect downstream positions shift to the required 16/24/32. Dock's bool bytes are
201/202 before and 204/205 after. The three promotions exercise native volatile
stores/loads over all 16 modifier combinations (including Super), all public/private
DataType values, and mouse -1 through valid buttons. SortDirection exercises native
call/return and storage over its three named values. This is not a managed calling
convention qualification or equivalence for out-of-domain truncated values.

There are **57 retained numeric/storage conflict observations**, not 57 independent
broken fields:

- 13 opaque placeholders: 39 size/stride/alignment observations. Never allocate/copy
  native objects using managed Size1; use actual native operations/lifetimes.
- 12 opaque parent member widths differ; their parent offsets still match.
- StyleVarInfo native size/stride4 vs real CLR6; RectEntry native4 vs CLR7: four
  observations. Neither native type is shrunk to match bad managed aliases.
- RectEntry concrete-vector element native stride4 vs CLR7: one observation; native
  checked indexing is mandatory **before** member access.
- TextBuffer.EmptyString is native static but managed instance offset0: one conflict;
  do not write its raw alias over Buf.Size; use real text-buffer APIs.

Additionally retain **44 unsafe raw bitfield aliases** and **24 enum signedness/domain
exceptions**. Safe C# helpers and full native bitfield semantics remain the next owner;
no unqualified binding/API compatibility is claimed. Helpers must not return refs to
these aliases, must use native indexing for bad strides, and must obey native borrowed
lifetime/UI-thread/invalidation constraints. Texture IDs remain the unsigned native
64-bit representation versus managed nint transport; bit-preserving transport belongs
to ABI/helper qualification, not a signed numeric conversion.

`self_test.py` currently executes **54 quiet tests**: patch/hash/order/hunk/repetition/
symlink/traversal failures; exhaustive map and bitfield omissions/mutations; deterministic
generation; producer/interface reverse omissions; native size/align/offset/array extent/
enum-width/base/Dock/signedness mutations; preserved CLR6/7 conflicts; and explicit
alignment4-to1 negatives for **both** stride-conflict records. Each requires exactly
one unexpected alignment mismatch, false ordinaryStorageParity, and unchanged known
conflicts, including the 13 opaque alignment observations.

Four actual compiler negatives cover a pristine-header substitution, blanket packing,
and isolated packed-declaration fixtures for StyleVarInfo and RectEntry. Each isolated
fixture first compiles without the parity gate, independently asserting native size4
and alignment1. Enabling the generated parity gate must then fail solely at that
record's new alignment assertion (1 versus host embedding4). These are disposable
negative-fixture declarations, not changes to the production patch set. Positive-control
and negative compiler logs are retained under the requested test scratch directory.

Next integration action: review the owned map/exception/patch manifests, call the
patch preparation interface from the eventual opt-in integrator, compile the API and
helper components against **that identical tree/configuration**, and consume the
47-member macro/metadata interfaces. Do not edit existing native/build.py or promote
production runtimes as part of this component handoff.
