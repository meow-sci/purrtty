# ImGui Playground

A standalone **.NET 10 / C#** application for iterating on KSA-compatible ImGui UI without
launching Kitten Space Agency. The default UI is a **local mockup gallery**: camera
sequencer, parts workshop and tuning panel, plus an opt-in Dear ImGui demo window.
All data is fake; there is no game connection, persistence, network or new UI dependency.

The application still calls the actual **`Brutal.ImGui.dll` API directly**. Hosting,
GLFW input, OpenGL rendering, framebuffer/PNG capture, native accessors and native/font
delivery now belong to the **`XBrutal.ImGuiStandalone` NuGet package**, not this application.
The package ships source-owned Dear ImGui 1.92.2 docking implementations, not the old
game-derived native wrappers. GLFW remains its exact transitive **`Ultz.Native.GLFW` 3.4.0**
dependency; GLFW is not vendored here or in the XBrutal package. Ordinary builds never
compile native code. A system graphics driver is still required.

## Independent consumer boundary

`ImGuiPlayground/` and sibling `ImGuiPlayground.Checks/` have their own solution, props,
NuGet configuration, licenses, `.editorconfig` and `.gitignore`. They do not reference
purrTTY projects, inherit mod build settings or deploy to the game. Keep both folders side
by side when extracting them. No enclosing repository files are required, but the package
feed and user-supplied BRUTAL managed closure must be available.

- `Program.cs`: unchanged scene/smoke/capture CLI, calling `StandaloneHost`.
- `WidgetGallery.cs`: direct BRUTAL fake-data UI and per-run state; the local iteration seam.
- `ImGuiPlayground.slnx`: only this application and its consumer checks.
- `Directory.Build.props`: explicit external assembly selection and isolated build settings.
- `NuGet.Config`: scoped local feed plus nuget.org; package-source mapping reserves
  `XBrutal.ImGuiStandalone` for the local feed. Both projects explicitly use this config.
- [Checks README](../ImGuiPlayground.Checks/README.md): application/package/gallery checks.

There is **no producer ProjectReference, linked-source fallback, manual native copy target,
or local host/renderer/input/accessor implementation**. Reusable implementation tests now
live in the producer repository `brutal-imgui-standalone/checks/`.

## Package and external managed inputs

The application pins **`XBrutal.ImGuiStandalone` `[0.1.0-local.4]`**. The configured local
feed is `/Users/asherwin/nuget-feed`; when extracting to another machine, change only that
source to the directory containing the same immutable package. The approved archive is:

```text
XBrutal.ImGuiStandalone.0.1.0-local.4.nupkg
SHA-256: 1c2c4a35ba6c3004a6bbb34c1ff7bc7d53946a95b00eb5b880f305222377bd20
```

The producer's README, `docs/COMPATIBILITY.md`, native manifest and accessor contract are
also included in the restored package. Those define native/loaded-managed identity and raw
layout restrictions. Same assembly version numbers alone do not establish compatibility.
The package validates loaded ImGui/Common/Numerics hashes and MVIDs before unsafe native
calls; unsupported managed inputs or stale/tampered native assets fail explicitly. Do not
work around these guards by substituting a game/system/Homebrew native library.

**Commands below run from `ImGuiPlayground/`**. Supply your own licensed BRUTAL managed
assembly closure (ImGui, Glfw, Common, Numerics, Strings and normal supporting assemblies).
There is no automatic game-directory lookup and the host does not need `KSA.dll`.
`KSAFolder` takes precedence over `KSA_DLL_DIR`, so unset a conflicting `KSAFolder`.

```bash
export KSA_DLL_DIR='/absolute/path/to/matching/managed/dll'
unset KSAFolder
dotnet build ImGuiPlayground.slnx --nologo -v quiet
# One-build alternative:
dotnet build ImGuiPlayground.slnx -p:KSAFolder='/absolute/path/to/matching/managed/dll'
```

PowerShell: `$env:KSA_DLL_DIR = 'C:\path\to\matching\managed\dll'` and remove a conflicting
`Env:KSAFolder`. File references are copy-local; no proprietary assemblies are supplied by
NuGet, checked into this workspace or modified. Use environment selection for `--packaging`:
child builds do not inherit outer `dotnet run -p:` overrides.

