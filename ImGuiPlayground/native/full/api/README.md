# Full non-format API generation component

This directory owns **1,130 real non-format wrappers**, an explicit signature mapping, and the shared C ABI POD/conversion header. It does **not** implement the remaining 16 imports, six dynamic bridges, layout patches/probes, managed helpers, or a managed compatibility verifier. Nothing here is an ordinary .NET build dependency. All explicit build products stay in `ImGuiPlayground/.tmp/native/full-api/`; never promote these pristine-layout libraries to production.

## Inputs and reproducible commands

Run from the isolated repository-shaped root, using Python 3.9+ and the existing Zig **0.17.0** pin. The macOS SDK is required for the pinned declaration extraction and local runtime checks. No download is performed by these scripts.

```sh
# Compile/link, C-header compile, static symbols, deliberate compile failures,
# and native/ctypes execution ONLY on macOS. Child processes have process-tree
# timeouts (10/30s utilities/execution, 120s AST/negative compile, 240s build).
python3 ImGuiPlayground/native/full/api/check.py \
  --rid osx-arm64 --rid linux-x64 --rid win-x64 --runtime

# Quiet generator replay and 31 negative tests; writes explicit evidence only.
python3 ImGuiPlayground/native/full/api/self_test.py \
  --contract ImGuiPlayground/.tmp/native/binding-contract.json \
  --declarations ImGuiPlayground/.tmp/native/full-api/ast-model/declarations.json \
  --output ImGuiPlayground/.tmp/native/full-api/tests

# Quiet output-boundary regressions; all fixture data stays in owned scratch.
python3 ImGuiPlayground/native/full/api/path_test.py \
  --contract ImGuiPlayground/.tmp/native/binding-contract.json \
  --declarations ImGuiPlayground/.tmp/native/full-api/ast-model/declarations.json \
  --output ImGuiPlayground/.tmp/native/full-api/path-tests
```

`check.py` verifies the existing source lock/config and checks all root `.h`/`.cpp` files against the **verified pinned archive** before compilation. It excludes `src/PlaygroundBrutalAdapter.cpp`; the old prototype must not be linked alongside the generated definitions. Every core, wrapper and test translation unit uses `-fno-strict-aliasing`. Normal builds never run this script.

Generation can be performed separately:

```sh
python3 ImGuiPlayground/native/full/api/declarations.py \
  --source ImGuiPlayground/.tmp/native/source/031a18c417158427217bc5890e0ec0cb7e7b4b63 \
  --output ImGuiPlayground/.tmp/native/full-api/ast-model
python3 ImGuiPlayground/native/full/api/generate.py \
  --contract ImGuiPlayground/.tmp/native/binding-contract.json \
  --declarations ImGuiPlayground/.tmp/native/full-api/ast-model/declarations.json \
  --output ImGuiPlayground/.tmp/native/full-api/generated
```

For independent replay, use new `replay-ast`/`replay` scratch output directories, then `cmp` their `declarations.json`, `wrappers.cpp` and `coverage.json` against the first run. These normalized artifacts are byte deterministic. Linked binary reproducibility is **not** claimed (e.g. Mach-O UUID/PE timestamp generation is outside this generator).

Input provenance:

- Reflection JSON: `20e417b6312f8d28ba282bd0dc641e7b2dbea4b5d378b40c42a50c390b0af89e`.
- Selected Brutal.ImGui DLL: `b76777a4ef3399d6353b9dba1982e3afb84b5da2aa2500da1c062db0bfad827c`.
- `imgui.h`: `16a258c0724c0dc7a249347c3f58170a432d0a202abbbed766959ee48e7ed3f1`.
- `imgui_internal.h`: `88ab1cb4e376e7a7789eb04481dc0b315f88b765d299c800d69a36dc45f9ffec`.
- Existing config: `18e6ca90823b36a891eca1c182eba6deb8a35d64431d03db3b91477a1e5049f3`.
- Portable declarative API projection: `3e3cd8793d90fcfd351ef14c9179446d56ec27c859fda78027a54c96804330eb`.

The selected three BRUTAL assembly hashes/MVIDs are separately checked in `mapping.json`. Runtime/framework file hashes are **not** compared against macOS values on other target hosts.

