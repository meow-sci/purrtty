# ImGui Playground Checks

Companion executable checks for [ImGuiPlayground](../ImGuiPlayground/README.md), not a mod
test project. The two sibling folders form one independent workspace; the only project
reference is to the playground. Local `Directory.Build.props` imports the playground's
settings and blocks enclosing-repository build/package configuration. The standalone
solution is `../ImGuiPlayground/ImGuiPlayground.slnx`.

## Setup and execution

Run these commands from this `ImGuiPlayground.Checks/` folder with the .NET 10 SDK:

```bash
export KSA_DLL_DIR="/absolute/path/to/KSA"
dotnet run -v quiet
# Gallery-only: graphical session; retain screenshots for review.
dotnet run -- --widgets ../ImGuiPlayground/.tmp/widgets
# Packaging-only: no graphical session needed.
dotnet run -- --packaging
```

On PowerShell, set `$env:KSA_DLL_DIR = 'C:\Program Files\Kitten Space Agency'` instead.
Use an environment variable for packaging checks: child `dotnet` processes do not inherit
MSBuild `-p:` arguments from the initial build. Required managed assemblies remain external;
there is no automatic repository/game-directory lookup. See the host README for native
runtime prerequisites and compatibility limitations. For target-side evidence and remaining
qualification gates, see [VALIDATE_PLATFORMS.md](../ImGuiPlayground/VALIDATE_PLATFORMS.md).

**.NET SDK 10.0.100 solution-path caveat:** invoke the solution from its folder using a relative
path, as documented, or use a physically resolved absolute path. On macOS, an absolute `.slnx`
path through `/var` (a symlink to `/private/var`) was observed to build successfully but omit
project/package dependencies from `Checks.deps.json`, causing launch failure. A canonical path
(including spaces), a relative solution path from that directory, and a direct checks-project
build all passed. Rebuild through one of those paths if affected; do not hand-edit `.deps.json`.

The default checks need a graphical session/OpenGL driver and run on `Main`, not NUnit
worker threads (macOS GLFW requires the main thread). They cover deterministic pixel
regions/orientation/scissors, large meshes, PNG round-trip, font selection/texture updates,
callback state reset, keyboard/character/cursor fidelity and callback-failure/repeated-run recovery.
`InteropChecks` also checks logical display/framebuffer/mouse mapping at Windows 100–300%
(including 250% and returning to 100%), independent axes, invalid-scale fallback, and unchanged
Retina behavior. The installed native cursor callback is exercised at the current host's scale;
these arithmetic cases do not emulate Windows DPI-change messages or certify monitor moves.
They are quiet on success, fail with a nonzero exit code, and never use fixed sleeps.

## Focused widget gallery checks

`--widgets [capture-directory]` uses the ordinary shipped-native host. It renders camera,
parts, tuning and the built-in demo at the default 800×500 logical size, plus the expanded
selected-part tree, reload modal and color picker popup. The host requires nonempty geometry
and checks GL errors; the fixture also requires bright text/colored pixels and different scene
images. Actual ImGui IO mouse press/release events assert that **Play** starts playback and
**Loop** toggles fake state. After the genuine Play click assertion, a separate capture of the
same fake model saves the settled PLAYING label (the label is submitted before the button).
Direct and gallery-opened demo captures check public window bounds and left/central content;
the latter uses an internal request seam, not an assertion of checkbox/menu input.
Success is quiet, with no sleeps or UI automation dependency.

Nine screenshots are retained: `camera.png`, `parts.png`, `tuning.png`, `demo.png`,
`parts-modal.png`, `tuning-picker.png`, `camera-play.png`, `camera-loop.png`, `demo-gallery.png`.
Without an explicit directory they are saved in `widget-captures/` beside the checks executable.
This is bounded representative coverage, not exhaustive widget/native qualification or a
pixel baseline across platforms. Menus/context actions, text editing, modal confirmation,
scrolling, numeric edits and color changes are also useful manual review cases.

Exact small handoff from the enclosing repository root:

```bash
export KSA_DLL_DIR="/Users/asherwin/repos/meow-sci/ksa-game-assemblies/current/dll"
dotnet build ImGuiPlayground/ImGuiPlayground.slnx --nologo -v quiet
dotnet run --project ImGuiPlayground.Checks --no-build -- --widgets ImGuiPlayground/.tmp/widgets
# Optional existing renderer/input checks (no native rebuild):
dotnet run --project ImGuiPlayground.Checks --no-build
```

Scene-specific CLI captures are documented in the [host README](../ImGuiPlayground/README.md#gallery-pages-and-widget-coverage).
The gallery uses original local-only mockups inspired by the three `unscience` UI reference
paths listed there, with no reference-repository build or runtime dependency.

`--packaging` locates the sibling source project relative to the executable, so it does
not depend on the caller's working directory. When running a copied/published checks
executable away from its source tree, pass the project explicitly:

```bash
dotnet run -- --packaging /absolute/path/to/ImGuiPlayground/ImGuiPlayground.csproj
```

Packaging checks cross-publish all three supported RIDs, verify native filenames, hashes,
architectures, managed dependencies, font and notices, replace deliberately stale/newer
output files, and require unsupported-RID/hash-mismatch failures. They do not execute
foreign binaries. Windows/Linux runtime behavior still needs validation on those platforms.

## Maintainer-only managed binding contract (no native loading)

The explicit exporter reads the **selected directory's actual assemblies**, not potentially stale
`bin/` copies. It does not create an ImGui context, invoke external getters/constructors, or load
any native library. Normal builds/default graphical checks are unchanged; this tool does not
run automatically. From this folder:

```bash
export KSA_DLL_DIR="/absolute/path/to/current/dll"
dotnet build ImGuiPlayground.Checks.csproj --nologo -v quiet
# Explicit directory argument is authoritative for export (independent of MSBuild KSAFolder).
dotnet run --no-build -- --export-binding-contract "$KSA_DLL_DIR" ../ImGuiPlayground/.tmp/native/binding-contract.json
# Quiet metadata fixtures; optional directory adds actual-input determinism/closure/failure tests.
dotnet run --no-build -- --binding-contract-self-test "$KSA_DLL_DIR"
```

On PowerShell use `$env:KSA_DLL_DIR` as the argument. Supported measurement hosts are
**osx-arm64, win-x64, linux-x64**, little-endian with eight-byte pointers. Unsupported hosts
fail before measurement. The versioned JSON is generated into scratch, not an automatically
updated/pinned source file. It contains no timestamps or source-machine paths. It is byte-for-byte
deterministic for identical selected inputs **and the same host runtime**; framework identities
and runtime layout observations intentionally differ across runtimes/targets. Write to a new
`.json` outside the input directory; failures leave any previous output unchanged. Input and
output directory symlinks/junctions are resolved before containment checking or creation;
unresolvable redirects and redirected destination files are rejected. Trailing separators do
not weaken the boundary. Publication uses the checked physical destination and a new temporary
file. This is a maintainer safeguard against existing redirects, not a sandbox against hostile
concurrent filesystem changes. Quiet fixtures cover both alias directions, ancestor links,
dangling redirects, destination links and unchanged protected contents; link fixtures may skip
on Windows without symlink privileges.

### Schema v1 consumer contract

Require `schema = playground_imgui.managed-binding-contract`, `schemaVersion = 1`, and
`exporterVersion = 1`. Reject unknown versions, missing records, duplicate symbols/types,
unsupported marshalling, unresolved mappings and identity mismatches; do not accept by counts
alone. This is a **managed metadata contract**, not native ABI acceptance or a forwarding map.

- `assemblies`: exact assembly/full/version/file/informational identities, SHA-256, module MVID,
  filename, resolution source, requested identities and declared references. An isolated load
  context permits only selected-directory dependencies and the current runtime's trusted
  framework set. No stale `bin/`, game-installation discovery or package probing fallback is used.
  The closure is **all required reflected metadata contributors**, not every implementation-only
  transitive dependency. Missing/ambiguous required metadata is fatal; declared references that
  do not contribute metadata remain recorded but are not eagerly loaded. This does **not** prove
  runtime dependency readiness: for example, the supplied input's implementation graph references
  `Microsoft.Extensions.ObjectPool` v11, which is absent there. Runtime execution has a separate
  dependency gate. Framework identity resolution may unify requested runtime versions; selected
  non-framework contributors must match requested full identities exactly. Files are rehashed
  before publishing a complete output.
- `imports`: all declared-only import methods across all assembly types, sorted by library/name.
  Each records declaring type, method name/token/MVID, method and implementation flags, managed
  convention, exact library/entry point, effective `DllImport` flags and raw `LibraryImport`
  metadata. Both attributes on one method produce **one** import; conflicting library/entry
  point metadata fails. Current default `Winapi` is retained, not relabeled `Cdecl`.
  Return/ordered parameter records retain attributes (including marshalling), In/Out/Optional,
  defaults, modifiers and structural type IDs. Custom attributes are read as metadata without
  running external attribute constructors.
- `types`: roots are **all** primary-assembly value types (public, private and nested) and
  delegates, plus import signatures. The recursive field/signature closure includes external
  numeric/Bool8/pointer types, pointer/byref/array shapes, scalar signedness, enums (exact decimal
  string constants), generic definitions/parameters and concrete generic arguments. Type IDs are
  ordinal, assembly-simple-name-qualified structural keys; records additionally retain full
  assembly-qualified names, assembly identity and token/MVID where applicable. Dependency
  reference types are named shapes, not an export of the BCL heap's private implementation.
- `signatureShape` on each parameter/return/field retains **modified signature types**, including
  nested function-pointer conventions and custom modifiers. It is authoritative for function
  pointer conventions: CLR `Type` alone can erase Cdecl/Stdcall information. `type` links into
  the ordinary structural graph. Delegate records include Invoke and raw/effective
  UnmanagedFunctionPointer metadata. Opaque `nint` callbacks still require a reviewed native
  signature/ownership mapping; they are not automatically inferred from parameter names.
- Value records separate declared `StructLayout` size/pack/kind from actual
  `Unsafe.SizeOf<T>()`, two-element CLR array stride, reference-containing status and the offset
  of `T` after a byte prefix in a sequential helper. The last value is a **managed embedding
  observation, not C++ alignof**. Instance-field offsets use `ldflda` on a zero-initialized
  local, not marshaler offsets; widths are raw managed storage, not marshaled widths.
  Separate `marshalerMeasurement` / `marshalerOffset` records retain Marshal.SizeOf/OffsetOf
  observations or explicit rejection reasons; never use them in place of raw storage (notably
  for bool/generic types). All fields, including nonpublic storage, static fields, explicit offsets, readonly flags,
  fixed-buffer/inline-array types and counts, remain present in metadata-token order.
  `overlaps` reports every intersecting measured interval without guessing whether it is a
  legitimate union or an unsafe native-bitfield alias. Static fields have no instance offset.
- Measurements use `status: measured` or `status: unavailable` with a stable reason. Open
  generics and byref-like helper limitations are explicit; unexpected concrete measurement/type
  load failures abort export. **Never substitute declared sizes/offsets for unavailable runtime
  values.** Ref-return property signatures remain present, but `rawStorageLink` is explicitly
  unresolved: mapping them to fields would require reviewed declarations, not guessed names or
  copied private method implementations. Native bit positions/widths remain unresolved.
- `dynamicExports`: six reviewed supplementary bridge declarations, **separate from imports**:
  three pointer-sized writable callback slots and three zero-argument pointer-returning
  Cdecl getters. Callback signatures are `float2(ImGuiViewportPtr)`; native trampolines are
  `ImVec2(ImGuiViewport*)`. Null-callback behavior is deliberately unresolved. Consumers must
  implement/review actual aggregate ABI adaptation, slot lifetime and callback behavior.
- Bounds: 8,192 structural types, 65,536 described members, 4,096 fields per value type,
  256 loaded metadata assemblies and 64 MiB serialized output; exceeding a bound fails.
  All numeric/enum metadata constants are decimal strings (floating values use round-trip
  invariant strings); booleans are JSON booleans and chars are codepoints. Attribute argument
  types disambiguate constants. Sizes, offsets, tokens and counts are JSON integers.

### Current measured input and handoff boundary

The 2026.10.10.5554 input's `Brutal.ImGui.dll` SHA-256 is
`b76777a4ef3399d6353b9dba1982e3afb84b5da2aa2500da1c062db0bfad827c`, **not** the prototype's
historical `9040dc5d410043c53dbea10d94995b6712240be37f9251b016f6cff66a76339f`.
Its assembly/file version is still `2026.9.0.0` / `2026.9.0`; use the hash/MVID, not just versions.
On .NET 10.0.0 / macOS ARM64 the contract contains **1,146 imports, 1,095 type shapes,
2,247 fields (2,178 instance), 1,806 ref-return properties, 149 closed generic shapes,
13 delegates, 16 fixed buffers, 22 inline arrays**, plus the six bridges. Counts include
reachable dependency/closed-generic records, not only ImGui source declarations. The current
raw layout has 104 overlapping field pairs. The 31 unavailable instance offsets belong to
open generic or byref-like shapes, not silently omitted concrete native-layout members.

Actual `ImGuiStyleVarInfo` size/stride is **6/6** and `ImFontAtlasRectEntry` is **7/7**, although
both declare size 4. `ImGuiContext` measures 11320, `ImGuiStackLevelInfo` 72. These are managed
facts, not repaired native layouts. The historical report contains the same 1,146 symbol
names (zero added/removed), but lacks a complete structural contract: no signature-equivalence
or no-layout-drift conclusion is justified from that comparison.

Next owners consume this JSON with pinned upstream declarations/config and an independently
reviewed native mapping. They must supply real wrappers, native layout/bitfield probes,
classified unsafe raw aliases and native-backed safe helpers, target-specific calling tests
and representative behavior. Do not generate successful stubs, expected native offsets copied
from this file, or full-compatibility claims from exports. Cross-build/static inspection is
not Windows/Linux execution; all native qualification and `VALIDATE_PLATFORMS.md` stages remain
outside this exporter seam.

## Maintainer-only native ABI prototype

The opt-in prototype verifier compares the exact staged native artifact with the real external
`Brutal.ImGui.dll` selected by `KSAFolder` or `KSA_DLL_DIR` (`KSAFolder` takes precedence). It is separate from ordinary builds, the default
checks, packaging tests, production runtime pins and `runtimes/`. First source-build the native
scratch artifact for the **current host** from `ImGuiPlayground/` (Zig 0.17.0 is required only
for this explicit step):

```bash
python3 native/build.py --target osx-arm64 # macOS arm64
# Or, on the matching target host only:
python3 native/build.py --target linux-x64  # Linux x64
python3 native/build.py --target win-x64    # Windows x64
```

Then from this `ImGuiPlayground.Checks/` folder, run the bounded portable stage runner:

```bash
python3 prototype_runner.py --target-rid osx-arm64 --report /tmp/managed-abi-report.json
# On Linux x64 replace osx-arm64 with linux-x64; on Windows x64 use win-x64.
```

The runner builds the ordinary host and checks apps, copies their outputs plus the frozen
native metadata into disposable system-temporary directories, replaces **only the staged
copies' ImGui library**, and launches three separate bounded processes: (1) headless managed
ABI gates/calling tests, (2) the unchanged render/input checks, and (3) hello-world capture.
Ordinary builds still populate `bin/` with production assets; the prototype is never copied
there or into `runtimes/`, and the runner does not modify native source artifacts. A successful run
is quiet and removes scratch outputs; `--report` optionally retains the machine-readable ABI
report. A failure is nonzero, keeps runner JSON, child logs, staged files and any partial ABI
report, and prints their directory. It has no fixed sleeps. Run it only on the native artifact's
target OS/architecture; foreign cross-built libraries are not loaded by this runner.

The independent required-field inventory covers current host/checks raw accesses, including
`ImDrawList`, clipboard/config fields, renderer limits and concrete vector headers. Thirteen
negative fixtures cover missing fields, empty field inventories, schema-valid size/offset
mismatches and the identity/config/export gates. Position helper results are asserted before
any other setter; a nonzero `ImTextureRef` value is checked in emitted draw commands.

Compiler/game-free tooling regressions are also available (quiet on pass):

```bash
python3 prototype_tooling_checks.py
```

They check redirected scratch and metadata symlinks plus timeout/Ctrl-C process-tree cleanup
for both the builder and runner. Child/grandchild readiness uses a bounded socket handshake,
not sleeps; symlink checks skip on hosts lacking symlink privileges. The builder and runner
share cancellation helpers under `../ImGuiPlayground/native/process_utils.py`.

The report records the reflected 1,146-import inventory against actual available exports,
exact managed/native SHA-256 identities and target RID, native-compiled layout values versus
managed reflection offsets/widths, the safe host/public subset gate, and explicit internal/raw
bitfield and full-import coverage gaps. Passing means only the bounded subset exercised on that
host. It is **not** full BRUTAL compatibility: 1,092 managed imports remain unavailable (including
`TextV`), and internal layout mismatches and bitfield locations remain unproven. Native configuration flags are
prototype hypotheses, not authenticated KSA build metadata. See the host and native prototype
READMEs for exact current results and target-side commands.

## Extraction check

To verify the workspace remains independent when changing build configuration:

1. Copy **only** `ImGuiPlayground/` and `ImGuiPlayground.Checks/` into a fresh directory
   outside the enclosing repository, preserving their sibling layout. Exclude `bin/`,
   `obj/` and `.tmp/`; do not copy parent build props, docs, licenses or solutions.
2. Set `KSA_DLL_DIR` to an absolute external game/assemblies path.
3. From the copied `ImGuiPlayground/` folder, run:

   ```bash
   dotnet build ImGuiPlayground.slnx --nologo -v quiet
   dotnet run --project ../ImGuiPlayground.Checks -- --packaging
   dotnet run --project ../ImGuiPlayground.Checks
   dotnet run --project ImGuiPlayground.csproj -- --capture .tmp/widgets/camera.png
   ```

4. Confirm changes remain confined to the standalone folders and no deployment occurs.
   For stronger isolation coverage, place failing `Directory.Build.props`,
   `Directory.Build.targets` and `Directory.Packages.props` files in the temporary parent:
   neither project should import them. A parent NuGet config should likewise not replace
   the explicitly selected playground config.

## Files and licensing

- `Program.cs`: main-thread rendering/capture fixtures and assertions.
- `InteropChecks.cs`: native callback/state-reset and input compatibility fixtures.
- `WidgetGalleryChecks.cs`: opt-in `--widgets` scene/popup captures and Play/Loop input assertions.
- `BindingContractExporter.cs`: opt-in complete selected-assembly signature/type/layout metadata
  export; no native initialization or decompilation dependency.
- `BindingContractExporterChecks.cs`: quiet structural/config/malformed-input fixtures and optional
  real-selected-input determinism, graph closure, identity and missing-dependency tests.
- `PrototypeAbiChecks.cs`: opt-in strict probe/layout/export gates, managed ABI calls,
  reverse callbacks, negative fixtures, and optional machine-readable results.
- `prototype_runner.py`: bounded disposable staging and isolated ABI/render/capture processes.
- `prototype_tooling_checks.py`: scratch redirection and child-tree cancellation regressions.
- `PackagingChecks.cs`: bounded child builds and platform-asset validation.
- [LICENSE](LICENSE): original project code; third-party terms and caveats are in the
  playground's [THIRD-PARTY-NOTICES.md](../ImGuiPlayground/THIRD-PARTY-NOTICES.md).
