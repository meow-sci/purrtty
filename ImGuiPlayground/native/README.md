# Maintainer-only Dear ImGui native tooling

The [full qualification runner](full/qualification/README.md) now validates the complete
combined profile and runs actual Host/Checks fixture and production-image renderer/input/
capture gates. Its independently anchored portable bundle requires no native compiler on
consumer hosts. macOS automated gates have been exercised; Windows/Linux execution remains
user-owned pending. No full or prototype artifact is promoted into shipped runtimes.

## Historical bounded prototype

This folder is an **explicit, bounded layout/ABI feasibility prototype** for the standalone
`ImGuiPlayground/` and `ImGuiPlayground.Checks/` pair. It is not the complete 1,146-import
BRUTAL binding, is not a replacement runtime, does not update the app's prebuilt native pins,
and is never invoked by ordinary `dotnet build`/`dotnet publish` commands.

All source, config, scripts and notices for this experiment are here. Downloaded sources,
compiler outputs, logs, native dependencies reports, and build records stay in the ignored
`ImGuiPlayground/.tmp/native/`. **Do not copy a prototype output into `runtimes/` or change
`runtimes/NativeLibraries.props` as part of this experiment.** The managed BRUTAL assemblies
remain external. No game files or private decompilation implementations are included.

## Pinned inputs and maintainer command

The upstream input is the peeled source commit `031a18c417158427217bc5890e0ec0cb7e7b4b63`
from `v1.92.2-docking`; annotated tag object `04c3466d23a72abee3696dcba698b0e02fee6057`
is recorded separately because it is **not** the source commit. `source.lock.json` pins the
immutable codeload URL and archive SHA-256 `2d1c175790496529ed8e90e0fba5b9dc852769f959edb9ef145e8f82d4dcc3be`.
The builder re-hashes a cached archive and safely re-extracts it into local ignored scratch on
every requested build; it does not depend on `/tmp`, another checkout, or a user-supplied
machine path. It rejects bad/missing source/config pins, unknown targets, unsupported archive
entries, unsafe scratch paths, and unexpected compiler versions. Both `.tmp` and `native`
components are checked before creation/resolution; redirected components and metadata leaf
symlinks (including dangling links) are rejected. This is maintainer path validation, not a
sandbox against concurrent hostile filesystem mutation.

Prerequisites for this **maintainer-only** path: Python 3.9+, `curl`, Zig **0.17.0**, and—for
the macOS target—an Apple macOS SDK discoverable through `xcrun`. The currently known compiler
is Zig 0.17.0. No Xcode path is embedded in the procedure. The script checks its compiler pin
before acquiring source. From the `ImGuiPlayground/` folder:

```bash
# No download or compiler: negative/positive checks for the source lock, config hash,
# archive digest handling, unsupported RID, and scratch path confinement.
python3 native/build.py --self-test

# Explicit native maintainer builds. Each produces only ignored .tmp scratch outputs.
python3 native/build.py --target osx-arm64
python3 native/build.py --target linux-x64
python3 native/build.py --target win-x64
# Or build all three sequentially:
python3 native/build.py --all
```

Outputs and records:

```text
.tmp/native/osx-arm64/libimgui.dylib
.tmp/native/linux-x64/libimgui.so
.tmp/native/win-x64/imgui.dll
.tmp/native/<rid>/build.log
.tmp/native/<rid>/dependencies.txt
.tmp/native/<rid>/build.json
.tmp/native/source/<peeled-commit>/    # re-extracted verified upstream snapshot
```

Build failures retain the target log with compiler output. `process_utils.py` provides the
builder and staged runner with bounded child-tree cleanup on timeout, interruption, or other
exceptions. Compiler/game-free regressions live in
`../../ImGuiPlayground.Checks/prototype_tooling_checks.py` (quiet on pass). The script writes to a staging file
and atomically replaces only the target's `.tmp` artifact on success. Nothing is staged or
promoted to the current runtime directories. Build records carry source/config/archive hashes,
Zig version, target triple, byte size, SHA-256, `file` output, and dependency-inspector output.
Hashes of different target artifacts are naturally different; this prototype does **not**
claim reproducible byte-for-byte rebuilds.

