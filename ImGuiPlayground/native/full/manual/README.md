# Manual formatting and callback ABI component

This is an **opt-in, isolated 22-export component**, not a full binding, a replacement managed binding, or a production artifact. Accepted API/layout inputs remain read-only. No ordinary .NET build invokes these tools.

## Integration contract

Compile `manual.cpp`, include `manual.h` and accepted `../api/purr_abi.h`, against the **real** pinned ImGui headers/core. Use `native/include/playground_imgui_config.h` and the source lock unchanged. All engine, wrapper, probe, and test translation units must use `-fno-strict-aliasing`. The tested flags also include C++11, `-O2 -fPIC -fvisibility=hidden -fno-exceptions -fno-rtti -fno-threadsafe-statics -nostdlib++`. Do **not** infer assertion state from the absence of an explicit `-DNDEBUG`: the locked Zig driver produces `NDEBUG=1`/assert no-op with these current flags. The gate retains actual macro evidence per target. A separate macOS real-core diagnostic control adds `-UNDEBUG`; production flags are unchanged. Tested core source inclusion: `imgui.cpp`, `imgui_draw.cpp`, `imgui_tables.cpp`, `imgui_widgets.cpp`. No demo/backend is needed by this component. Use `layout/patch_source.prepare_source` to obtain a fresh declaration-patched tree; never change the pristine cache.

Production inclusion is **only `manual.cpp`**: precisely 16 imports plus three writable slots and three getters. `manifest.json` contains the full unchanged managed declaration for every import/dynamic export, exact native declarations, conversion and lifetime records, pinned input identities, declaration-reference hashes, and source hashes. `contract.py` closes the inventory and rejects omissions, duplicates, changed declarations, extra supplements, and changed source. Compiler-typed overload casts provide 16 additional native-signature gates.

`fixture.cpp` is **test-only**, not a production dependency. Its six exports are explicitly and separately inventoried in the manifest and `fixture.h`:

- `purr_manual_fixture_create` / `purr_manual_fixture_destroy`: real context/font lifetime;
- `purr_manual_fixture_formats`: drive caller-supplied endpoint invocations, observe actual logging/render/ID/tree/tooltip/style state;
- `purr_manual_fixture_textv`: genuine native variadic producer and exact-output observer;
- `purr_manual_fixture_viewport`: borrowed real main viewport;
- `purr_manual_fixture_invoke`: typed native `ImVec2(ImGuiViewport*)` invocation and fieldwise POD result conversion.

No helper implements an import, intercepts formatting, or produces a measurement in lieu of native state. Test/library output is quiet on success. The independent C++ fixture and unchanged-BRUTAL managed execution are separately recorded.

`FullManualAbiChecks.Run(nint nativeHandle)` returns a report dictionary. The caller already owns the native image and resolver; the seam does not add another resolver/owner. The handle must expose the component plus its six fixture exports (later combined qualification can link the fixture TU alongside the full binding). Run on the single UI thread with no pre-existing context owned by another test. The supplied disposable harness loads its staged fixture image, installs one resolver for the unchanged BRUTAL assembly, and then invokes Run. `Interop.CreateTrampoline` itself explicitly loads `imgui`; checks verify writes are visible in the **same handle's slot**, and getter addresses match. Native references held by that unchanged Interop implementation/resolver remain live until disposable process exit.

## Formatting and va_list

All 15 non-V imports are fixed exported functions calling real native printf-facing functions with **zero extra varargs**. Valid zero-extra-operand formats retain native printf behavior: `%%` becomes `%`. Labels and tree string IDs are not formats. Operand-consuming formats without supplied operands violate the calling precondition. There is no `%s` literal forwarding, synthetic value, or sanitizer.

`TextV` is declared and defined with the compiler's actual `va_list`. Its native type is statically identical to `ImGui::TextV`. The compiler, not this component, declares the representation:

| Locked target | Actual header/Clang AST | Export parameter lowering |
|---|---|---|
| aarch64-macos.11.0 | `va_list` = `char*` | pointer value, not address-of-pointer |
| x86_64-linux-gnu.2.17 | `va_list` = compiler `struct __va_list_tag[1]` | array parameter adjusts to compiler struct pointer |
| x86_64-windows-gnu | `va_list` = `char*` | pointer value, not address-of-pointer |

No SysV struct definition or register-save buffer is copied. All-target AST files record the actual typedefs and adjusted function declaration; LLVM records two pointer parameters for TextV; `abi_proof.cpp` pins the supported compiler type families and real native platform callback member types. Cross-compilation **does not** prove foreign CLR ABI behavior.

The producer owns the list and all argument storage through the synchronous callback. A consumer may consume the passed list; reuse requires a producer-side `va_copy`, followed by matching `va_end`. The wrapper neither copies nor ends a list it does not own and preserves native consumption semantics. The test calls `va_start`, makes two independent real `va_copy` lists, and passes each through a managed callback into **unchanged `ImGui.PInvoke.TextV(byte*, byte*)`**. It checks exact native rendered/logged output with negative integer, double, string, dynamic width, dynamic precision, 64-bit integer, and `%%`. This is not an empty or fabricated va_list test.

## Callback preconditions

Slots are aligned, writable, pointer-sized data symbols stable while the library lives. Getters always return actual typed native trampoline addresses, including before installation. Each native trampoline has `ImVec2(ImGuiViewport*)` shape, invokes a Cdecl POD `PurrVec2(ImGuiViewport*)` managed callback, then converts fields into real ImVec2. Managed callbacks are Cdecl `float2(ImGuiViewportPtr)`; viewport argument address identity is preserved.

Install a live compatible callback before invoking a trampoline, retain it while installed, and finish all synchronous calls before replacement/unregistration or release of the library/callback. Installation, invocation, replacement, and clearing belong to the owner UI thread. An empty slot is **inactive**. Null invocation is not qualified and has no invented zero-result success fallback. Native and managed tests cover all three bridges with mixed-sign nonzero values, replacement, stable addresses, independent slots, rooted delegates across full GC, and clearing before roots are released. They never call an inactive trampoline.

## Reproduction

From this isolated copy, explicitly run (use a fresh output name for every run):

```sh
python3 ImGuiPlayground/native/full/manual/self_test.py --output negatives-new
python3 ImGuiPlayground/native/full/manual/check.py --output gate-new \
  --rid osx-arm64 --rid linux-x64 --rid win-x64 --runtime \
  --ksa /explicit/path/to/selected/dll
```

`--ksa` is an explicit override; otherwise `KSAFolder` takes precedence over `KSA_DLL_DIR`. There are no game-directory guesses. Zig must be the locked 0.17.0; macOS compilation uses the actual xcrun SDK. Only macOS executes locally. Omitting `--runtime` requests compilation/static gates only. Windows/Linux native and CLR runtime execution is **not performed or inferred**. Their eventual execution must repeat both native fixtures and this unchanged-managed seam on actual target runtimes with explicitly selected contributor identities.

Tools exclusively create new trees below project `.tmp/native/full-manual`. A stdlib-only bootstrap checks the entrypoint and `paths.py` before importing that helper. The helper then checks the closed component/dependency module leaves, optional package initializers, source/contract/config/archive/patch inputs and their ancestors before component/dependency imports or input reads. Component modules load by checked exact filename, not an ambient module-name search. Selected managed inputs and input/output disjointness are checked before output creation/native compilation; output beneath the selected input directory is rejected. All existing ancestor/root/leaf and dangling symlinks, non-directory ancestors, shared/nonregular leaves, parent traversal, and existing run reuse are rejected before mkdir/compiler/write. A new subtree is the whole write-plan boundary, including dotnet build/stage outputs and Zig caches; no protected input can be overwritten. Concurrent hostile swaps are outside the contract. Shared `native/process_utils.py` bounds process groups and retains commands/logs on failure; there are no fixed sleeps. The selected, built, staged, and process-loaded ABI contributors are checked against exact hashes. Managed libraries are staged only in the disposable output tree, never ordinary bin/runtimes. These are existing-redirect/project-input checks, not arbitrary-code sandboxing of a replaced interpreter entrypoint, system SDK/compiler resolution, or concurrent filesystem swaps. Real subprocess regressions redirect imported modules to executable canaries, redirect source inputs to identical bytes/dangling paths, and invoke both actual entrypoints. They also verify selected-input/output-overlap and selected DLL redirects are rejected before a compiler canary executes or a run tree is created.

