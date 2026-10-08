# ImGui Playground

A standalone **.NET 10 / C#** host for iterating on KSA-compatible ImGui UI without
launching Kitten Space Agency. The initial window displays **Hello world!**

It uses the actual **`Brutal.ImGui.dll` API and original native ImGui library**,
plus `Brutal.Glfw.dll` for windowing/input and a small **managed OpenGL renderer**.
The ordinary host, checks, and packaging path has **no custom production C++ bridge,
native compilation, CMake, Metal backend, Vulkan SDK, ImGui.NET, or Hexa.NET**. A separate,
explicit maintainer-only [full native qualification runner](native/full/qualification/README.md)
and the historical bounded prototype are separate opt-in tools; neither is used by ordinary
app builds or promoted to runtime files. Full-image macOS fixture/renderer/input/capture gates
are exercised through the real Host/Checks boundary; Windows/Linux execution remains pending.
No external game Content directory, StarMap, or Harmony dependency.
Native ImGui is vendored per platform; GLFW restores from NuGet. Only the managed
KSA/BRUTAL assemblies remain external. A system graphics driver is still required.

## Independent project boundary

`ImGuiPlayground/` and its sibling `ImGuiPlayground.Checks/` are a self-contained development
workspace, temporarily hosted in another repository for convenience. They do not reference
purrTTY projects, inherit its build settings, participate in its solution/CI/deploy, or use
its documentation or licenses. Keep both folders side by side when extracting them; no
files from the enclosing repository are needed.

- `ImGuiPlayground.slnx` contains only the host and companion checks.
- Local `Directory.Build.props` files own .NET/C# settings and managed assembly resolution.
  The checks import only the host's settings. Parent `Directory.Build.props`,
  `Directory.Build.targets`, and `Directory.Packages.props` are not inherited.
- `NuGet.Config` is explicitly selected for both projects; `.editorconfig` and `.gitignore`
  are local. No mod deployment paths or targets are defined.
- Documentation, licenses, font and native assets live in these two folders. Check-specific
  instructions are in [the checks README](../ImGuiPlayground.Checks/README.md).

**Commands below run from this `ImGuiPlayground/` folder**, not an enclosing repository root.
Configure the external managed-assembly directory first.

## Platforms and prerequisites

The same C# source requests desktop OpenGL **3.2 core / GLSL 150** on **macOS arm64,
Linux x64, and Windows x64**. **Runtime validated on macOS Apple Silicon only so far**;
all three RIDs have packaging checks, but Windows/Linux execution still needs validation.
The separate full-native effort's target checklist and current gate status are in
[VALIDATE_PLATFORMS.md](VALIDATE_PLATFORMS.md).
Other RIDs (including Intel macOS, Windows ARM and Linux musl) are not packaged.

- .NET 10 SDK. Standard Microsoft logging/object-pool packages restore through NuGet;
  the current BRUTAL assemblies reference ObjectPool **11.0.0.0**, so the project pins
  its compatible `11.0.0-rc.1.26425.128` package (netstandard asset on .NET 10).
- A graphical session and an OpenGL 3.2-capable driver. OpenGL is deprecated but
  available on macOS; this is a lightweight developer harness, not a new game renderer.
- User-supplied managed BRUTAL assemblies via `KSA_DLL_DIR` or `KSAFolder`, resolved
  by this folder's own `Directory.Build.props`. There is no machine-specific fallback.
- Native ImGui comes from the checked-in **`runtimes/<rid>/native/`** files; native
  GLFW comes from pinned **`Ultz.Native.GLFW` 3.4.0**. No native installation path,
  Homebrew GLFW, native compiler, or custom bridge is required for ordinary builds/runs.
  Only explicit maintainer native **builds** need Zig 0.17.0; extracted full-qualification
  bundles execute target probes without installing a native toolchain.
- Normal OS runtime dependencies still apply: Linux requires glibc (the supplied
  ImGui imports symbols through 2.29), libstdc++, window-system libraries and GL;
  Windows requires the Microsoft Visual C++ x64 Redistributable. See
  [native runtime provenance and prerequisites](runtimes/README.md).

A generic cimgui build is **not** compatible: BRUTAL has its own exports and layouts.
Current BRUTAL uses **Dear ImGui 1.92.2 docking** and 16-bit draw indices. Startup
checks version and the public native/managed layout sizes. Do not bypass checks:
`DebugCheckVersionAndDataLayout` may deliberately assert on incompatible libraries.
These size checks do **not** prove every generated binding field is compatible;
see the older macOS runtime's character-width limitation below.

## Configure the managed game directory

Set **`KSA_DLL_DIR`** to an absolute path containing the managed `Brutal.*.dll` assemblies
and their dependencies, not a native-runtime subdirectory. A full game directory or a
reference-assembly directory works. The host itself does not need `KSA.dll`.