## Deterministic interfaces (all schemaVersion 1)

### Consumed reflection contract

`playground_imgui.managed-binding-contract` v1, from the actual selected assembly. Consumed fields:

- `host.pointerSizeBytes`/`endianness`: this finite profile requires 64-bit little-endian; host RID/runtime is provenance, not a cross-target measurement.
- `assemblies`: selected-directory SHA-256/MVIDs must equal the owned three-assembly provenance.
- `imports`: complete unique entry points, library, DllImport policy, exact return and ordered parameter types/names, signature shapes, attributes/defaults/modifiers. Current import parameters have no extra marshalling metadata; unrecognized metadata fails, never disappears.
- `types`: identity/kind, scalar/enum signedness and declarative graph enter the structural hash; imported enums must be signed `System.Int32`. All generic-vector type identities remain explicit in mappings.
- `dynamicExports`: six reviewed supplement records are passed through with manual ownership and `implemented: false`.

`playground_imgui.portable-api-projection` is canonical JSON (`sort_keys`, two spaces, ASCII escaped, trailing newline) containing `imports`, `types`, `dynamicExports`, recursively omitting exactly `generate.OMIT`. These exclusions are host measurements (`measurement`, `marshalerMeasurement`, offsets, widths, overlaps), trace tokens/MVIDs, assembly identities/framework provenance, source/disposition observations and non-ABI ref-return property inventory. Assembly-qualified attribute strings retain the full type name but omit assembly/version information. All remaining declarative data, including ordered field/signature types, layout declarations, attributes and enum constants, remains hashed. This is **not a layout parity hash**. The full original JSON SHA is retained separately as `inputContractSha256`. A positive mutation fixture changes host observations and framework hashes without changing generated output; real signature/field declarations remain gated.

### Compiler declaration model

`playground_imgui.pinned-native-declarations`: compiler/target description, two header hashes, config hash, five callback typedef signatures, fixed native enum-underlying types and a sorted declaration list. Each declaration records exact scope, name, complete compiler `qualType`, ordered native parameter types/names, const/reference distinctions, kind, static/variadic status and header/line evidence. No AST addresses, paths or compiler-generated IDs enter deterministic output.

`declarations.py` obtains Zig's exact embedded Clang `-cc1` invocation from `zig c++ -###`, then invokes that pinned frontend with `-ast-dump=json`. Zig 0.17.0's driver returns an object-rename error for AST-only modes; only that precise driver error is recognized, and the actual frontend must independently succeed. Raw driver/AST command/logs remain scratch evidence. No failed compiler AST is consumed.

This is an AST consumer, not a C++ name-grep parser. It excludes template declarations/instantiations and out-of-line method definitions in favor of their canonical in-record declarations. Every required target must still exist exactly once. Source locations must match an exact token/offset in one pinned header; ambiguity fails. None of the current imported targets requires unsupported templates.

### Owned mapping

`playground_imgui.nonformat-mapping`, `mapping.json`: input pins, selected assembly provenance, portable projection, exact DllImport policy, callback typedefs and **1,130 explicit records**. Each record binds:

- exact exported name and full managed signature;
- exact native scope/name/full signature/ordered parameters and declaration evidence;
- explicit member/static-target classification;
- expected ABI signature, per-argument conversion rule/expression and return conversion;
- opaque callback signature, calling convention, lifetime and native parameter type;
- `boolPointerParameters`: exact index/name, direction (`inout`/`out`), optional/required null precondition, borrowed lifetime, canonical shared-write policy and native source-access evidence. This required per-import direction manifest is also emitted into coverage evidence; missing/duplicate/unsupported or changed direction recipes fail. Alias/shared-write policy weakening is rejected even when the stored evidence is changed alongside the policy.

Production generation performs **no suffix stripping, overload-index inference or name fallback**. The finite records already resolve every overload. Every call uses a compiler-checked exact free/member function-pointer `static_cast`, including const method qualifiers. Ordered managed/native parameter names also match at every forwarded position. Missing, duplicate, ambiguous, changed or unsupported mappings fail before output writing. `mapping.json` is an owned review input, not a file to regenerate automatically when inputs drift.

### Generated outputs

