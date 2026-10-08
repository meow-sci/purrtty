# Platform validation: owned native ImGui implementation

This checklist is for the **source-owned native API effort**, not promotion of its artifacts
into production. Keep `ImGuiPlayground/` and `ImGuiPlayground.Checks/` as sibling folders.
No enclosing repository, game launch or game-installation write is needed.

**Current status:** the full combined implementation and portable qualification runner are
implemented. Actual macOS ARM64 guarded fixture/component and **production-image** renderer,
input and ordinary Host capture gates have passed. These are bounded automated checks,
not exhaustive API or desktop qualification. Retain independent review and runtime results
before distributing bundles. The older 72-export prototype remains a separate experiment,
not full proof. No source-built artifact has been promoted to runtimes.

| Full-API gate | macOS ARM64 | Windows x64 | Linux x64 |
|---|---|---|---|
| Current-input managed contract export/self-tests | Passed | Pending native-host execution | Pending native-host execution |
| Complete native import/signature/forwarding reconciliation | Passed | Cross-built/inspected; execution pending | Cross-built/inspected; execution pending |
| Complete classified storage/layout and safe-helper gates | Passed, exact known conflicts retained | Static target evidence; execution pending | Static target evidence; execution pending |
| Representative BRUTAL behavior, including `TextV` and callbacks | Passed (bounded cases) | Pending native-host execution | Pending native-host execution |
| Source-built full artifact with rendering/input/capture checks | Passed using production image | Pending native-host execution | Pending native-host execution |
| Manual desktop integration | Not fully qualified | Pending | Pending |

Windows/Linux execution is intentionally **user-owned and pending**. Cross-compilation,
export inspection, cross-publishing, or running under Wine on macOS does not close it.
The [full qualification runner](native/full/qualification/README.md) implements `build`,
`extract`, `run`, `test` and `ordinary`. Its bundle carries all-target probes but each runtime
must execute its own target probes and remeasure its actual CLR. An independently retained
manifest SHA is required; a bundled self-reported success flag is not ABI evidence.

## 1. Prepare the native target host

- Use the matching 64-bit OS/architecture above and the .NET 10 SDK. Other architectures,
  Linux musl and big-endian processes are outside the selected target set.
- Supply the actual managed BRUTAL assemblies locally. Set `KSA_DLL_DIR` explicitly;
  unset `KSAFolder` or set it to the same directory because **`KSAFolder` takes precedence**
  for builds. The exporter separately takes an explicit input-directory argument.
- Python 3.9+ is needed for maintainer tooling. Zig 0.17.0, curl and the relevant SDK are
  required only for explicit native compilation, not ordinary .NET builds or metadata export.
- Graphics checks need a working desktop/OpenGL 3.2 environment and GLFW's OS dependencies.
  Invisible capture still needs a graphics context. A headless ABI pass is not GPU/desktop proof.
- Do not replace files under `runtimes/`, change `NativeLibraries.props`, modify the external
  DLLs, or substitute a candidate native library in ordinary `bin/` outputs. Native tests must
  use disposable staged copies with one native implementation/context owner per process.

Record OS/build, CPU architecture, .NET SDK/runtime, display backend, DPI/scaling and GPU/driver.
If rebuilding native artifacts, retain compiler version, flags, target triple and build logs.

## 2. Run the currently implemented managed preflight

Commands below run from **`ImGuiPlayground/`**. They inspect metadata only and need no display
or native compiler. Tests and exports are quiet on success; retain diagnostics on failure.

Linux/macOS:

```bash
export KSA_DLL_DIR="/absolute/path/to/current/dll"
# Ensure KSAFolder is unset or points to the same selected directory.
dotnet build ../ImGuiPlayground.Checks/ImGuiPlayground.Checks.csproj --nologo -v quiet
dotnet run --project ../ImGuiPlayground.Checks --no-build -- --binding-contract-self-test "$KSA_DLL_DIR"
dotnet run --project ../ImGuiPlayground.Checks --no-build -- --export-binding-contract "$KSA_DLL_DIR" .tmp/native/binding-contract.json
```

Windows PowerShell:

```powershell
$env:KSA_DLL_DIR = 'C:\path\to\current\dll'
# Ensure $env:KSAFolder is unset or points to the same selected directory.
dotnet build ../ImGuiPlayground.Checks/ImGuiPlayground.Checks.csproj --nologo -v quiet
dotnet run --project ../ImGuiPlayground.Checks --no-build -- --binding-contract-self-test $env:KSA_DLL_DIR
dotnet run --project ../ImGuiPlayground.Checks --no-build -- --export-binding-contract $env:KSA_DLL_DIR .tmp/native/binding-contract.json
```

Use a direct project build as above, or a relative solution path from its directory. With
.NET SDK 10.0.100 on macOS, an absolute solution path through the `/var` directory alias was
observed to omit dependencies from the generated checks `.deps.json`; the physical path and
relative-path builds passed. See the [checks README](../ImGuiPlayground.Checks/README.md).

The audited ABI metadata contributors are:

| Assembly | SHA-256 |
|---|---|
| `Brutal.ImGui.dll` | `b76777a4ef3399d6353b9dba1982e3afb84b5da2aa2500da1c062db0bfad827c` |
| `Brutal.Core.Common.dll` | `4cbae1473d6a3759345f1eec8e62e6c7da1e19f8d10db8d13c005203ec590de4` |
| `Brutal.Core.Numerics.dll` | `a217a6098116a1895e965b3a1d4fdee931e59a7a55f326c7d710b0d7eeccd17a` |

The old prototype-tested ImGui DLL hash started `9040dc5d…`; equal ImGui `1.92.2` version
strings and equal import counts do **not** make it interchangeable with the current input.
An identity change requires re-audit, not editing an expected hash until a test passes.

The exporter records 1,146 imports plus six separately declared dynamic bridge symbols.
It deliberately retains overlapping fields, opaque shapes and unresolved native mappings.
On the measured macOS runtime, `ImGuiStyleVarInfo` and `ImFontAtlasRectEntry` have actual CLR
strides of **6 and 7**, despite declared size 4. Re-measure on the destination runtime.
The complete JSON includes framework identities and host-specific observations: **do not
expect its whole-file hash to match across different operating systems or .NET runtimes**.
Metadata dependency closure is not runtime dependency readiness; the latter needs its own gate.

## 3. Run full-native qualification on the destination target

Obtain `qualification.zip`, the standalone `extract_bundle.py`, and independently retained
manifest/extractor SHA-256 values from the trusted maintainer. Verify the extractor before
execution. Integrity is relative to that maintainer/compiler, not authenticity against a
malicious builder. The archive includes no private KSA DLLs and runtime execution needs no
native compiler, producer cache or producer absolute path. Use fresh physical destinations:

```bash
ANCHOR='<independently retained manifest SHA-256>'
python3 extract_bundle.py --archive qualification.zip --anchor "$ANCHOR" --output '/physical/path/qualified bundle'
BUNDLE='/physical/path/qualified bundle'
RUNNER="$BUNDLE/source/ImGuiPlayground/native/full/qualification/run.py"
python3 "$RUNNER" run --bundle "$BUNDLE" --anchor "$ANCHOR" --selection "$KSA_DLL_DIR" --output '/physical/path/full-run'
python3 "$RUNNER" test --bundle "$BUNDLE" --anchor "$ANCHOR" --selection "$KSA_DLL_DIR" --run-output '/physical/path/full-run' --output '/physical/path/full-negatives'
python3 "$RUNNER" ordinary --bundle "$BUNDLE" --anchor "$ANCHOR" --selection "$KSA_DLL_DIR" --output '/physical/path/ordinary-checks'
```

PowerShell equivalents (Python 3.9+ on PATH):

```powershell
$anchor = '<independently retained manifest SHA-256>'
python extract_bundle.py --archive qualification.zip --anchor $anchor --output 'C:\validation\qualified bundle'
$bundle = 'C:\validation\qualified bundle'
$runner = "$bundle\source\ImGuiPlayground\native\full\qualification\run.py"
python $runner run --bundle $bundle --anchor $anchor --selection $env:KSA_DLL_DIR --output 'C:\validation\full-run'
python $runner test --bundle $bundle --anchor $anchor --selection $env:KSA_DLL_DIR --run-output 'C:\validation\full-run' --output 'C:\validation\full-negatives'
python $runner ordinary --bundle $bundle --anchor $anchor --selection $env:KSA_DLL_DIR --output 'C:\validation\ordinary-checks'
```