The build compiles these five unmodified upstream core/demo translation units plus the owned
`src/PlaygroundBrutalAdapter.cpp` into one shared library. It compiles **no** C++ rendering backend,
platform backend, or GLFW. The build records currently show these cross-build dependencies:

- `osx-arm64`: `@rpath/libimgui.dylib`, `/usr/lib/libSystem.B.dylib` (the `@rpath` entry is the
  dylib's own install name, not an external dependency).
- `linux-x64`: `libm.so.6`, `libc.so.6`; the inspected output references GLIBC 2.2.5, 2.3, 2.7,
  and 2.14, while the selected compile target is `x86_64-linux-gnu.2.17`.
- `win-x64`: Windows API-set UCRT imports `api-ms-win-crt-{convert,heap,math,private,runtime,stdio,string,utility}-l1-1-0.dll`, plus `KERNEL32.dll`, `SHELL32.dll`, and `USER32.dll`.

These are **static cross-artifact dependency inspections**, not proof that the target OS can
load or execute them. Only the macOS arm64 artifact has been loaded and called on native Apple
Silicon here. Windows/Linux binaries were cross-compiled/inspected only; no Wine execution,
Windows/Linux execution claim, or graphics-driver test is made.

The current link flags disable exceptions, RTTI, the C++ standard library and thread-safe
function-local static initialization. Upstream and the adapter do not use C++ exceptions or
RTTI, and this host's ImGui-facing work is synchronous on its single UI thread. This choice
avoids adding a C++ runtime dependency; it is a **threading constraint**, not a source patch or
layout tweak. The ordinary standalone host already runs on its main/UI thread. Revisit this
flag before using the prototype in a concurrent or different host.

## Explicit config hypotheses

`include/playground_imgui_config.h` is force-included uniformly for every source translation
unit and hash-checked against `source.lock.json`:

- `IMGUI_USE_WCHAR32` — strongly indicated by the managed `uint` input/glyph-range declarations
  and Linux/Windows native symbol evidence; it avoids the older macOS wchar16 limitation.
- `IMGUI_DISABLE_OBSOLETE_FUNCTIONS` — inferred from current generated `ImFont`/`ImFontAtlas`
  layouts omitting guarded obsolete fields.
- `ImDrawIdx` is left at upstream's 16-bit default; `ImTextureID` is left at upstream's U64
default. No packing, draw-vertex, color, math-type or source overrides are added.

These settings are **hypotheses measured by the native layout probe**, not authenticated KSA
build metadata. They do not establish the exact KSA source revision, wrappers, generator or
all effective config. Do not modify upstream files or suppress/relax the production managed
ABI guard to make results pass. The experiment deliberately reports actual native values and
allows mismatches against external managed types to remain visible.

## ABI/export subset

`exports.json` is the authoritative curated prototype export list and C-signature record.
The rebuilt library has 54 named managed-import exports, three writable data exports, and 15
additional explicit prototype/probe functions (72 symbols total). The maintained subset covers the current
`PlaygroundHost`, `GlfwInput`, `OpenGlRenderer`, `Program` and existing rendering/input checks:
context/frame lifecycle, style, window placement, unformatted text, button, font loading,
input events, draw-list callbacks/rectangles, draw data, texture create/update/destroy helpers,
texture references, ImGui's allocator pair and selected rectangle helpers. It also includes
representative value-shape exercises for scalar one-byte bool, `float2`, `float4`, rectangle,
texture-reference, allocation/deallocation and callback bridging.

The C++ adapter forwards to the pinned Dear ImGui core/demo and explicitly adapts POD shapes;
it is not a list of success stubs. Examples:

- `Begin` copies managed byte `Bool8` into native `bool` storage and copies the optional result
  back. Other input events convert `uint8_t` booleans to native `bool`.
- `SetNextWindowPos`, `SetNextWindowSize` and `ImDrawList_AddRectFilled` translate the
  8-byte two-float managed `float2` values into upstream `ImVec2` inputs.
- `GetWindowPos`, `GetWindowSize`, draw-list clip queries, `ColorConvertU32ToFloat4` and
  `ImRect_ToVec4_internal` return POD values through the native target ABI; the probe exercises
  representative float2/float4/rectangle calls rather than assuming a Windows aggregate rule.
- `Image` accepts the actual 16-byte upstream `ImTextureRef` by value and forwards it to
  `ImGui::Image`; `ImTextureData_GetTexRef`, `ImTextureRef_GetTexID` and draw-command texture
  queries exercise related conversions.
- `MemAlloc`/`MemFree` forward to `ImGui::MemAlloc`/`MemFree`, keeping allocations in the core's
  allocator domain.
- `ImRect_*_internal` reconstructs a real upstream `ImRect` from the managed rectangle POD.

Formatted `TextV` is deliberately not included: BRUTAL packs OS-specific `va_list` state and a
symbol name cannot prove argument-storage compatibility. The current set is **not full BRUTAL
compatibility** and does not implement the other imports/internal functions in the generated
binding. The external binding inventory has 1,146 native imports plus six bridge exports;
this curated prototype omits the remaining imports and all unproven signatures. `exports.json`
records this limit and why `TextV` is excluded.

### Three platform vector callback bridges

`include/playground_imgui_probe.h` declares the writable data symbols and `Get_*_InteropPointer`
getters required by BRUTAL's dynamic trampoline setup:

```c
// These C-compatible mirror types are declared in playground_imgui_probe.h.
typedef struct PlaygroundImVec2 { float x, y; } PlaygroundImVec2;
typedef PlaygroundImVec2 (*PlaygroundManagedViewportVectorFn)(void* viewport);

extern PlaygroundManagedViewportVectorFn Platform_GetWindowPos_ManagedFunctionPointer;
extern PlaygroundManagedViewportVectorFn Platform_GetWindowSize_ManagedFunctionPointer;
extern PlaygroundManagedViewportVectorFn Platform_GetWindowFramebufferScale_ManagedFunctionPointer;
void* Get_GetWindowPos_InteropPointer(void);
void* Get_GetWindowSize_InteropPointer(void);
void* Get_GetWindowFramebufferScale_InteropPointer(void);
uint8_t playground_imgui_probe_invoke_window_pos(void* viewport, PlaygroundImVec2* result);
uint8_t playground_imgui_probe_invoke_window_size(void* viewport, PlaygroundImVec2* result);
uint8_t playground_imgui_probe_invoke_window_framebuffer_scale(void* viewport, PlaygroundImVec2* result);
```

For each bridge, store a Cdecl function pointer matching
`PlaygroundImVec2 Callback(void* viewport)` in its writable slot. The corresponding getter returns
an adapter with native `ImVec2(ImGuiViewport*)` ABI. The probe invoker returns `0` if the slot or
result pointer is null, otherwise calls the same adapter, writes the two floats, and returns `1`.

The three data symbols are correctly distinct writable function-pointer slots. Their getter
functions return native C++ `ImVec2(ImGuiViewport*)` adapter functions. Each function reads the
managed callback address, calls it using the target C calling ABI, and constructs/returns the
upstream `ImVec2`; the helper exports call those exact trampoline functions and copy the result
to an output POD. The staged managed verifier in `../ImGuiPlayground.Checks/PrototypeAbiChecks.cs` writes each
slot from a Cdecl unmanaged callback, calls both the matching native invoker and getter
trampoline, and verifies the sentinel viewport pointer and returned x/y while the static managed
callbacks remain rooted. The native builder's `ctypes` check still validates only native export
presence and null rejection; managed aggregate callbacks are exercised only by the separate
staged .NET verifier.

`ImDrawList_AddCallback` forwards callback/userdata/size to the upstream draw-list method. The
existing staged Checks process exercises renderer-state reset and its managed callback against
the copied prototype library on the validated macOS host.

## Native layout probe protocol

`playground_imgui_probe_manifest_utf8()` returns a stable-lifetime, NUL-terminated UTF-8 JSON
string. `include/playground_imgui_probe.h` documents its top-level schema:

```json
{
  "schema": "playground_imgui.native-abi",
  "schemaVersion": 1,
  "upstream": { "version": "1.92.2", "sourceCommit": "031a18c..." },
  "targetRid": "osx-arm64",
  "configuration": {
    "IMGUI_USE_WCHAR32": true,
    "IMGUI_DISABLE_OBSOLETE_FUNCTIONS": true,
    "ImDrawIdxBytes": 2
  },
  "scalars": {
    "boolBytes": 1,
    "ImWcharBytes": 4,
    "ImDrawIdxBytes": 2,
    "ImTextureIDBytes": 8,
    "sizeTBytes": 8
  },
  "types": [
    {
      "name": "ImVec2",
      "sizeBytes": 8,
      "alignmentBytes": 4,
      "fields": [
        { "name": "x", "offsetBytes": 0, "sizeBytes": 4, "alignmentBytes": 4 }
      ]
    }
  ],
  "bitFieldsNotIndividuallyAddressable": []
}
```

The shown measurements illustrate field names/shape only; **actual values are generated by native
`sizeof`, `alignof`, `offsetof`, and member `sizeof` expressions at compile time**. Do not use
this illustrative snippet as expected sizes/offsets. The complete native list includes
`ImGuiIO`, `ImGuiStyle`, `ImGuiPlatformIO`, ImVec2/ImVec4, the `ImWchar` vector header,
`ImDrawList` and the five concrete draw-vertex/index/command/list/texture vector headers used
by the host/checks, `ImDrawVert`, `ImDrawCmd`, `ImDrawData`, `ImTextureRef`, `ImTextureData`, `ImFontGlyph`,
`ImFontBaked`, `ImFont`, `ImFontConfig`, `ImFontAtlas`, `ImRect`, `ImGuiContext`,
`ImGuiDockNode`, `ImGuiBoxSelectState`, and `ImGuiStackLevelInfo`, plus the probe's `PlaygroundImVec2`,
`PlaygroundImVec4`, and `PlaygroundFloatRect` mirror structs. Where legal, every emitted field
contains its native offset, byte width and alignment. The `bitFieldsNotIndividuallyAddressable`
array calls out upstream bitfields: standard C++ does not permit applying `sizeof` or `offsetof`
to individual bitfields, so the type's total native size/alignment and neighboring ordinary
field offsets are reported without inventing individual bit locations/widths.

The managed verifier separately maintains an independent required-field inventory covering
current host/checks accesses. Omitting those records is a failure, not silently reduced coverage.
Schema-valid size/offset mismatch fixtures specifically exercise the reflection-comparison gate.
The current measured inventory has 30 types and 195 fields; 28 sizes and 190 fields agree.
The two internal size mismatches and five internal field mismatches are reported, not suppressed.

The native builder's macOS load check loads the dylib via `ctypes`, confirms the
version/commit/RID, resolves all 72 names listed by `exports.json`, parses the probe JSON,
validates object/field bounds, and executes selected native calls (one-byte bool result,
rectangle value input, float4 returns, upstream color conversion, and `MemAlloc`/`MemFree`).
That builder-side step validates native loading/calls only—not .NET/BRUTAL calling conventions
or full layout agreement. Probe methods `playground_imgui_probe_upstream_commit()` and
`playground_imgui_probe_target_rid()` expose scalar identity checks.

## Evidence boundary and next gates

- Native source provenance/config, target compiler commands, output hashes, file formats and
  the `otool`/`objdump` dependency reports are saved in `.tmp/native/<rid>/build.json` and
  `dependencies.txt` for the current scratch build.
- macOS arm64 has a native load, export resolution, JSON validation, rectangle/bool/POD-return,
  color-return and allocator/deallocator check on this Apple Silicon host.
- Linux x64 and Windows x64 have source-built ELF/PE cross artifacts and static dependency
  inspections only. Do **not** describe them as executed/loaded.
- The explicit managed integration is documented in `../README.md` and
  `../../ImGuiPlayground.Checks/README.md`; it stages only disposable output copies, keeps the
  ordinary checks source unchanged, and never edits the production ABI guard.
- No production `runtimes/` assets or native hash pins were changed. Ordinary host/checks builds
  continue to use the original pinned runtime pair.

The managed report records exact artifact/binding identities, all 1,146 reflected import names
and availability, the 72-symbol curated prototype list, native-vs-managed public field/width
comparisons, and explicit internal/bitfield coverage gaps. Current macOS measurements and test
results are in `../README.md`; see the checks README for target-side invocation. Keep failures
explicit: the measured `ImGuiBoxSelectState`, `ImGuiContext`, and `ImGuiStackLevelInfo` field
offsets differ from managed reflection, and native bitfield offsets/widths remain unproven.
These are prototype results, not reasons to mutate vanilla source/config or weaken production
guards.