- `wrappers.cpp`: exactly one exported definition per non-format import; includes `purr_abi.h`. Calls only real selected upstream operations, with no missing-symbol stubs.
- `coverage.json`, schema `playground_imgui.nonformat-coverage`: full input/code/header/mapping/wrapper hashes, portable projection, exact per-import source/ABI/target/conversion/callback evidence, 1,130 implemented records, 16 reserved import records and six reserved dynamic records. It records the mandatory all-TU `-fno-strict-aliasing` flag and the limited scope of qualification.
- `check.py`: per-RID actual command JSON, compiler logs, full symbol-table logs, expected-failing signature compile logs and `result.json`; combined `results.json`. Actual tables have exactly 1,130 owned functions. Mach-O additionally exposes the linker metadata symbols `__dso_handle` and `_mh_dylib_header`; these are not imports or compatibility coverage.

All inputs must pass before the generator starts output replacement. The real malformed-CLI fixture verifies a previous wrapper file is not overwritten on validation failure. This is not a multi-file transactional publisher; integration should continue to use scratch staging.

### Corrected scratch-only output boundary

All API writer entry paths (`generate.py`, `declarations.py` CLI/`produce`, `check.py`, `self_test.py`, and the path-fixture runner) are restricted to **this physical project's `.tmp/native/full-api/` subtree**. This intentionally narrows the formerly permissive `--output` interface. Use physical absolute or root-relative paths without `..`; do not resolve an untrusted output path before calling these APIs. Symlinks—including redirects to another directory inside the allowed subtree—dangling redirects, Windows reparse points, shared/hardlinked file leaves and non-regular file leaves are rejected. Contract/pristine source/config/source-code locations are outside the output subtree; input/output collisions inside the subtree are also rejected.

`output_paths.py` validates root, every ancestor, every directory and **every artifact leaf in the entire planned operation before any mkdir/write/compiler invocation**. This includes AST files, wrapper/coverage pairs, logs/command records, compiler outputs and Windows `.lib`/`.pdb` sidecars. Only after preflight does it reuse the existing builder's `project_scratch`/`safe_child` creation rules. It never erases redirects via output `resolve()`. Local imports disable bytecode-cache writes. Existing redirects are in scope; concurrent hostile filesystem swaps and arbitrary subprocess/Python sandboxing are not.

`path_test.py` covers all declared artifact leaves of all four writers with symlink, dangling and hardlink fixtures, root/ancestor/destination redirects, in-root redirects, protected-input collisions and real CLI rejection. It asserts unchanged protected markers, no partial/temp writes and no early tool invocation. Normal canonical reuse and independent replay remain supported. Symlink privilege skips on Windows would be explicitly recorded rather than counted as passes; current recorded execution is macOS only.

## Shared ABI header contract

`purr_abi.h` is directly consumable as C11 or C++11. C mode exposes only fixed-width POD declarations and `PURR_EXPORT`/`PURR_API`; C++ mode adds the pinned native declarations and owned `purr` conversions. The manual format/bridge owner should **reuse these exact types**, not define another spelling/layout:

| Managed aggregate | C ABI POD | Native value conversion |
|---|---|---|
| float2 | `PurrVec2 { float x,y; }` | `ImVec2` |
| float3 / float4 | `PurrVec3` / `PurrVec4` | scalar-array view / `ImVec4` |
| floatRect | `PurrRect { PurrVec2 Min,Max; }` | `ImRect` |
| int2/3/4 | `PurrInt2/3/4` | fixed-width scalar-array view |
| ImTextureRef | `PurrTextureRef { void* TexData; intptr_t TexID; }` | fieldwise `ImTextureRef` |
| ImGuiListClipperRange | `PurrClipperRange` | int32 Min/Max, uint8 bool, two int8 offsets; native value return |

