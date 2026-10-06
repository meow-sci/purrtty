# ImGui Playground

A standalone **.NET 10 / C#** host for iterating on KSA-compatible ImGui UI without
launching Kitten Space Agency. The initial window displays **Hello world!**

It uses the actual **`Brutal.ImGui.dll` API and original native ImGui library**,
plus `Brutal.Glfw.dll` for windowing/input and a small **managed OpenGL renderer**.
There is **no custom C++ bridge, native compilation, CMake, Metal backend, Vulkan
SDK, ImGui.NET, or Hexa.NET**. No purrTTY, external game Content directory, StarMap, or Harmony dependency.
Existing native ImGui, GLFW, and the system graphics driver are still required.

## Platforms and prerequisites

The same C# source requests desktop OpenGL **3.2 core / GLSL 150** on **macOS,
Linux, and Windows**, in a 64-bit process. **Runtime validated on macOS Apple Silicon
only so far**; Windows/Linux and Intel macOS need their own validation.

- .NET 10 SDK. Standard Microsoft logging/object-pool packages restore through NuGet;
  the current BRUTAL assemblies reference ObjectPool **11.0.0.0**, so the project pins
  its compatible `11.0.0-rc.1.26425.128` package (netstandard asset on .NET 10).
- A graphical session and an OpenGL 3.2-capable driver. OpenGL is deprecated but
  available on macOS; this is a lightweight developer harness, not a new game renderer.
- KSA managed assemblies via the repository's `Directory.Build.props`:
  `KSA_DLL_DIR`, the adjacent `ksa-game-assemblies/current/dll`, or documented defaults.
- Matching original BRUTAL native ImGui and GLFW 3.3+ libraries for the **running
  OS/CPU architecture**. The managed-only assemblies checkout does not supply them.

| OS | ImGui file | GLFW file |
|---|---|---|
| macOS | `libimgui.dylib` | `libglfw.dylib` |
| Linux | `libimgui.so` | `libglfw.so` |
| Windows | `imgui.dll` | `glfw3.dll` |

A generic cimgui build is **not** compatible: BRUTAL has its own exports and layouts.
Current BRUTAL uses **Dear ImGui 1.92.2 docking** and 16-bit draw indices. Startup
checks version and the public native/managed layout sizes. Do not bypass checks:
`DebugCheckVersionAndDataLayout` may deliberately assert on incompatible libraries.
These size checks do **not** prove every generated binding field is compatible;
see the older macOS runtime's character-width limitation below.

## Configure native libraries once

For example on macOS:

```bash
export KSA_NATIVE_DIR="/path/to/KSA/runtimes/osx-arm64/native"
# Optional if GLFW is elsewhere, e.g. brew install glfw:
export KSA_GLFW_DIR="/opt/homebrew/opt/glfw/lib"
```

Or in PowerShell on Windows:

```powershell
$env:KSA_NATIVE_DIR = 'C:\path\to\KSA\runtimes\win-x64\native'
```

Alternatively, create git-ignored **`ImGuiPlayground/Playground.local.props`**:

```xml
<Project>
    <PropertyGroup>
        <KSA_NATIVE_DIR Condition="'$(KSA_NATIVE_DIR)' == ''">/path/to/native</KSA_NATIVE_DIR>
    </PropertyGroup>
</Project>
```

Environment variables and `-p:KSA_NATIVE_DIR=...` override that local default.
Without an override, the project checks `$(KSAFolder)/runtimes/<os>-<architecture>/native/`,
then the managed assembly directory itself. `KSA_GLFW_DIR` defaults to `KSA_NATIVE_DIR`.
Both native files are copied beside the app; they and their system dependencies must
be available on the target machine. Missing files fail the build with a setup message.
Build on the target OS with its matching native files; this is **not** a universal
multi-RID distribution. No game installation is modified.

## Run and iterate

```bash
dotnet run --project ImGuiPlayground
dotnet run --project ImGuiPlayground --no-build  # after a build
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
dotnet run --project ImGuiPlayground -- --capture .tmp/hello.png
```