```bash
export KSA_DLL_DIR="/path/to/KSA"
dotnet build ImGuiPlayground.slnx
# One-build alternative (KSAFolder is also accepted, and takes precedence):
dotnet build ImGuiPlayground.slnx -p:KSA_DLL_DIR="/path/to/KSA"
```

On Windows, for example:

```powershell
$env:KSA_DLL_DIR = 'C:\Program Files\Kitten Space Agency'
```

There is deliberately **no enclosing-repository lookup or automatic game-directory default**.
Only required managed assemblies are referenced and copied to output as normal .NET
runtime dependencies; their originals remain external. No game installation is modified.
Use the environment variable for packaging checks: their child `dotnet` processes do not
inherit MSBuild `-p:` overrides supplied to the outer `dotnet run`.

**`KSA_NATIVE_DIR`, `KSA_GLFW_DIR`, and `Playground.local.props` are no longer used.**
Native filenames and SHA-256 hashes are pinned in `runtimes/NativeLibraries.props`.
Build/publish selects `RuntimeIdentifier` when specified, otherwise the SDK host RID,
and stages the selected pair beside the app. Unsupported RIDs, missing assets, or hash
mismatches fail explicitly.

To cross-publish, use a separate directory for each target:

```bash
dotnet publish ImGuiPlayground.csproj -r osx-arm64 --self-contained false -o .tmp/publish/osx-arm64
dotnet publish ImGuiPlayground.csproj -r linux-x64 --self-contained false -o .tmp/publish/linux-x64
dotnet publish ImGuiPlayground.csproj -r win-x64 --self-contained false -o .tmp/publish/win-x64
```

These are per-RID outputs, not a universal zip. Framework-dependent outputs also require
.NET 10 on the destination machine. Cross-publishing verifies packaging, not execution on
that OS. Native provenance, system dependencies and upgrade rules: [runtimes/README.md](runtimes/README.md).

## Run and iterate

```bash
dotnet run --project ImGuiPlayground.csproj
dotnet run --project ImGuiPlayground.csproj --no-build  # after a build
```

Edit **`Program.DrawHelloWorld()`**. These are ordinary KSA mod UI calls:

```csharp
using Brutal.ImGuiApi;
using Brutal.Numerics;

ImGui.SetNextWindowSize(new float2(360, 160), ImGuiCond.FirstUseEver);
bool visible = ImGui.Begin("My UI"u8);
try
{
    if (visible)
        ImGui.Text("Hello world!"u8);
}
finally { ImGui.End(); }
```

Close the native window to quit. Keyboard navigation, Unicode character input,
mouse buttons/motion/wheel/focus, and clipboard use the managed platform adapter.
The default font is **the game's `JetBrainsMono-Regular.ttf`**, copied unmodified from
`ksa-linux/Content/Core/` with its OFL license into `fonts/`. It is bundled into build
and publish output and loaded at **18 logical pixels**; the Linux game folder is not
needed at runtime. This matches the font face, not the entire game theme or DPI setup.
No `imgui.ini` or log file is written.

`PlaygroundHost.Run(Action drawUi)` is the future library-extraction boundary,
**not yet a packaged library**. Call synchronously from `Main`, not `Task.Run` or
an async continuation. It owns the current ImGui/GLFW/OpenGL contexts and ImGui's
native resolver; do not combine it with another host in the process. Sequential
runs are supported. Keep ImGui stacks balanced, even when your callback throws.
BRUTAL's frame-scoped UTF-8 string storage resets each frame.

## Capture rendered pixels — no OS screenshots

```bash
dotnet run --project ImGuiPlayground.csproj -- --capture .tmp/hello.png
```

This renders three nonempty frames in an **invisible GLFW window**, reads the back
buffer **before swap**, writes a PNG, and exits quietly. It needs no Orca, accessibility,
or screen-recording permissions, and captures no other windows or OS decorations.
It still needs a working graphical session/driver: invisible does **not** mean
truly display-server-free. Linux CI can provide Xvfb + Mesa and the OS libraries
(e.g. `xvfb-run -a dotnet run --project ImGuiPlayground.csproj -- --capture hello.png`);
that setup has not yet been runtime-tested here.

The reusable API exposes the actual image data for automated assertions:

```csharp
CapturedFrame frame = PlaygroundHost.Capture(DrawMyUi, width: 800, height: 500);
ReadOnlyMemory<byte> pixels = frame.Rgba;  // owned RGBA8, tightly packed, top row first
frame.SavePng("artifacts/my-ui.png");
// Assert regions/colors or compare a baseline using frame.Width/Height and pixels.
```

