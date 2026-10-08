# Selected patched API profile (explicit maintainer component)

This component joins the **unchanged accepted API generator/common header** to the
**unchanged accepted layout patch set**. It owns no manual imports, bridges, safe
accessors, production runtime, resolver used by PlaygroundHost, or final full runner.
Ordinary .NET builds compile the two check seams but do **not** invoke native tools.

## Reproduction

From the isolated repository-shaped root, choose a **new physical output directory**
for each command. Python 3.9+, pinned Zig 0.17.0, the macOS SDK and .NET 10 are needed.
No native download or production promotion is performed. Success is quiet; bounded
process-group execution and diagnostic logs use the accepted `process_utils.py`.

```sh
PROFILE=ImGuiPlayground/native/full/profile
ROOT=ImGuiPlayground/.tmp/native/full-api/profile/review-replay
python3 "$PROFILE/selected_profile.py" --output "$ROOT/selected"
python3 "$PROFILE/enums.py" --selected "$ROOT/selected" --output "$ROOT/enums"
python3 "$PROFILE/layout_gate.py" --selected "$ROOT/selected" --output "$ROOT/layout"
python3 "$PROFILE/managed.py" --selected "$ROOT/selected" --enums "$ROOT/enums" \
  --output "$ROOT/managed" \
  --ksa-folder /Users/asherwin/repos/meow-sci/ksa-game-assemblies/current/dll
python3 "$PROFILE/self_test.py" --selected "$ROOT/selected" --enums "$ROOT/enums" \
  --output "$ROOT/tests"
python3 "$PROFILE/path_test.py" --selected "$ROOT/selected" --enums "$ROOT/enums" \
  --output "$ROOT/paths" --ksa-folder /Users/asherwin/repos/meow-sci/ksa-game-assemblies/current/dll
```

The managed CLI requires explicit selection. `--ksa-folder` overrides environment;
otherwise the existing case-sensitive `KSAFolder` takes precedence over `KSA_DLL_DIR`.
There is no automatic game-directory lookup. It builds a new isolated harness with
Logging 10.0.0 and ObjectPool 11.0.0-rc.1.26425.128, then copies build products into a
**disposable stage**. Only that stage receives `libimgui.dylib`. Missing runtime
implementation dependencies are not swallowed. The ordinary checks project was also
built successfully with explicit `-p:KSAFolder=...` and no native build step.

All output is restricted further than the accepted API tools: a **new** directory
below this physical project's `.tmp/native/full-api/profile/`. Existing roots,
ancestor/leaf/dangling/in-root redirects, traversal, nonregular/shared leaves and
out-of-root paths fail before writes or compiler invocation. Complete known write
plans are checked before mkdir. Outputs must be disjoint from selected profile, enum
and managed input trees, and cannot be nested inside an already prepared/published
profile or enum tree. This explicitly protects fresh descendants such as
`selected/source/new-output`; merely requiring a new output directory was insufficient.
Concurrent hostile swaps are excluded, as in the accepted tooling. Owned entrypoints preflight local modules (including the preflight module itself),
accepted dependency modules/package initializer, fixed source/config/contract/archive
inputs, entire selected input trees and selected managed/source leaves before import,
read or native work. Existing symlinks, reparse points, dangling redirects and shared
file leaves fail; standard-library/SDK/toolchain resolution remains unchanged. Safe
sibling output trees are supported. No dependency writer's boundary is broadened. `source-record.json` cannot self-authorize a changed
source: consumers independently compare every file to the verified archive plus the
exact reviewed final patch hashes.

## Source/profile contract

`selected_profile.build_profile(output, targets, zig='zig', runtime=True)` prepares a
new tree using `layout.patch_source.prepare_source`, extracts the actual pinned Clang
AST against it, derives a constrained overlay, emits using `api.generate.generate`,
and builds the selected targets. Default targets are osx-arm64, linux-x64, win-x64.
`native_model.declarations` retains the accepted declaration schema and AST extraction
logic, adding the mandatory `-fno-strict-aliasing` even to declaration-only TUs.
Every engine, adapter, native fixture, enum and layout probe TU uses that flag.

