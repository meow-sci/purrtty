# Combined native artifact — bounded integration milestone 1

Explicit maintainer tooling only. This joins the reviewed API, layout/profile,
manual and accessor components into **one production image and one separately
inventoried fixture image per target**. It is not production promotion, the final
portable verifier/runner, or renderer/input/capture/packaging qualification.
Ordinary .NET builds perform **no native compilation**. Nothing under `runtimes/`,
fonts, external managed inputs, or the original repository is replaced.

## Reproduce

Run from the physical isolated repository-shaped root, with Python 3.9+, Zig
0.17.0, the macOS SDK, `nm`/`objdump`/`file`/`otool`, and .NET 10. The pinned archive,
pristine source and binding contract must already be in `.tmp/native`; there is no
download or game-directory fallback. Use new sibling output directories each time.
The names below are the final evidence names for this milestone; choose different
names if they already exist.

```sh
export PYTHONDONTWRITEBYTECODE=1
P=ImGuiPlayground/native/full/profile
A=ImGuiPlayground/native/full/accessors
C=ImGuiPlayground/native/full/combined
P_OUT=ImGuiPlayground/.tmp/native/full-api/profile
A_OUT=ImGuiPlayground/.tmp/native/accessors
C_OUT=ImGuiPlayground/.tmp/native/combined
KSA=/Users/asherwin/repos/meow-sci/ksa-game-assemblies/current/dll

python3 "$P/selected_profile.py" --output "$P_OUT/combined-final-selected"
python3 "$P/enums.py" --selected "$P_OUT/combined-final-selected" \
  --output "$P_OUT/combined-final-enums"
python3 "$P/layout_gate.py" --selected "$P_OUT/combined-final-selected" \
  --output "$P_OUT/combined-final-layout"
python3 "$A/run.py" --out "$A_OUT/combined-final-build"

python3 "$C/run.py" build --selected "$P_OUT/combined-final-selected" \
  --enums "$P_OUT/combined-final-enums" --layout "$P_OUT/combined-final-layout" \
  --accessors "$A_OUT/combined-final-build" --output "$C_OUT/final-build"
python3 "$C/run.py" managed --build "$C_OUT/final-build" \
  --managed-directory "$KSA" --output "$C_OUT/final-managed"
python3 "$C/run.py" test --build "$C_OUT/final-build" --output "$C_OUT/final-tests"
```

`build` always builds/inspects all three closed targets. The ordinary native source,
wrappers, enum matrix/census and generated accessor body are revalidated; accessor
generation is replayed into a **new valid sibling**
`.tmp/native/accessors/combined-<combined-output-name>-replay`. This intentionally
uses the accepted producer's output guard, not an override. The selected profile,
enum, layout and accessor trees remain immutable inputs.

`managed` builds the **actual Checks project and its Host ProjectReference** with
`--artifacts-path` and warnings-as-errors. It then copies that ordinary output into
a disposable `stage/`, replacing only the stage's `libimgui.dylib`. It does not
compile the helper/check sources into a substitute harness. The minimal opt-in
Checks dispatch is `--combined-components <profile|manual|accessor|composition>
<stage-receipt.json> <report.json>`; use the Python tool to establish and validate
the build/stage receipts before invoking it. Ordinary no-argument Checks behavior
is unchanged. `KSAFolder` takes precedence over `--managed-directory`, then
`KSA_DLL_DIR`; unrelated environment variables are preserved.

Affected standalone regressions (marker absent) remain required:

```sh
python3 "$P/managed.py" --selected "$P_OUT/combined-final-selected" \
  --enums "$P_OUT/combined-final-enums" --output "$P_OUT/combined-final-managed" \
  --ksa-folder "$KSA"
python3 "$P/self_test.py" --selected "$P_OUT/combined-final-selected" \
  --enums "$P_OUT/combined-final-enums" --output "$P_OUT/combined-final-tests"
python3 "$P/path_test.py" --selected "$P_OUT/combined-final-selected" \
  --enums "$P_OUT/combined-final-enums" --output "$P_OUT/combined-final-paths" \
  --ksa-folder "$KSA"
python3 "$A/run_managed.py" --build "$A_OUT/combined-final-build" \
  --out "$A_OUT/combined-final-managed" --managed-directory "$KSA"
python3 "$A/self_test.py" --build "$A_OUT/combined-final-build" \
  --managed "$A_OUT/combined-final-managed" --out "$A_OUT/combined-final-tests"
```

Accessor self-tests automatically exercise the corrected case-insensitive test-child
environment isolation; production selection precedence is not changed. The final
accessor suite is also run with both valid selection aliases exported.