This renders three nonempty frames in an **invisible GLFW window**, reads the back
buffer **before swap**, writes a PNG, and exits quietly. It needs no Orca, accessibility,
or screen-recording permissions, and captures no other windows or OS decorations.
It still needs a working graphical session/driver: invisible does **not** mean
truly display-server-free. Linux CI can provide Xvfb + Mesa and matching native files
(e.g. `xvfb-run -a dotnet run --project ImGuiPlayground -- --capture hello.png`);
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
dotnet build ImGuiPlayground --nologo -v quiet
dotnet run --project ImGuiPlayground --no-build -- --smoke-test
dotnet run --project ImGuiPlayground.Checks --nologo -v quiet
```

Smoke mode opens a visible window, requires three nonempty frames, waits for GL
completion and checks GL errors on each frame. Capture uses the same checks plus
synchronous framebuffer readback. Neither alone proves the intended widgets/pixels;
the optional checks executable asserts image regions/orientation, PNG encoding,
default font selection/texture updates, draw-callback state reset, key translation,
character callback fidelity, and callback-failure recovery on the main thread. Checks
are quiet on pass, have no fixed sleeps, and need the same native/desktop dependencies.
Also manually verify input, resizing, minimize/restore and closing on each platform.

Both projects are **outside `purrtty.slnx`**: normal mod builds, tests and releases
remain independent of the optional desktop/native runtime.

## Implementation and limits

```text
UI callback → Brutal.ImGui.dll → original native ImGui (one context)
PlaygroundHost / GlfwInput → Brutal.Glfw.dll → existing native GLFW
OpenGlRenderer → GL entry points loaded by GLFW → system graphics driver
CapturedFrame → owned RGBA bytes / PNG via .NET APIs
```

- `PlaygroundHost.cs`: native loading/ABI checks, synchronous lifecycle and teardown.
- `GlfwInput.cs`: key translation, input events, stable UTF-8 clipboard callbacks.
- `OpenGl.cs` / `OpenGlRenderer.cs`: GL entry points, dynamic ImGui texture requests,
  geometry/scissors/base-vertex offsets, GPU cleanup, framebuffer readback. Dedicated
  GL state, not a drop-in backend for rendering alongside another engine.
- `CapturedFrame.cs`: top-down RGBA ownership and PNG encoding with BCL zlib + CRC.

The renderer follows the contracts/shader math of upstream `imgui_impl_opengl3.cpp`
at `v1.92.2-docking` (`04c3466d23a72abee3696dcba698b0e02fee6057`), ported to C#.
It uploads full RGBA textures on atlas updates for simplicity. No upstream C++
sources are downloaded or compiled during builds.

**Older macOS native-runtime limitation:** the available arm64 ImGui library was
observed to use 16-bit `ImWchar` despite the current generated binding declaring
`InputQueueCharacters` as `ImVector<uint>`. It replaces supplementary codepoints
(such as emoji) with U+FFFD. The host forwards the full GLFW codepoint, avoiding a
separate truncation in BRUTAL's `OnChar` wrapper, but cannot add native-engine Unicode
support. It does not read wchar-backed native vectors. **Do not use the generated
`InputQueueCharacters.Span` or assume arbitrary raw wchar-backed structures are safe
with this older library.** A fully matching native build is needed for unrestricted
binding/Unicode parity; no such build is supplied or compiled by this project.

Not reproduced: the rest of KSA's font setup/theme/game services/Vulkan texture handles, multi-viewport
platform windows, gamepads, OS cursor-shape changes/warping, full IME composition UI,
mod-loader lifecycle, hot reload, or UI automation. Mod images need OpenGL-compatible
fixtures, not game Vulkan handles. Keep host-specific services out of shared UI code.

## Licensing

See `licenses/` and `../THIRD-PARTY-NOTICES.md`. The managed backend's Dear ImGui
reference is MIT licensed; GLFW is zlib/libpng licensed; Microsoft packages are MIT.
The bundled JetBrains Mono font is SIL OFL 1.1 (license/provenance in `fonts/`).
These notices are copied to playground output. BRUTAL/KSA libraries remain third-party
game components supplied by the developer, not checked in or fetched by this project.
Do not publish local build output without resolving their redistribution permissions.