`profile.json` (`playground_imgui.selected-profile`, v1) publishes:

- Prepared source path; full archive/config/ordered patch and pre/post file receipts.
- Declaration model, derived mapping, generated wrapper/coverage and delta paths;
  an independently required artifact-hash inventory including the raw compiler AST; accepted generator/header pins.
- Portable API projection separately from host-specific full reflection provenance.
- Target artifacts/gate results and the still-reserved 16 imports and six bridges.
- Required all-TU flags. This is **not** a production runtime manifest.

`selected-mapping.json` retains the accepted `nonformat-mapping` v1 interface, so the
later builder can pass it directly to the accepted generator. `derive` requires the
reviewed pristine mapping SHA-256
`8c9f56f9fdf022c56af931ac2044cd4536fcf7aba9b1604ae9714a5a6bb733ca`.
It never selects a new callee merely because an AST contains one. It compares every
prior scope, name, signature, ordered parameter, kind, static/variadic status,
callback and conversion; Bool8 policy evidence remains unchanged. Only:

1. The two exact approved final header hashes;
2. Mechanically located **identical** source lines (489 mapped declarations shift by
   one line after the Dock allocation boundary); and
3. Both SortDirection scalar adaptations and source-derived width/signedness
   assertions changing from unsigned8 to signed32

may differ. Embedded conversion `target` evidence is recomputed and compared against
that same constrained delta. The patch receipt records all six steps; no core code
or common ABI header is edited. `delta.json` preserves each relocated source line.
SortDirection casts remain through `ImGuiSortDirection`, with no uint8 intermediate
and no new out-of-domain policy. Both scalar endpoints exercise 0/1/2 natively and
through the actual unchanged BRUTAL imports.

Pins retained unchanged:

| Input | SHA-256 |
|---|---|
| Contract (macOS/.NET10 provenance) | `20e417b6312f8d28ba282bd0dc641e7b2dbea4b5d378b40c42a50c390b0af89e` |
| Portable declarative projection | `3e3cd8793d90fcfd351ef14c9179446d56ec27c859fda78027a54c96804330eb` |
| Source archive | `2d1c175790496529ed8e90e0fba5b9dc852769f959edb9ef145e8f82d4dcc3be` |
| Configuration | `18e6ca90823b36a891eca1c182eba6deb8a35d64431d03db3b91477a1e5049f3` |
| Ordered patch manifest | `060599ff6f13e95c5df83610b26c336c3e2ba44e46cde67f9806f24427eaf36d` |
| Patched imgui.h | `95a0ff5ccf138f015e72dcd3cdabd5e0ea7ee887c3db7820dbec19297d13115f` |
| Patched imgui_internal.h | `b2776134aa9d987af6c3125b77a6c50cad9fe75d75453da7e332d2afa6abf07e` |

Exactly 1,130 production component symbols are required on every target; linker
metadata symbols are separately classified. There are no format/bridge substitutes.
All targets retain C11/POD assertions, wrong-signature compiler negatives and the
LLVM Bool8 output-view **pointer return only/no load/store/call** proof. macOS also
executes the accepted C++ fixtures (including callback aliases/output-only Bool8)
and actual-library ctypes checks. The prototype adapter is excluded.

`layout_gate.py` consumes the accepted full ordinary-storage generator/comparator on
**this same selected tree/config**, including CPP-local definitions. It does not
modify or rerun the layout runner with a different source. `imgui.cpp` is included by
that probe and omitted from its other link inputs. All three static gates pass;
macOS observes 6,050 comparisons, 57 retained conflicts, zero unexpected mismatches.

## Complete enum join: declarations are not storage aliases

