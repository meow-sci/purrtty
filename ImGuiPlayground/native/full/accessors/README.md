# Native field accessors — explicit component tooling

Additive native member/container access and owned C# borrowed views. This is **not**
a replacement managed binding, production package, or full API qualification.
Ordinary .NET builds invoke no native tools. Existing api/layout/shared sources,
external DLLs and native algorithms are unchanged.

## Reproduce from the canonical isolated-copy root

Use new output names for every run; existing destinations are rejected. No downloads,
root/game writes, native substitutions in ordinary bin, or promotion are performed.
`KSAFolder` takes precedence over the explicit managed-directory option, which takes
precedence over `KSA_DLL_DIR`. There is no automatic game-path selection.

```sh
export PYTHONDONTWRITEBYTECODE=1
A=ImGuiPlayground/native/full/accessors
O=ImGuiPlayground/.tmp/native/accessors
python3 "$A/run.py" --out "$O/fix-final-build" \
  --targets osx-arm64 linux-x64 win-x64
python3 "$A/run_managed.py" --build "$O/fix-final-build" \
  --out "$O/fix-final-managed" \
  --managed-directory /Users/asherwin/repos/meow-sci/ksa-game-assemblies/current/dll
python3 "$A/self_test.py" --build "$O/fix-final-build" \
  --managed "$O/fix-final-managed" --out "$O/fix-final-negatives"
dotnet build ImGuiPlayground/ImGuiPlayground.slnx \
  -p:KSAFolder=/Users/asherwin/repos/meow-sci/ksa-game-assemblies/current/dll \
  -warnaserror --nologo -v:q
```

The native builder uses Zig0.17.0 and the pinned target triples
`aarch64-macos.11.0`, `x86_64-linux-gnu.2.17`, `x86_64-windows-gnu`.
Every engine/accessor/fixture TU and C11 header check uses `-fno-strict-aliasing`.
C++ also uses `-fno-exceptions -fno-rtti -fno-threadsafe-statics -nostdlib++`.
Native export names and defined-code kinds are checked: Mach-O/ELF external `T`
symbols, PE export RVAs in executable `TEXT` sections (not inferred from names).
All subprocesses use the shared process-group timeout utility; successful runs are
quiet, with logs retained. Existing root/ancestor/leaf/dangling redirects are checked
before imports/input reads as well as output creation. The whole write plan is a
new tree under project scratch, disjoint from selected input trees and protected
generated native source. Concurrent hostile swaps are outside this contract.

### Corrected P1 bootstrap/input preflight

The historical closeout did not guard imported/native-source leaves early enough:
a redirected generator could execute even on `run.py --help`. The correction does
not change native behavior or weaken pins. Each trusted invoked entrypoint checks
`path_guard.py` and its selected bytecode-cache leaf/ancestors **before loading it**.
That guard checks the complete component and sibling-layout input closure, shared
process utility, selected headers/TUs, config/lock/contract/archive and managed
source paths before any other project module is imported. Dependencies are loaded
from exact checked paths rather than an unchecked sys.path fallback. Selected
module cache paths are also checked; tools disable project bytecode writes.

Consuming entrypoints guard build/stage/managed input trees and reject output
inside or above selected inputs before receipt reads, mkdir or compiler work.
Generation checks every output leaf before writing, and earlier generated native
source trees remain protected. Accepted dependencies are unchanged. This is not a
sandbox against arbitrary edits to the invoked bootstrap or hostile concurrent
swaps, and does not ban approved SDK/compiler/framework symlinks.

`input_self_test.py --out <new-owned-scratch-directory>` runs actual subprocess
entrypoints, not post-import helper-only tests. Its90 cases include88 existing/
dangling module/native/header/data/ancestor and protected-output negatives plus
2 functioning import/compiler-marker positive controls. Each negative requires
the specific preflight diagnostic, no marker, no destination and unchanged
protected bytes/membership. The complete suite invokes it automatically.
Controlled child environments remove `KSAFolder` and `KSA_DLL_DIR` aliases
case-insensitively, so an operator's exported selection cannot replace a synthetic
fixture input. The selected-directory overlap case deliberately supplies a
conflicting ambient directory before that isolation; a separate control checks
case variants and preservation of unrelated environment variables. This changes
only test environments, not production managed-directory selection precedence.

## Closed ABI and source identity

The generated `manifest.json` is schema `purr.native-accessors`, version1.
Its47 `fields` retain original/effective widths, signedness, bounds, exact member and
source identities, readonly/boolean classification and live-engine domain notes.
`opaqueShapes` is the closed13-shape route/lifetime inventory. `records` defines the
record IDs. IDs are closed, not arbitrary casts or unknown-type fallbacks.

Final accessor identity (SHA256 of canonical sorted compact JSON, excluding only
its own `identity` property):
`e86ced3fb638c94c285378697c77e6e723bd75864c337e2b557449b4e4677f72`.
It binds the closed inventories, source/config/patch profile and production
header/TU/generator/guard hashes. `NativeFieldAccessors.ExpectedIdentity` must agree;
never relax that check when changing code. The helper is not included in its own
identity hash, so generation is not cyclic. Build/run records separately freeze
all component and managed source hashes plus actual compiler/artifact identities.