`purr::to_native`/`from_native` convert values fieldwise; `purr::bits<To>` is a same-width trivially-copyable memcpy bit conversion. Single-field ImGuiID, ImFontAtlasRectId, ImGuiKeyChord, ImTextureID and Bool8 use their explicit fixed-width scalar ABI. The two ImGuiSortDirection scalar endpoints need a reviewed width adaptation: managed signed Int32 versus pristine native `enum ImGuiSortDirection : ImU8` (unsigned 8-bit). Both wrappers cast directly through the actual native type; valid 0/1/2 values are tested, and no new out-of-range policy is imposed. `scalarAdaptations` records these endpoints and widths. Generated width/signedness assertions derive from the selected AST enum-underlying records, not an unconditional common-header width1 invariant. The layout owner's proposed widened sort-direction declaration will require new header/profile receipts and review/regeneration before integration; pristine width1 evidence is not the final patched ABI verdict. Texture-ID and unsigned-managed/signed-native selection-user-data bit patterns are preserved; platform-dependent C++ `long` is never the managed 64-bit ABI spelling.

**Supervisor-approved narrow pointer representation contract:** float2/float4/floatRect pointer/reference views preserve actual addresses, null pointers, array stride, mutable outputs and overlapping Rect/Vec subviews. Size, alignment, each field offset, standard-layout and trivially-copyable properties are compiler asserted. Related float/int scalar-array views have explicit size/alignment/offset assertions. By-value arguments/results still convert fieldwise. This pinned-compiler FFI contract requires `-fno-strict-aliasing` on **all core/adapter/probe TUs**; the flag alone does not establish arbitrary C++ lifetime or make unrelated native types safe. Caller storage must have the proven representation/alignment and valid lifetime. No arbitrary nontrivial native objects are copied into shadows to hide layout conflicts.

**Corrected Bool8 pointer contract:** the old shadow `BoolCells` implementation was defective and is removed. A registered size-constraint callback could close the original `Begin.p_open` cell, after which copy-back restored stale `true`; `BeginPopupModal` could also re-read the stale shadow after nested Begin. Global allocator callbacks mean callback reachability cannot be inferred from an endpoint's immediate signature.

The parent-approved fix extends the narrow pinned-compiler storage-view contract to **all 21 borrowed Bool8 pointer parameters across 18 imports**. `purr::bool_inout` normalizes nonzero entry bytes **in place**, then passes the original address. `purr::bool_output` passes the original address without reading, normalizing, initializing or copying back the cell. Null is passed through unchanged; required native pointer preconditions remain required. `sizeof`, alignment, standard-layout and trivial-copy properties plus compile-time bit-cast checks of actual false=0/true=1 bytes gate the selected compiler/targets; macOS also exercises byte representations at runtime. All relevant TUs retain `-fno-strict-aliasing`. This is not a general standard-C++ object-lifetime theorem.

**While native bool execution shares a cell, caller/callback writes must be canonical 0/1.** Initial noncanonical input bytes are supported by entry normalization, but arbitrary noncanonical callback writes are not made safe. All aliases, including callback/UserData aliases, retain the original address. There is no temporary, destructor or copy-back to hide or discard callback writes. Returned native `bool*` storage (GetBoolRef) follows the same canonical-write constraint and usual native invalidation rules. Retained-pointer endpoints outside this finite borrowed inventory require a new review.

| Finite Bool8 pointer inventory | Direction | Null precondition |
|---|---|---|
| Begin, BeginDocked_internal, BeginPopupModal, BeginTabItem, TabItemEx_internal, ShowAboutWindow, ShowDebugLogWindow, ShowDemoWindow, ShowIDStackToolWindow, ShowMetricsWindow: `p_open` | inout (10) | optional |
| CollapsingHeader_1.`p_visible`, MenuItem_1.`p_selected` | inout (2) | optional |
| Checkbox.`v`, Selectable_1.`p_selected`, MultiSelectItemHeader_internal.`p_selected`, MultiSelectItemFooter_internal.`p_selected`/`p_pressed` | inout (5) | required |
| ButtonBehavior_internal.`out_hovered`/`out_held` | out (2) | optional |
| TabItemLabelAndCloseButton_internal.`out_just_closed`/`out_text_clipped` | out (2) | optional |

Inout includes endpoints that preserve the caller's entry state on non-write paths, not only functions that directly read it. Every record has pinned native definition/line evidence in the mapping. No input-only or retained Bool8 pointer endpoints were found. `bool_view_proof.cpp` compiles on all RIDs to LLVM IR: the output adapter body must be **only `ret ptr %0`**, with no load/store/call. Generated-source checks require exactly four output adapters and 17 inout adapters. Real native output-only paths use uninitialized bytes, optional nulls, aliases and early returns; these runtime fixtures supplement rather than replace the no-load compiler proof.