`enum-policy.json` is the closed, pinned, reviewable correspondence/domain input:
85 BRUTAL enum types, **1,179 named values**, eight explicit combinations. The only
contract enum excluded is `System.TypeCode`, a nonnative managed helper declaration.
The policy hash is
`6a8c6b970f7dcd905cfac12b5a223db7afd5cdc5dcf30e7bb64d5ddeaa8ff6bc`.
No production name fallback chooses values: every managed/native name is explicit.

`enums.py` independently joins the contract and accepted layout mapping to a fresh
compiler model. Header ASTs are supplemented by filtered compiler ASTs for CPP-local
DockRequest, DockNodeSettings and DockPreviewData and actual compiler-resolved
`__underlying_type`/typedef aliases. This is not a source-name inference of signedness.
The native matrix statically verifies underlying types, widths, actual field/import
leaf storage types and every named value on **all three target compilers**.

`matrix.json` (`playground_imgui.profile-enum-matrix`, v1) retains:

- All 24 layout exception dispositions, including full original constant inventories;
  true enum vs typedef representation, associated constants owner/backing and actual
  signed-int storage typedefs. **Unsigned Private_ enums never replace signed native
  parameter/result or field storage types.**
- Exact compiler source evidence for values and actual fields; 133 unique native
  fields (158 associated rows), 133 unique managed field associations (158 associated
  rows), and 308 unique mapped import positions (418 associations). This includes
  ImGuiContext.ItemFlagsStack -> ImVector<ImGuiItemFlags>, all eleven ImGuiKeyChord
  fields and all twelve imported ImGuiKeyChord parameter/result positions, including
  SetNextItemShortcut parameter0. ImGuiKeyChord stays its actual signed-int typedef,
  never the ImGuiKey enum as a substituted declared C++ type.
- Reserved TreeNodeEx_1/2 flag imports explicitly marked unimplemented, rather than
  silently dropped. Constants declarations not themselves imported are explicitly
  classified, even when their associated storage typedef has many import uses.
  ImGuiWindowDockStyleCol has no imported/field storage use.
- Named domains, range/count/mask/reserved markers, negative sentinels and explicit
  legitimate combinations. Named constants are **not universally valid arguments**
  to every endpoint. Count/END markers are only transported in test fixtures.
- Supplemental transport symbols and the exact raw pattern inventory 0, 0x7fffffff,
  0x80000000, 0xffffffff. Raw high-bit fixtures use the **underlying integer type**,
  never an out-of-range nonfixed enum or a reserved engine value.

`census.json` (`playground_imgui.enum-use-census`, v1) is an independent reverse
census, regenerated by consumers from raw compiler ASTs, all selected declarations
and the full managed contract. It retains dispositions for **1,974 native field
edges, 3,901 declaration positions, 3,202 mapped import positions and 5,779 managed
field/import edges**. Candidate discovery uses nominal-type token membership,
independently of the producer's recursive leaf decomposition; omission from both
producer harvest and matrix still fails against raw AST fields. Recognized
relationships are cv qualifiers, pointers, references, arrays, ImVector/ImSpan
elements and the closed ImGuiKeyChord scalar wrapper. Unknown enum-containing
relationships fail instead of disappearing. All 576 associated field/import leaf
observations are compiled, executed and reverse-compared on macOS.

Record types are explicit graph boundaries: each field is censused once, rather
than recursively duplicating the entire Context/Window graph at every object-pointer
import. Plain int has no inferred enum semantics. Erased managed ImVector<Int32>
storage is joined using actual native nominal ImVector<ImGuiItemFlags>, not guessed
from managed names. Non-imported declarations and nonnative helper fields retain
dispositions. This is completeness over the pinned header/selected API and managed
closure plus the three required CPP-local Dock record ASTs, **not** a census of all
local variables/implementation expressions in every core translation unit.