Pinned inputs:

- Source commit `031a18c417158427217bc5890e0ec0cb7e7b4b63`; archive SHA256
  `2d1c175790496529ed8e90e0fba5b9dc852769f959edb9ef145e8f82d4dcc3be`.
- Source-lock SHA256
  `0f8dba40bb599030d1a13d8907a07a411a4c55a5b6fad208768d3e43b0602a77`.
- Config SHA256 `18e6ca90823b36a891eca1c182eba6deb8a35d64431d03db3b91477a1e5049f3`.
- Exact six-patch manifest SHA256
  `060599ff6f13e95c5df83610b26c336c3e2ba44e46cde67f9806f24427eaf36d`.
- Managed contract SHA256
  `20e417b6312f8d28ba282bd0dc641e7b2dbea4b5d378b40c42a50c390b0af89e` is
  **host-specific macOS/.NET provenance**, not a universal foreign-runtime hash.
  Portable declarative projection is retained separately as
  `3e3cd8793d90fcfd351ef14c9179446d56ec27c859fda78027a54c96804330eb`.

`patch_source.prepare_source` creates a fresh verified patched tree; pristine cache
is never edited. `source-record.json` retains every extracted/patched source hash.
The frozen source lock is verified before interpreting config/toolchain/target data.

### Production versus fixture exports

`accessors.h` is the C ABI: **23 production exports**. Generated `exports.json`
(schema `purr.accessor-exports`, version1) inventories their exact declarations
separately from **19 TEST-ONLY exports** in `fixture.h`.

Statuses are0 success,1 null,2 invalid/mismatched ID/operation,3 range,4 readonly,
5 insufficient copy buffer,6 actual file I/O failure. Managed wrappers translate
these deterministically. No exception crosses the native ABI. Pointer arguments
must still denote correctly typed live native objects; native OOM/invalid-pointer
behavior is not converted into arbitrary-memory safety.

Own sequential DTO sizes are FieldInfo24, Triple12, SharedSnapshot20,
TextEditSnapshot16, CellSnapshot8. C++, C11 and C# checks corroborate these and
exercise fieldwise semantics. Production setters assign actual C++ members;
production C# never masks raw packed storage.

## Representational safety, readonly storage and ownership

All47 original fields are readable. The3 StyleVarInfo fields are readonly;
`StyleAt` bounds-checks and calls native `GetStyleVarInfo`. Other44 fields have
representationally checked setters. Three promoted fields use effective signed32,
not old8/16-bit bounds. Bool-like fields accept0/1 and return canonical0/1; signed
flags and signed2-bit IsEnabled retain extrema/sentinels rather than becoming bool.

**Representability is not engine-domain validation.** The manifest's per-type domain
notes preserve native requirements: e.g. valid modifier masks, mouse -1/buttons,
named sort/data-authority values, valid source/rectangle indices, and consistent
font/dock/table/settings lifecycles. Helpers do not automatically preserve these
higher-level invariants. Out-of-domain representational tests are isolated fixture
assignments, restored before cleanup; no engine algorithm consumes them.

Raw BRUTAL aliases remain unsafe:44 packed aliases, StyleVarInfo CLR6/native4,
RectEntry CLR7/native4 (including its concrete vector), all13 Size1 placeholders,
and TextBuffer.EmptyString's managed-instance/native-static conflict. Glyph
full-width aliases and FontBaked byte79 overlap are not repaired in the DLL.
The helper exposes checked native indexing and owned snapshots, never a CLR
ref/span using defective strides. TextBuffer uses real append/clear/copy/static
access and never writes the managed EmptyString alias over Buf.Size.

`NativeFieldAccessors` takes the **already-owned library handle**. It creates no
resolver/context, binds all required exports explicitly, and owns/frees neither
library nor context. Missing/wrong assets fail on explicit helper construction,
not ordinary startup. Dispose helpers before the owner unloads their library.

Borrow scopes and every derived operation check UI-thread affinity, helper/scope
disposal and their captured generation. The caller must invalidate **before**
external structural mutation, reallocation or destruction. This does not detect
an arbitrary stale or wrongly typed pointer supplied by a caller. A checked
`BorrowedAddress.Address` extraction is an explicit pointer escape: do not cache it
past invalidation. TextBuffer returns owned copies only, so its own append/clear
cannot invalidate dependent managed byte views. No hidden native liveness hooks.