The package delivers ImGui and GLFW aliases automatically. **Portable/no-RID** outputs
retain `runtimes/<rid>/native/`; explicit RID publishes flatten matching native assets.
JetBrains Mono Regular is automatically staged into `fonts/` and loaded at **18 logical
pixels**, with its debug name unchanged. There is no local native/font staging owner.
`KSA_NATIVE_DIR`, `KSA_GLFW_DIR` and the old `NativeLibraries.props` are not used.

```bash
dotnet publish ImGuiPlayground.csproj -o /tmp/playground-portable
dotnet publish ImGuiPlayground.csproj -r osx-arm64 --self-contained false -o /tmp/playground-osx
dotnet publish ImGuiPlayground.csproj -r linux-x64 --self-contained false -o /tmp/playground-linux
dotnet publish ImGuiPlayground.csproj -r win-x64 --self-contained false -o /tmp/playground-win
```

Requires .NET 10 and desktop OpenGL **3.2 core / GLSL 150**. Supported process RIDs are
**osx-arm64, linux-x64, win-x64**; Intel macOS, ARM Windows/Linux, musl and 32-bit are
unsupported. Cross-publish is not foreign-OS runtime certification. macOS ARM64 automatic
consumer gates have passed; Windows/Linux execution and manual desktop/DPI checks remain
pending. Normal OS GLFW/OpenGL dependencies still apply: Windows GLFW requires the Microsoft
Visual C++ x64 Redistributable; Linux needs its graphical/window-system libraries and GL.
The owned ImGui's glibc 2.17 target baseline is a build contract, not oldest-OS runtime
certification. See [platform status](VALIDATE_PLATFORMS.md) and package compatibility/provenance.

## Run and iterate

```bash
dotnet run --project ImGuiPlayground.csproj
dotnet run --project ImGuiPlayground.csproj --no-build
dotnet run --project ImGuiPlayground.csproj -- --help
dotnet run --project ImGuiPlayground.csproj -- --mockup parts
dotnet run --project ImGuiPlayground.csproj -- --mockup tuning --smoke-test
dotnet run --project ImGuiPlayground.csproj -- --mockup demo --capture /tmp/demo.png
```

`--mockup camera|parts|tuning|demo` can precede or follow `--smoke-test` or `--capture <file>`.
Without selection, smoke/capture renders the camera page. `demo` directly renders the built-in
demo without a gallery underneath. Close that demo to return to the gallery interactively.
Close the native window to quit. No `imgui.ini` or log file is written.

### Gallery pages and widget coverage

- **Camera sequencer:** working play/pause/rewind, disabled buttons, looping/progress/scrubbing,
  selectable keyframes, name/multiline notes, duration drag, easing combo, camera-mode radio
  buttons and expanded summary tree.
- **Parts workshop:** live name/category filter, warnings checkbox, scrolling status table,
  selectable rows/context menu, editable stock, details tree, collapsing log and reload modal.
- **Tuning panel:** resettable drag/slider/numeric fields, normalization checkbox, built-in
  plots, color editor and popup picker; edits update synthetic previews.
- Tabs/scrolling children fit the **800×500 logical viewport**. Gallery/Help menus and tooltips
  expose guidance; **Show ImGui demo** opens the wider catalog on demand.

These original mockups are inspired only by UI patterns in the read-only `unscience`
repository: `camera-controller-override.lib/UI/KeyframeSequencePanel.cs`,
`parts-now.lib/Ui/ResultsPanel.cs`, `kitten-animations.lib/Ui/TuningSection.cs`.
No reference-repository code/project/game-service dependency is introduced. Owned byte arrays
back text inputs; the selected BRUTAL `ImGui.Text` overload is native `TextUnformatted`, so
literal percent signs in dynamic/user text are safe.