The generated native fixture has genuine noinline typed/volatile round trips through
both actual storage types and associated constants-declaration types, and a separate
underlying-integer bit-copy path. This proves transport, not engine semantics. All
1,179 names are compared to both actual reflection constants and compiled native
values. Native field representation observations are retained in the native run log.
The complete matrix executes on macOS; Linux/Windows executables are compiled only.

`FullEnumAbiChecks.Run(nint alreadyOwnedNativeHandle)` exercises **real unchanged
BRUTAL imports**, not echo replacements:

- SetMouseCursor/GetMouseCursor: -1 None and named Hand;
- Both SortDirection endpoints at 0/1/2, observing actual column and dirty state;
- Closed polyline flag: real draw geometry changes to 12 vertices/18 indices;
- A and Ctrl input: actual native event-queue records (fixture explicitly disables
  the optional macOS Ctrl/Super remapping);
- A+Ctrl+Shift with Repeat|Tooltip: SetNextItemShortcut changes native NextItemData.

`RunMatrix(handle, matrixJson)` checks an independent actual-assembly enum/name
inventory and compiler-returned native representation before **2,706** transport
calls. `RunMatrixNegatives` rejects type/name omission, value/signedness mutation,
high-bit omission and domain mutation. These methods neither load/free a library nor
install a resolver. The harness owns the only native image and one BRUTAL resolver.

## Early real CLR aggregate/reference barrier

`FullProfileAbiChecks.Run(nint alreadyOwnedNativeHandle)` uses real BRUTAL imports
against native-initialized, correctly destroyed fixture objects. Actual macOS CLR
calls pass for **all five** aggregate-return families used by the 54 imports:

| Shape | Real mapped calls / discriminating data |
|---|---|
| float2 | ImRect_GetCenter_internal, nonzero mixed-sign rectangle |
| floatRect | ImGuiDockNode_Rect_internal, native initialized Pos/Size |
| float4 | ImRect_ToVec4_internal, all four differing coordinates |
| ImTextureRef | ImTextureData_GetTexRef, native object identity; high-bit `fedcba9876543210` texture identity resolved by real GetTexID, plus direct ID route |
| ImGuiListClipperRange | Both real static factories, negative/nonzero bounds, true/false Bool8 and signed -7/+6 offsets; synthetic target remains null |

It also tests managed Rect/Vec interior alias ordering, stable original style/IO and
Bool8 cell addresses, live reference writes, noncanonical Bool8 scalar normalization,
and an actual skip-items Checkbox inout cell with protected neighboring bytes. The
accepted C++ callback/output-only fixtures remain separately active. Each seam
returns an exercise inventory and throws on failure; it never substitutes a success
stub. Passing one representative per aggregate family is **not** exhaustive behavior
coverage for all 54 aggregate-return endpoints or all 1,130 wrappers.

## Supplemental instrumentation, not production exports

The managed test image combines the real selected core/wrappers with these owned
fixtures, all in one native image. It is intentionally distinct from the 1,130-symbol
component artifact. `supplemental-exports.json` inventories **346 additional exports**:

- `profile_fixture_create(ImGuiContext*) -> opaque ProfileFixture*`,
  `profile_fixture_destroy(ProfileFixture*) -> void`;
- `profile_fixture_object(ProfileFixture*, int32 index) -> void*` (borrowed native
  objects and a style subobject; valid only until fixture destruction);
- `profile_fixture_sort_prepare(ProfileFixture*, int32 direction) -> void`;
- `profile_fixture_observe(ProfileFixture*, int32 index) -> int32`;
- `profile_fixture_key_event(ProfileFixture*, int32 index) -> uint64`;
- For each matrix index 0..84: `profile_enum_N(actual storage) -> int32`,
  `profile_enum_constants_N(actual constants enum) -> int32`,
  `profile_enum_raw_N(uint32) -> uint32`, and `profile_enum_rep_N() -> uint32`
  (width plus native/storage signedness bits 8/9).