Requested width/height are **window coordinates**; returned dimensions are **physical
framebuffer pixels** (800×500 yielded 1600×1000 on the tested Retina display). Pin DPI,
fonts, theme, timing and driver for stable image comparisons; identical pixels across
OS/driver combinations are not promised. This API does not yet provide scripted input
or a configurable capture frame/time.

## Validation

```bash
dotnet build ImGuiPlayground.slnx --nologo -v quiet
dotnet run --project ImGuiPlayground.csproj --no-build -- --smoke-test
dotnet run --project ../ImGuiPlayground.Checks -v quiet
# No desktop required; cross-publishes/checks all three RIDs:
dotnet run --project ../ImGuiPlayground.Checks -- --packaging
```

Smoke mode opens a visible window, requires three nonempty frames, waits for GL
completion and checks GL errors on each frame. Capture uses the same checks plus
synchronous framebuffer readback. Neither alone proves the intended widgets/pixels;
the optional checks executable asserts image regions/orientation, PNG encoding,
default font selection/texture updates, draw-callback state reset, key translation,
character callback fidelity, and callback-failure recovery on the main thread. Checks
are quiet on pass, have no fixed sleeps, and need the same native/desktop dependencies.
The separate `--packaging` checks locate the sibling source project without assuming a
particular working directory and run without opening a window:
all three RID outputs, per-file hashes/architectures, unsupported-RID rejection, hash
mismatch rejection and replacement of stale native files with newer timestamps.
Also manually verify input, resizing, minimize/restore and closing on each platform.

Checks belong only to the standalone solution. See [the checks README](../ImGuiPlayground.Checks/README.md)
for execution details and an extraction verification recipe.

## Experimental native layout/ABI feasibility prototype

The prototype is an opt-in maintainer experiment, not a replacement native runtime and not a
production compatibility claim. Its current `osx-arm64` artifact was built from pinned vanilla
Dear ImGui commit `031a18c417158427217bc5890e0ec0cb7e7b4b63` using the measured prototype config
hypotheses. On this Apple Silicon host, the managed staged runner passed its pre-context
identity/schema/config/export gates, required public-subset layout gate, managed calls, all 13
negative fixtures, unchanged render/input checks, and hello-world capture. The observed native
SHA-256 is `d1481c0c62ef24b125934523f5f0c90c8f9819841e1c780acc0d62a567e7ae3b`; the external
`Brutal.ImGui.dll` SHA-256 was `9040dc5d410043c53dbea10d94995b6712240be37f9251b016f6cff66a76339f`.

The 1,146 reflected managed imports were checked individually: only 54 are present in this
72-symbol prototype (54 managed imports, three data slots, 15 other functions); the other 1,092
imports, including `TextV`, remain unsupported. All 30 native layout records mapped to reflected
managed types, but only 28 of 30 measured type sizes matched. `ImGuiContext` is 11,312 native
bytes versus 11,320 managed bytes; `ImGuiStackLevelInfo` is 64 versus 72 bytes. Across the 195
measured native fields, 190 matched; the five reported offset mismatches are in risky internal
types (`ImGuiBoxSelectState`, `ImGuiContext`, and `ImGuiStackLevelInfo`). These internal size
and field mismatches are outside the required host/public subset gate. The 24 individual native
bitfield locations/widths remain unmeasured and unproven by this prototype. An independent managed inventory
requires every raw field used by the current host/checks, including draw-list buffers and their
concrete vector headers. Missing fields and schema-valid offset/size mismatches fail before
context creation. This gate is narrower than complete layout parity; sample field offsets do
not imply all-layout parity. The `IMGUI_USE_WCHAR32` and obsolete-field settings
are not authenticated KSA build metadata.

Linux x64 and Windows x64 artifacts were rebuilt and statically inspected, but **not** loaded
or executed. No Wine-as-native claim is made. On a matching native target host, build its
artifact explicitly and run the portable staged check as described in
[Checks README](../ImGuiPlayground.Checks/README.md); exact prototype implementation, current
hashes, omissions, and target-side commands are in [native prototype README](native/README.md).
All prototype libraries remain in ignored `.tmp/native/`; production `runtimes/` and native pins
are untouched. Ordinary builds still populate their standard `bin/` outputs with pinned
production assets; only disposable copies are replaced with the prototype by the staged runner.

## Full native API work — in progress

The accepted prototype above is historical, bounded evidence, not the full implementation.
The expanded effort keeps the external managed DLL unchanged and permits narrowly scoped,
hash-pinned native declaration/configuration patches plus owned native-backed C# helpers.
It does not authorize rewriting ImGui widget behavior or promoting artifacts into `runtimes/`.