The checker creates two native libraries per target: production 22 exports, fixture 28 exports. It verifies exact export inventories and independently inspects **both complete inventories** in real Mach-O/ELF/PE sections (executable functions versus aligned writable slots). Results are retained separately as `productionSymbolSections`/`fixtureSymbolSections` and `production-symbol-sections.json`/`fixture-symbol-sections.json`. On each target, nine mutations of the actual fixture image relocate each of the six fixture helper functions or three writable slots into an opposite-kind section. Export names still pass; binary-kind inspection rejects the specific mutated symbol. Mutated images are never loaded. The gate also checks pointer storage width in compiler IR and ELF symbol sizes, compiles C11 and C++ type gates, and rejects all 16 deliberately corrupted native-overload signatures on every target. The only excluded native bookkeeping names are explicit Mach-O linker-generated `__dso_handle` and `_mh_dylib_header`.

Native observations cover percent formatting at every endpoint, literal label/string-ID percent text, both tree overload families/open and closed return/stack balance, color vertices and neighbor guards, aligned position, style/wrap restoration, enabled/disabled buffer logging, debug logging without TTY output, tooltip hovered/not-hovered behavior and same-frame replacement. Hover input is deliberately established in real native fixture state; this is not a platform-backend hover-timing qualification. The negative native fixture changes the submitted format to prove exact percent-output checks reject a wrong result. Eight additional cases invoke real tree endpoints but flip the returned open/closed result, require the original mismatch diagnostic, reuse the fixture successfully and destroy/recreate its context. Scope-bound cleanup restores observed native tree/ID depth independently of the callback's reported result before frame cleanup. The same native cases pass in the separately assertion-enabled real-core control. Invalid operand-consuming formats are not executed as negative tests because that would invoke undefined behavior.

## Bounded review correction and counterevidence

The previous documentation's assertion-enabled claim was incorrect. Parent `manual-tree-result.json` (SHA256 `9a2ef5c097642c02f574658f862b1544c7e3afaf17c607efe1b8fab1c1fd9a84`) records the **pre-fix** real-tree/wrong-result reproducer returning the mismatch diagnostic, successfully reusing/destroying the fixture, and exiting 0. Parent `manual-compiler-macros.log` (SHA256 `d12eb2859d47c771e5546a4324b18a6afd84933c13fb0f736ed2b9665b29b6ef`) establishes `NDEBUG=1`. No current production ABI defect or current-build tree abort was established. The tree change removes implicit reliance on upstream recovery; the `-UNDEBUG` control qualifies an additional diagnostic configuration without changing production flags or native algorithms. The fixture binary-kind omission was a confirmed gate gap and is now covered separately on all targets.

The macOS `--runtime` gate additionally runs the **ordinary unchanged Host-to-Checks ProjectReference build**, with explicit `-p:KSAFolder=... -p:TreatWarningsAsErrors=true` and SDK per-project `--artifacts-path` under the new run tree. It is not replaced by the additional direct-source runtime harness, and does not replace native assets in ordinary bin or production runtimes. All tools remain explicit maintainer operations; no project build hook was added.

## Evidence limitations

This component does not qualify all 1146 imports, layouts, enum domains, unsafe managed aliases, a graphics backend, production installation, or Windows/Linux runtime ABI. It does not integrate sibling components. Source hashes, exact commands, source patch provenance, target AST/LLVM, symbol sections, static negatives, staged identities, managed result, and diagnostic logs are retained under each run. Earlier development failures remain retained and are not passing evidence. Parent integration and independent review are still required.