Fixture creation/preparation performs initialization only. Observers inspect real
native effects of the unchanged imported functions; no production import is replaced.
`enum-fixture.cpp` includes real `imgui.cpp` once, so its combined test-image build
omits the separate engine TU. Later integration must not link duplicate core copies.

The harness freezes/checks all three selected ABI contributor SHA-256/MVIDs against
the accepted mapping, verifies built and staged copies, then verifies their actual
process-loaded locations/hashes/MVIDs before calls. Native and enum-matrix staged
hashes are also checked. `identities.json`, `managed-report.json`, command records and
logs retain the evidence. No host-specific full-contract hash is presented as a
foreign CLR measurement.

## Checked evidence and limits

The durable corrected snapshot is `validation.json`; code/README were frozen in
`fix-freeze/source-freeze.json` before the complete final rerun. The previous
validation snapshot is retained there and all original reports remain untouched.
Scratch evidence used for the corrected snapshot is under:

- `fix-selected-final`: three-target1130-wrapper gates, exact source/mapping receipts;
- `fix-layout-final`: same-source three-target ordinary-storage gates;
- `fix-enums-final`: complete compiler model, matrix, all-target fixtures;
- `fix-managed-final`: actual unchanged-BRUTAL macOS execution and six negatives;
- `fix-tests-final`: **85** profile-specific tests, no skips, including deterministic
  replay, changed callee/signature/kind/static/parameter/location/config/width rejects,
  independent type/value/field/exception omissions, private-alias signedness, path
  redirects/traversal/shared leaves, and source mutation with self-consistent forged
  receipts. Eighteen actual compiler negatives reject width/signedness/value and each of the
  three reproduced use substitutions across the three targets; three compiled
  positive controls cover nested pointer/reference/array/container/wrapper leaves;
- `fix-paths-final`: **212** actual entrypoint/canary cases, zero skips; protected
  input bytes and membership unchanged, no compiler/canary module/destination
  creation in rejected cases (including fresh nested outputs and import/data
  symlink, dangling and hardlink variants);
- `fix-ordinary-build`: successful ordinary checks-project build, no native tool invocation.

Initial development failures are retained, not claimed as passes: Python3.9 rejected
an initial zip strict argument; an intentionally bounded unfiltered CPP AST exceeded
256 MiB and was replaced with filtered AST extraction; an early managed fixture
incorrectly expected a modifier queue canonicalization, corrected after reading the
pinned engine declaration/use behavior. All final checked runs pass.

All 85 baseline source hashes remain unchanged. This isolated copy has no `.git`;
no staging or other Git mutation occurred. Only new owned profile/check files were
added. No sibling component was integrated, no shared build/Program/csproj changed,
and no original-checkout or production/game file was written.

Windows/Linux **runtime execution remains user-owned and pending**; compilation
cannot establish foreign CLR calling conventions. The original 44 unsafe raw
bitfield aliases, opaque lifetimes, StyleVarInfo/RectEntry CLR strides6/7 versus
native4, TextBuffer static alias and all other retained layout/helper gates remain
visible. This component does not make those aliases safe, implement the missing
16 formatting imports/six bridges, or qualify the complete API. Independent reviewer
approval and later parent-owned integration/target qualification are still required.

## Independent-review correction history

The original passing1130/6050/2706/51 results did not detect two P1 defects: a fresh
output could be created inside a selected input tree, and direct-type-only enum
filtering omitted vectors and actual scalar-wrapper uses. Those original evidence
claims are superseded by the immutable-input guard and independent reverse census
above. New concrete removal/mutation tests cover ItemFlagsStack, Shortcut and
SetNextItemShortcut parameter0, both real enum pointer imports, erased managed
Int32 vectors, KeyChord backing, native observation omissions, and nested
pointer/reference/array/vector/span/wrapper paths. The development generated import
observation printf initially had an escaping error; its failed build log is retained
in `fix-enums-dev1` and the corrected full runs pass. No ABI, domain, lifetime or
shared dependency change was required. Independent acceptance remains parent-owned.