```csharp
using Brutal.ImGuiApi;
using XBrutal.ImGuiStandalone;

StandaloneHost.Run(() => {
    ImGui.Begin("My UI"u8);
    try { ImGui.Text("Direct BRUTAL calls"u8); }
    finally { ImGui.End(); }
});
```

Call synchronously on the OS main thread, not `Task.Run` or async continuations. One host owns
ImGui/GLFW/OpenGL contexts and ImGui's external native resolver at a time; do not initialize
competing game/UI backends. Keep UI stacks balanced even when throwing. Repeated synchronous
sessions are supported; native handles remain loaded for process lifetime.

**Display scaling:** the package retains Windows `ScaleToMonitor` plus logical display/mouse
conversion; macOS retains logical-window/Retina-framebuffer mapping. Do not add another
font/style multiplier. Actual Windows 250% scaling and cross-monitor moves still require
native desktop validation. The owned native image uses **WCHAR32** (unlike the old macOS
wrapper's supplementary-codepoint replacement); this does not repair unsafe raw BRUTAL aliases.

## Capture and validation

```bash
dotnet run --project ImGuiPlayground.csproj -- --capture /tmp/camera.png
dotnet run --project ImGuiPlayground.csproj -- --smoke-test
dotnet run --project ../ImGuiPlayground.Checks -v quiet
dotnet run --project ../ImGuiPlayground.Checks --no-build -- --widgets /tmp/widget-captures
dotnet run --project ../ImGuiPlayground.Checks --no-build -- --packaging
```

Capture renders three nonempty frames in an invisible GLFW window, reads the framebuffer
before swap and exits quietly. No OS screenshot/accessibility permission is required, but a
working graphical session/driver is: this is **not a headless backend**. Smoke uses a visible
window and checks three nonempty frames and GL errors. `CapturedFrame` exposes `Width`,
`Height`, top-down tightly packed `ReadOnlyMemory<byte> Rgba` and `SavePng(path)`.
Requested size is logical; returned dimensions are physical (800×500 gave 1600×1000 on the
tested Retina display). Cross-driver/platform identical pixels are not promised.

Default Checks captures the application via the package and asserts frame delivery and font.
`--widgets` retains **nine PNGs**, checks pages/demo/popups and direct ImGui IO Play/Loop
interaction. `--packaging` checks portable and all three RID consumer outputs against the
resolved immutable package/GLFW assets. It needs no desktop and never executes foreign images.
Checks are quiet on success and have no fixed sleeps. Lifecycle, rendering algorithms, input,
PNG encoding, native exports/helpers and startup-negative checks belong to the producer now.

## Historical native tooling boundary

The deleted native generator/prototype/full-fixture tooling and qualifier-only Checks are
archived **exactly** at purrtty revision **`5d71c50943c13310d02066eb56b6fd4abc914790`**.
For example, inspect historical instructions without pretending they are current commands:

```bash
git show 5d71c50943c13310d02066eb56b6fd4abc914790:ImGuiPlayground/native/full/qualification/README.md
```

The producer owns the frozen production source closure, native rebuild/verification and
provenance. Historical production bundle `sol-delivery-20261008T144358Z-33442-combined-final3`
and qualification archive hash `b0c089d45a6aeac64e8affa9ddbc2fb48675ec1c00d23e805f346be98cd5fd39`
are recorded in its `native/manifest.json` (also package `docs/manifest.json`). Full historical
fixture qualification was **not relocated or rerun**; future regeneration/full requalification
is a separate task. Existing ignored `.tmp` evidence is preserved, not an active tool dependency.

## Limits and licensing

Not reproduced: KSA game services/theme/Vulkan textures, OS multiviewport windows, gamepads,
cursor-shape/warping backend, complete IME, mod lifecycle/hot reload or general UI automation.
Shared UI callbacks must keep host-specific services out. Complete native export coverage is
not unrestricted safe raw layout access; consult the package's compatibility/accessor contract.

See [LICENSE](LICENSE), [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) and `licenses/`.
The package includes native/font provenance and notices (including JetBrains Mono's SIL OFL).
Managed BRUTAL/KSA libraries remain licensed user-supplied components; resolve their
redistribution rights before publishing application output. No remote distribution is configured.