## Exact image inventories and ownership

`exports.json` is derived from accepted producers, not from prefixes guessed at
runtime. Every row names its component, production/fixture classification and kind.
Every binary's **complete defined external export list** must match; Mach-O's two
linker bookkeeping symbols are separately excluded. No new integration metadata
export is added.

| Component | Production | Fixture-only |
|---|---:|---:|
| Selected-profile typed API wrappers | 1,130 | 0 |
| Manual fixed imports | 16 | 0 |
| Dynamic bridge getters + slots | 6 | 0 |
| Supported accessor functions | 23 | 0 |
| Profile object observations + generated enum transport | 0 | 6 + 340 |
| Manual actual-call fixtures | 0 | 6 |
| Accessor native-object/audit fixtures | 0 | 19 |
| **Total** | **1,175** | **371** |

Thus the fixture image has **1,546** owned exports, including the complete unchanged
production surface. Exactly three production exports are aligned writable pointer
slots; all others are code. The existing binary-section inspector checks every
owned export on Mach-O, ELF and PE, including slot width/alignment/nonoverlap.
Functions relocated to data and slots relocated to code are rejected without
executing corrupted images. Architecture and dependency inspector logs are retained.
Mach-O install ID is `@rpath/libimgui.dylib` (separate from its libSystem dependency);
Linux needs libm/libc; Windows records the actual UCRT API sets plus
KERNEL32/SHELL32/USER32. No foreign dependency execution is inferred.

### Approved CPP-local composition

The supervisor explicitly approved **only** guards around the two owned literal
`#include "imgui.cpp"` lines: accessor `fixture.cpp` and the enum-fixture generator
prologue. With the marker absent, standalone behavior is unchanged.
`fixture-core.cpp` includes the verified patched real core once, defines
`PURR_COMBINED_CORE_INCLUDED` locally, includes the accessor and generated enum
bodies once, and undefines the marker. Supplying it globally is a compile-time
error. The actual configured ImStb/private declarations remain intact. There is no
namespace wrapping, copied native type, changed native algorithm or engine shim.

The fixture link omits a separate core object and both included bodies as separate
TUs. Manual/profile object fixtures remain separate TUs. Production instead links
one ordinary `imgui.cpp` object, the four other locked core files (including demo),
wrappers, manual and accessors; it has no fixture bodies. The prototype adapter is
never linked. All TUs use the same exact patched source/config and the accepted
`-fno-strict-aliasing`, visibility, exception/RTTI/static-init and target flags.
Commands are retained per TU, including C11 and actual target `va_list`/bridge
signature proofs. No C++ function-pointer cast hides a signature mismatch.

`fixture-core.ii` and `include-ownership.json` record actual preprocessor entry
markers for the core and both fixture bodies exactly once. Unlike `-H` stderr,
these remain available on Zig cache hits. Full object symbol tables show exactly
one **defined** `GImGui` per linked object set; PE `.refptr.GImGui` is a reference,
not a second engine. The explicit pre-link ownership validator rejects a real
additional core object on every target. Do **not** rely on duplicate-symbol linker
errors: the development Mach-O duplicate-core experiment unexpectedly linked under
Zig. That unused artifact/log is retained in `dev-build3`; it was never loaded.

## Actual component calls and evidence-before-access

Four fresh bounded macOS CLR processes use the exact same combined fixture SHA:

* `profile`: unchanged FullProfileAbiChecks plus all FullEnumAbiChecks entrypoint,
  2,706 typed/raw transports and six matrix-negative semantics;
* `manual`: unchanged FullManualAbiChecks (23 format, two true TextV, three rooted
  bridge callbacks), including its explicit Interop same-artifact assertion;
* `accessor`: unchanged FullAccessorChecks through the actual built Host helper:
  all 47 fields, 13 opaque routes, neighbors/strides/lifetimes/borrow invalidation,
  and the original allocation-audit restoration semantics;
* `composition`: independently observes the wrapper, manual, profile and accessor
  contexts/allocator callbacks in the same image, including nested accessor context
  restoration and allocator callback **and user-data** restoration.

A separate C++ executable also runs the existing manual native tests against the
actual combined fixture image, never a per-component substitute. Component-only
standalone images are used only by the explicitly separate regression commands.