Precedence is `KSAFolder` > `--selection` > `KSA_DLL_DIR`; unset conflicting variables.
Full Windows redirect negatives require symlink creation privileges. No display/privilege
skip is reported as passed. Keep `results.json`, per-process `report.json`, source/stage
receipts, commands/logs, actual CLR/probe/guard JSON and `process-capture/hello-world.png`.
The runner builds the actual Checks->Host projects, uses disposable staged copies, and
runs six guarded fresh processes: accessor/profile/manual/composition against the fixture,
then renderer and actual ordinary Host capture against **production**.

The following remain the interpretation and manual follow-up requirements, not a claim
that every endpoint or desktop domain is exhaustively exercised:

1. **Identity and provenance:** approved versus selected external DLLs, staged Host and Checks
   copies, and each process's loaded assemblies agree. Native binary/build record, upstream
   commit, config, ordered patches, wrapper sources and helper contract agree. Reject stale or
   mixed inputs before unsafe access. Ordinary production assets remain unchanged.
2. **Import/signature coverage:** every current import maps exactly once to compiled code that
   calls the reviewed upstream overload; six bridges have the right function/data kind.
   Verify calling conventions, parameter shapes, pointer depth, Bool8/scalar/enum widths,
   aggregates and callback signatures. Export names alone are insufficient.
3. **Storage and helper coverage:** every required type/field/array/container has an explicit,
   independently measured disposition. Ordinary layout mismatches fail. Retain known unsafe
   raw aliases, opaque copying/indexing restrictions and the text-buffer static/instance
   conflict. Test native-backed helpers, signed bounds, measured bit masks, preservation of
   neighboring bits/records and native indexing. **Original raw aliases remain unsafe** even
   when helper-mediated tests pass; never report them as repaired.
4. **Negative gates:** remove or mutate a required signature, field, array extent, bitfield,
   accessor or identity. Require rejection by the intended gate, before context creation or
   unsafe dereference where applicable; parser failure must not masquerade as layout rejection.
5. **Actual BRUTAL behavior:** context/allocator lifetimes; interactive widgets and scopes;
   input and UTF-8 editing/callbacks; fixed-format `%%` semantics and real target-correct
   `TextV` argument packing; fonts; drawing/textures; tables/clipping; docking/settings;
   platform callback bridges; and selected internal storage/hooks. Assert observable results
   and balanced teardown. Do not blindly invoke every internal method outside its lifecycle.
6. **Graphics and capture:** run the existing pixel/orientation/scissor, >64K-vertex,
   texture/font, draw-callback/reset, keyboard/Unicode and recovery checks against the staged
   full native artifact, then capture Hello world. Keep reports, command results, logs and PNG.
7. **Manual desktop session:** verify typing, clipboard, resizing, minimize/restore, focus
   loss, DPI behavior and repeated close/reopen. State exactly what the host implements:
   its missing OS multi-viewport backend, gamepads, cursor warping/shapes and full IME
   composition are not supplied merely by completing native ImGui exports. An in-memory
   platform callback fixture is not proof of real secondary-window/IME integration.

No fixed sleeps or unbounded subprocesses. Preserve the last-started case on native crashes.
A missing report, crash, timeout or unexecuted target is **pending/failed**, never a success skip.

## 4. Return evidence and retain the publication boundary

For each RID, keep artifact and input hashes, build/probe/behavior reports, command exit status,
logs, capture and manual observations together. Separate these conclusions:

- cross-built / statically inspected;
- loaded and exhaustive identity/signature/classified-layout gates passed;
- native bitfield and owned safe-helper semantics passed;
- representative BRUTAL behavior passed;
- renderer/input/capture passed;
- manual desktop integration passed, with named unsupported features.

Record unsafe original aliases and all remaining limitations even in a successful bundle.
Passing representative behavior is not exhaustive testing of every widget/internal path.
Do not redistribute private managed assemblies or decompilation with public reports; their
redistribution rights remain unverified. Promotion of source-built native artifacts into
production runtime assets requires a **separate explicit decision**.