| ID | Concrete opaque shape | Useful route and lifetime restriction |
|---:|---|---|
|0|`ImBitArrayForNamedKeys`|Actual named-key bounds; test/set/clear; borrowed key storage|
|1|`ImChunkStream<ImGuiTableSettings>`|Native chunk count/ordinal iteration; invalidate on stream mutation|
|2|`ImChunkStream<ImGuiWindowSettings>`|Same, variable chunks via begin/next_chunk|
|3|`ImDrawListSharedData`|Owned font/tessellation/flags snapshot; borrowed context/draw-list lifetime|
|4|`ImFileHandle`|Pointer value, real ImFileGetSize; no ownership/close; failure may move position|
|5|`ImPool<ImGuiMultiSelectState>`|Map/alive counts, key lookup, checked map iteration; invalidate on structural change|
|6|`ImPool<ImGuiTabBar>`|Same; tombstones/missing keys return null, never treat Buf holes as objects|
|7|`ImPool<ImGuiTable>`|Same, with scoped packed-field record access for live entries|
|8|`ImSpan<ImGuiTableCellData>`|Native count/index and owned cell snapshot; borrowed backing lifetime|
|9|`ImSpan<ImGuiTableColumn>`|Native count/index and scoped member access; invalidate on rebinding/backing change|
|10|`ImSpan<short>`|Native indexing and copied value, not Size1 stride|
|11|`ImStableVector<ImFontBaked,32>`|Actual block indexing; clear/destruction invalidates; growth itself retains native addresses|
|12|`ImStbTexteditState`|Owned cursor/selection/insert-mode snapshot; actual configured STB declaration/input lifetime|

Production owns no opaque allocation or destruction. No copied upstream records,
private binding implementations, or invented container/editor algorithms.

## Evidence and limits

Successful correction commands retain per-RID build/header/symbol logs and build
records, `managed-report.json`/`run-record.json`, and the127-check/zero-skip
`self-test.json` (the prior37 plus90 actual entrypoint preflight cases) with
individual logs and `entrypoint-preflight/input-self-test.json`. The final handoff indexes their
actual bytes and source hashes rather than checking in megabytes of host reports.
Measurements are streamed/variable-size (the managed report exceeds32KiB), not
stored in a fixed32KiB buffer. Windows/Linux execution is **pending**, not implied
by their compilation or host CLR observations.

The actual C# checks cover47 fields/native masks/signedness, complete nonzero
ordinary/packed neighbors and adjacent native RECORD preservation;39 readonly
style entries; native RectEntry vector indexing; all13 concrete CLR Size1
observations and real opaque operations; TextBuffer static safety; bounds/null,
wrong-record/shape, readonly, scope/generation/disposal/thread and handle ownership.
The native fixture constructs95 records/containers. Its pass-through address ledger
requires matching allocation/free addresses, no outstanding/error/failed events,
and exact original allocator callback **and user-data** restoration. The completed
pre-closeout run observed639 allocations/639 frees,7 null frees; final records are
authoritative. This audit is only the bounded fresh-process callback interval,
**not** proof of zero CRT/STB/OS/global allocations. Null context alone is not proof
of no preexisting allocations. Restoration runs even on test failure; leaks are
not freed to hide failure.

Negatives include closed inventory/profile mutations, protected path redirects,
deterministic generation,5 actual managed manifest mutants and8 compiled native
mutants: wrong member/mask/sign, CLR6/7 strides, missing export, identity mismatch,
and deliberate real native allocation leakage. Wrong-stride fixtures use adjacent
native records, not an offset0-only check. Selected/built/staged/process-loaded
BRUTAL ABI contributor identities and the actual runtime dependency closure are
checked; missing implementation dependencies are not swallowed.

`FullAccessorChecks.Run(handle, manifestPath, selectedManagedDirectory, fixtureFilePath)`
is the owned check seam. It does not load/free a native library or install a resolver.
The disposable harness is the sole native owner. Real Host->Checks project-reference
compilation is a separate gate, not replaced by direct-source harness execution.

Production integration (not performed) links **only accessors.cpp**, generated
`fields.inc`/`identity.h`, and the identical six-patched core/config. Never include
fixture lifecycle/audit exports in production. The standalone fixture is different:
`fixture.cpp` includes actual imgui.cpp once to observe its CPP-local style table,
so its build omits a separate imgui.cpp TU. Final combined semantic tests, independent
review/reproduction and foreign-host execution remain subsequent owners' gates.

## Narrow analyzer dispositions

Compiler macro/intrinsic **uses** (`_WIN32`, `__cplusplus`, export attributes and
platform CRT APIs) are not owned reserved-name declarations; retain correct names,
with no broad suppression. `_WIN32`/`__cplusplus` diagnostics were specifically
reviewed as false positives. Actual pinned headers—not copied or pristine fallbacks—
are selected through the component-local `.clangd` compilation database.
Historical ambient missing-include and stale C# project-reference diagnostics were
recorded; actual all-target and real Host->Checks compilers are authoritative for
those inclusion/reference questions. An editor limitation is not a clean-LSP claim.
The final handoff records active diagnostic commands/results and any remaining
coverage limitations. `KSAFolder`'s case-sensitive legacy alias has one targeted
SIM112 disposition; changing its spelling would change managed selection policy.