Allocator getter outputs still use typed locals and native alloc/free/user copy-out order, preserving aliases even between callback/data output cells; this independent existing fix is unchanged.

Callback pointers use exact native typedefs or full compiler prototype types; all 18 opaque callback parameters across 16 imports carry lifetime records. AddCallback preserves `userdata_size` and sentinel **-8**. All five closed ImVector signatures use their actual compiler element types (ImWchar/ImGuiID, ImDrawList*, ImGuiWindow*, const char*), without shadow-vector copies; native object/container layout qualification remains the layout owner's gate.

The 12 draw-list leading-underscore aliases and two static factories are explicit. Static factory synthetic targets are never dereferenced. Six native reference returns retain addresses. Aggregate returns are exactly **37 float2 + 12 floatRect + 2 float4 + 1 ImTextureRef + 2 ImGuiListClipperRange = 54**. Of the 287 scope-member mappings, 285 are actual instance methods and two are static factories; the separate target-named DockContext function remains a namespace function.

## Exact reservations and integration boundary

Reserved imports (not emitted): `BulletText`, `DebugLog`, `LabelText`, `LogText`, `SetItemTooltip`, `SetTooltip`, `Text`, `TextColored`, `TextDisabled`, `TextWrapped`, `TreeNode_1`, `TreeNode_2`, `TreeNodeEx_1`, `TreeNodeEx_2`, `TextAligned_internal`, `TextV`. All six dynamic supplement exports are also reserved. The manual owner must preserve valid zero-extra-vararg formatting and real per-target va_list behavior, not literal-forwarding substitutes. Callback-slot null behavior is not chosen here.

`validation.json` is the durable checked snapshot: exact code/generated hashes, actual per-RID commands and full compiler/symbol/expected-failure/runtime logs, generator test names, and the 51-file baseline-preservation check. It is evidence, not a production runtime pin.

All three RID compile/link, static export, C11 header and expected-negative-compile gates passed. Only macOS native fixtures and actual-library ctypes smoke execute locally. The fixtures cover Rect/Vec interior aliasing, arrays/output mutation, original-address Bool8 views, exported Begin and BeginPopupModal callback-alias comparisons against direct native calls, genuine output-only paths, reference identity, mixed/float aggregate returns, high-bit scalar wrappers, real allocation callbacks, aliased allocator outputs, SortDirection values 0/1/2 on both scalar endpoints, and AddCallback retention/sentinel metadata. They do **not** validate BRUTAL managed calling ABI, exhaustive widget behavior or patched native/managed layouts. Windows/Linux execution remains pending.

Next: independent review of this finite mapping/conversion seam; then the integration owner combines the generated TU/common header with manual adapters and reviewed layout patches. This component generates from pinned pristine declarations and tests pristine core. A patched-header declaration model must not silently replace these pins; integration must validate its patch receipts and rerun all exact typed call sites and layout gates. Original overlapping managed aliases, including StyleVarInfo stride 6 and RectEntry stride 7, remain unsafe. No root projects, shared builder/exporter, runtime assets/pins or external assemblies were changed.

Local analyzer disposition: initial pi-lens `identifier` diagnostics on `purr_abi.h` compiler macros `_WIN32`/`__cplusplus` and upstream `_TexData`/`_TexID` uses were supervisor-approved false positives (uses, not owned declarations). Header include resolution is configured by the component-relative `.clangd`; no global suppression or upstream renaming was applied. Pinned compiler shape checks and actual builds remain authoritative. Independent reviewer approval is still required. The corrective follow-up also received narrow supervisor confirmation that USE of pinned `__is_trivially_copyable(bool)` / `__is_standard_layout(bool)` intrinsics is not an owned reserved-identifier declaration. Those properties are now checked through the existing common shape macro (no blanket diagnostic suppression).

Pending enum/flag named-value, valid-flag, negative-sentinel and applicable high-bit transport work remains separate and was not started by this corrective slice. Associated private constants-enum backing must not be mistaken for an actual int-aliased parameter/return's storage. Layout-patched source/profile regeneration (especially SortDirection), managed ABI verification, and Windows/Linux execution are still required integration gates. The correction does not qualify the full API.