The [metadata exporter](../ImGuiPlayground.Checks/README.md#maintainer-only-managed-binding-contract-no-native-loading)
now audits the actual selected DLL without loading native ImGui. The current ImGui DLL hash is
`b76777a4ef3399d6353b9dba1982e3afb84b5da2aa2500da1c062db0bfad827c`, different from the
prototype-tested input despite the same ImGui version and 1,146 import names. Native mapping,
complete classified layout evidence, safe helpers and full behavioral qualification are still
being implemented; the exporter alone certifies none of them.

Some unchanged managed fields are overlapping full-width aliases for distinct native bitfields.
They cannot be made independently writable by a native layout patch. Other records have wrong
managed element strides or represent opaque native storage. The planned helpers must use real
native accessors/indexing, while reports retain those original raw limitations explicitly.
Do not treat the original aliases as repaired or use metadata size agreement as semantic proof.
See [the platform validation checklist](VALIDATE_PLATFORMS.md) for separate coverage, behavior
and per-target execution gates.

## Implementation and limits

```text
UI callback → Brutal.ImGui.dll → original native ImGui (one context)
PlaygroundHost / GlfwInput → external Brutal.Glfw.dll → NuGet native GLFW
OpenGlRenderer → GL entry points loaded by GLFW → system graphics driver
CapturedFrame → owned RGBA bytes / PNG via .NET APIs
```

- `Program.cs`: hello-world UI callback; the entry point for UI iteration.
- `Directory.Build.props` / `NuGet.Config`: independent build/reference/package settings.
- `runtimes/NativeLibraries.props`: selected native filenames and SHA-256 pins;
  `runtimes/README.md`: provenance, OS dependencies and upgrade rules.
- `fonts/`: unmodified default game font, OFL license and provenance.
- `PlaygroundHost.cs`: native loading/ABI checks, synchronous lifecycle and teardown.
- `GlfwInput.cs`: key translation, input events, stable UTF-8 clipboard callbacks.
- `OpenGl.cs` / `OpenGlRenderer.cs`: GL entry points, dynamic ImGui texture requests,
  geometry/scissors/base-vertex offsets, GPU cleanup, framebuffer readback. Dedicated
  GL state, not a drop-in backend for rendering alongside another engine.
- `CapturedFrame.cs`: top-down RGBA ownership and PNG encoding with BCL zlib + CRC.

The renderer follows the contracts/shader math of upstream `imgui_impl_opengl3.cpp`
at `v1.92.2-docking` (`04c3466d23a72abee3696dcba698b0e02fee6057`), ported to C#.
It uploads full RGBA textures on atlas updates for simplicity. Ordinary app/checks builds do
not download or compile upstream C++ sources. Only explicit invocation of the isolated,
maintainer-only native ABI prototype downloads the hash-pinned vanilla source and compiles its
core/demo plus the owned subset adapter into ignored `.tmp/native/` scratch; no output is
promoted into `runtimes/`.

**Older macOS native-runtime limitation:** the available arm64 ImGui library was
observed to use 16-bit `ImWchar` despite the current generated binding declaring
`InputQueueCharacters` as `ImVector<uint>`. It replaces supplementary codepoints
(such as emoji) with U+FFFD. The host forwards the full GLFW codepoint, avoiding a
separate truncation in BRUTAL's `OnChar` wrapper, but cannot add native-engine Unicode
support. It does not read wchar-backed native vectors. **Do not use the generated
`InputQueueCharacters.Span` or assume arbitrary raw wchar-backed structures are safe
with this older library.** A fully matching native build is needed for unrestricted
binding/Unicode parity; the experimental source build preserves WCHAR32 input but does not
establish full-binding parity and is not shipped as a runtime.

Not reproduced: the rest of KSA's font setup/theme/game services/Vulkan texture handles, multi-viewport
platform windows, gamepads, OS cursor-shape changes/warping, full IME composition UI,
mod-loader lifecycle, hot reload, or UI automation. Mod images need OpenGL-compatible
fixtures, not game Vulkan handles. Keep host-specific services out of shared UI code.

## Licensing

See [LICENSE](LICENSE), [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md), and `licenses/`.
The managed backend's Dear ImGui
reference is MIT licensed; GLFW is zlib/libpng licensed; Microsoft packages are MIT.
The bundled JetBrains Mono font is SIL OFL 1.1 (license/provenance in `fonts/`).
These notices are copied to playground output. Managed BRUTAL/KSA libraries remain
external, user-supplied game components. The platform-specific native ImGui binaries
are vendored at the operator's request; see [their provenance](runtimes/README.md).
Upstream ImGui's MIT license does not establish redistribution rights for KSA's complete
native wrapper. Do not publish game-derived binaries/build output without resolving
those permissions.