Before loading native code each CLR process checks selected, built, staged and
actually loaded contributor SHA/MVID/location, including the built Checks and Host
assemblies. It measures **528 actual closed CLR shapes and 2,211 field offsets**
against the same host contract used by the exhaustive native layout gate. That
same-source gate retains 6,050 comparisons, all 57 known conflicts, enum/callback/
aggregate classifications and no success-by-ignore conversion of unsafe aliases.
The contract SHA is macOS host provenance, not a universal foreign measurement.

The stage receipt binds matrix/helper/layout/contract/inventory/native bytes.
`dladdr` checks all **1,546 addresses** against the one staged native path and source
SHA; dyld image enumeration checks no earlier ImGui image and only that image after
calls. Each process installs exactly one BRUTAL resolver and leaves its native
references alive to process exit, including Interop's explicit loader reference.
No renderer/PlaygroundHost startup occurs in this dispatch.

In the accessor process, no ImGui allocation/context/object/function call precedes
FullAccessorChecks' audit begin: only managed measurements and native loader/export
address inspection occur. Other component state is in other processes. The observed
accessor interval is 639 allocations/639 frees, seven null frees, zero failed/error/
outstanding events and exact restoration. The composition interval observes
41/41 and zero outstanding. **These are callback-observed bounded intervals, not
CRT/STB/OS/global leak freedom.** A deliberately invalid staged accessor contract
fails inside the audit and proves restoration before propagation.

## Receipts, freeze, guards and negatives

`manifest.json`, `source-freeze.json`, `exports.json`, per-target ownership/section/
architecture/dependency/command logs, and `compile_commands.json` form the native
handoff. `manifest.json` binds source receipt, config, helper identity, contributor
evidence hashes and both artifact hashes for all targets. Managed `results.json`
binds the native manifest, four process IDs/report hashes and five negative cases.
All reports retain source/staged/actually loaded identity, not just successful
export lookup. `seed-delta.json` inventories each original seed hash and its current
hash; only the two guards and explicit Checks dispatch adapt existing seed files.
Accessor ABI identity remains
`e86ced3fb638c94c285378697c77e6e723bd75864c337e2b557449b4e4677f72`:
the test-only fixture is not part of that production identity's input closure.
Its full component build receipt nevertheless records the new fixture source hash.

`run.py` uses only stdlib before lexically checking the entire selected project
source/import/cache closure, fixed inputs, selected trees and complete fresh output
locations. Redirects (including dangling/reparse), shared/nonregular leaves, parent
traversal, overlaps and existing output roots fail before project import/read/mkdir/
compiler work. Accepted producer guards remain in force. `test` adds 34 bounded
inventory/identity/actual-entrypoint checks with working compiler/import markers;
compiled ownership/kind negatives and the managed negatives are additional gates.
No hostile concurrent-swap or system SDK/toolchain sandbox guarantee is claimed.
No fixed sleeps, background build hooks or external staging edits are used.

Development failures are retained rather than reported as passing: an attempted
reuse of the accepted API symbol helper correctly rejected this different output
root (replaced by owned inspection, not a guard bypass); cached `-H` stderr required
using `.ii` markers; the duplicate-core Mach-O link was not a reliable oracle; a PE
refptr was initially overcounted; and the initial composition report used an
unsupported IntPtr JSON value (changed to observed integer addresses). Final
commands/results and the pre-final source freeze are indexed by the run handoff.

## Diagnostics and strict remaining gates

New Python/C# source diagnostics and actual project builds are checked. The
combined-local `.clangd` points to `final-build/compile_commands.json`; the explicit
changed combined TU probe against the actual patched database reports zero errors.
The accepted accessor database probe has no parsing/type errors but **12 existing
ExtractFunction refactoring self-test failures**. The profile bootstrap retains its
pre-existing E402/I001 import-order findings: validating before local imports is
intentional, and the supervisor explicitly prohibited rearrangement or broad
suppression merely to silence them. Initial missing-include editor diagnostics
came from absent/stale databases, not a pristine-header fallback. No global clean
analyzer claim is made; compiler macro uses are not owned reserved declarations.

This frozen milestone needs independent parent/reviewer reproduction before any
integration/promotion. Windows/Linux are **cross-built and inspected only**. Full
portable verification, all API behavior, foreign CLR calling conventions, complete
macOS staged renderer/input/capture/extraction/ordinary packaging and user-owned
foreign-host execution remain later gates. The original packed aliases, CLR6/7
strides, Size1 placeholders and static-field conflict remain unsafe. Typed enum
transport is not exhaustive engine-domain validation. No production assets or
external managed assemblies are modified or distributed, and no nonexistent final
runner command is advertised. `VALIDATE_PLATFORMS.md` is deliberately unchanged.
