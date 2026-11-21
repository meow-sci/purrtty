# KSA 5901 compatibility review

Reviewed on **2026-11-20** using `.agents/skills/upgrade-ksa/SKILL.md`.

- Last validated baseline: **2026.11.10.5801**, assemblies commit `4de698e`
  (see [the 5801 review](ksa-5801-compatibility.md)).
- Supplied previous snapshot: **2026.11.10.5801**, the validated baseline. The target's
  `fromRevision` (5801) chains to it, so the changelog has no gap.
- Target: **2026.11.16.5901**, snapshot date **2026-11-17**, assemblies commit `8d2ad13`.
- Reviewed the **5801 → 5901** source and asset diff for every coupling-catalog touchpoint.
  Only six binaries changed: `KSA`, `Brutal.Vulkan`, `Brutal.Vulkan.Abstractions`,
  `Brutal.Vulkan.Vma`, `Planet.Core` and `Planet.Render.Core`. No DLL was added or removed.
  Decomp diffs inside unchanged assemblies, such as the `LogLevel` casts in `ConsoleWindow.cs`,
  are decompiler artifacts from the snapshot host's .NET 11 install.

KSA paths below are relative to `current/decomp/` in the assemblies checkout at `8d2ad13`;
asset paths are relative to `current/Content/`. purrTTY paths are repo-relative.

### Off-screen ImGui backend: dynamic rendering — High

- **KSA delta:** rev 5816 ("Convert all remaining legacy render pass/framebuffer usage to
  dynamic rendering") rewrote `KSA/ImGuiBackendVulkanImpl.cs`:
  - `CreateInfo.RenderPass` and `SubPass` were removed. A required
    `CreateInfo.ColorFormat` (line 27) replaces them.
  - The pipeline is built once in the constructor by `CreatePipeline(VkFormat, …)` (line 1268),
    through `VkPipelineRenderingCreateInfo` (line 1357).
  - That pipeline has one colour attachment format, **no depth format**, and a default
    depth-stencil state.
  - KSA's own backend now passes `renderer.ColorFormat` (`KSA/ImGuiBackend.cs:40`), and
    `Core/Renderer.cs` drops `MainRenderPass`.
- **purrTTY location:** in `purrTTY.GameMod/InWorld/`: `OffscreenImGuiBackend.cs`,
  `OffscreenRenderTarget.cs`, `PerFrameRenderer.cs` and `InWorldTerminalInstance.cs`.
- **Coupling kind:** render/behavioral, direct API.
- **Impact:** GameMod failed to compile (`CreateInfo` has no `RenderPass` or `SubPass`, and
  `ColorFormat` is required). Supplying only the format would not have been enough. The old
  off-screen pass began a `VkRenderPass` with colour and depth attachments, which leaves two
  problems:
  - A dynamic-rendering pipeline is invalid inside a `VkRenderPass`.
  - A rendering scope with a depth attachment does not match a pipeline that declares no depth
    format.
  
  In-world terminals would therefore fail validation or misrender.
- **Change needed:** render redesign, implemented (gotcha 39):
  - The backend takes the target's colour format.
  - `OffscreenRenderTarget` now owns only a colour image, view and sampler. The render pass,
    framebuffer and the never-sampled depth image were removed.
  - `PerFrameRenderer` records a colour-only `BeginRendering` scope. It clears to transparent,
    as before.
  - Dynamic rendering performs no implicit layout transitions, so `PerFrameRenderer` issues the
    two the deleted render pass did, through KSA's `CommandBuffer.TransitionImages2`:
    - **Before the scope:** `Undefined` → `ColorAttachmentOptimal`. The source stages are
      `ColorAttachmentOutput | FragmentShader`, which covers the previous frame's quad
      sampling and our own previous writes.
    - **After the scope:** → `SampledReadF` (`ShaderReadOnlyOptimal`), the layout
      `InWorldQuad`'s descriptor declares.
  - A live grid resize still does not rebuild the backend, because the pipeline depends only on
    the unchanged colour format.

### Input gates and fallback menu — Low; compatible

- **KSA delta:**
  - `KSA/Program.cs:1985`: the `OnKey` gate chain adds `PartPicking.OnKey` before
    `WantCaptureKeyboard`. It consumes only the `Cancel` (Esc) press and its matching release
    while a part pick is active (`KSA/PartPicking.cs:179`; rev 5886).
  - `KSA/Program.cs:2264`: `OnMouseButton` adds `PartPicking.OnMouseButton` just before the
    mouse-binding dispatch. It consumes left or right **releases** while picking
    (`PartPicking.cs:148`).
  - `KSA/Program.cs:2034`: `DispatchKeyEvent` gains the release-arm `ToggleCrewClock` (rev 5863).
  - `OnKey`, `DispatchKeyEvent(in GlfwKeyEvent)`, `GameSettings.OnKeyAll`
    (`KSA/GameSettings.cs:3791`) and `DrawProgramMenusHook` (`KSA/Program.cs:4338`) keep their
    signatures and placement.
  - `KfsIntegration.BeforeInputEvents` (rev 5888) runs before input polling and is not a key
    handler.
- **purrTTY location:** `purrTTY.GameMod/Patcher.cs` (`Patch01`, `Patch03_HotkeyGuard`,
  `Patch04_MouseBindingGate`, `Patch02`) and `InWorld/InWorldTerminalManager.cs`.
- **Coupling kind:** Harmony, input/behavioral.
- **Impact:** compatible.
  - Patch01 runs before the whole chain, so a press the terminal swallows never reaches
    `PartPicking`. A press the game saw always gets its release forwarded, so `PartPicking`'s
    swallowed-release flag cannot go stale.
  - The new release-arm toggle is covered generically by the held-key model, and mouse-binding
    gating is unchanged.
  - In-world click focus polls `ImGui.IsMouseClicked`, not KSA's `OnMouseButton` chain, so
    `PartPicking` cannot consume terminal clicks.
- **Change needed:** none. A click on an in-world terminal while a life-support part pick is
  active can still act on both; that overlap already exists for vehicle clicks.

### Menu popups — Low; optional parity, implemented

- **KSA delta:** rev 5808 adds `ImGuiHelper.BeginMenu` (`KSA/ImGuiHelper.cs:1659`). It pins
  the popup to the window's viewport, which fixes a hover offset in windowed mode on some Linux
  desktops, and caps its height so long menus scroll. Every KSA menu now uses it.
- **purrTTY location:** the fallback top-level menu (`Patcher.cs:Patch02`) and the four
  submenus in `TerminalMenus.cs`.
- **Coupling kind:** direct API, behavioral.
- **Impact:** `ImGui.BeginMenu` still works, but purrTTY's menus would keep the Linux offset
  KSA just fixed.
- **Change needed:** mechanical, implemented: open menus through `ImGuiHelper.BeginMenu`.

### Translucency injection, render order and shader assets — Low; compatible

- **KSA delta:**
  - `SuperMeshRenderSystem`, `KSA.Rendering/RenderTarget`, `ImageBarrierInfo`, `CommandBufferEx`,
    `PartModelGlass` and every preset/helper type the quad uses are source-identical.
  - The main pass moved its final swapchain composition to dynamic rendering, and cloud
    lighting/screen shadows changed (revs 5876, 5879).
  - The order is unchanged: opaque main pass → ocean (`KSA/Program.cs:5246`) →
    atmosphere/clouds (5255) → transparency MSAA resolve (5273) → **translucency pass** (5281) →
    resolve (5284) → underwater (5288) → bloom (5296) → part outline (5297).
  - Scene colour remains `B10G11R11UfloatPack32` (`KSA/Program.cs:229`).
  - `Core/DefaultAssets.xml:55` still registers `UnlitMeshVert`, and `Shaders/Mesh/UnlitMesh.vert`
    is unchanged.
- **purrTTY location:** in GameMod's `InWorld/`: `Patches/RenderTranslucencyPassPatch.cs` and
  `Display/SharedQuadResource.cs`.
- **Coupling kind:** Harmony, render/behavioral, asset-id.
- **Impact:** compatible. The postfix still runs after every pass that rederives colour from
  depth. Its scope and tracked barriers are unchanged, and the quad pipeline reads attachment
  formats and MSAA dynamically.
- **Change needed:** none.

### ImGui textures, fonts, reflection and console capture — Low; compatible

- **KSA delta:** the Brutal ImGui, Logging and Glfw assemblies are binary-identical.
  `AddTexture`/`RemoveTexture` (`KSA/ImGuiBackendVulkanImpl.cs:1049`/`1058`), the
  1000-descriptor main pool (`KSA/ImGuiBackend.cs:44`), `SimpleVkTexture` and `FontManager` are
  source-identical. The `ConsoleWindow.cs` diff is only decompiler `LogLevel` casts.
  `Print(ReadOnlySpan<char>, ImColor8, int)` remains the string sink.
- **purrTTY location:** `purrTTY.Display/Ghostty/ImageTextureCache.cs`,
  `purrTTY.Display/Rendering/PurrTTYFontManager.cs`, `ToggleHotkeyBinding.cs` (`KeySuper`
  probe), and `purrTTY.GameMod/Patches/ConsoleWindowPrintPatch.cs`.
- **Coupling kind:** render, reflection, Harmony.
- **Impact:** compatible.
- **Change needed:** none.

### Camera, ray picking and vehicle identity — Low; compatible

- **KSA delta:**
  - `Vehicle.cs` (1,034 diff lines) and `Part.cs` (376) change avionics, crew, missions,
    aerodynamics and module-list storage.
  - `Part.SubParts` (`KSA/Part.cs:1172`) is still a `ReadOnlySpan<Part>` that is empty when
    unset. Rev 5898's "compact form" applies to module lists, not sub-parts.
  - `GetMatrixAsmb2Ego` (`KSA/Vehicle.cs:1539`), `Part.PositionEgo`/`Asmb2Ego`
    (`KSA/Part.cs:1281`/`1286`) and `Universe.CurrentSystem.All` are unchanged.
  - `PartTree.Split`/`Merge` still route through `TransferPartSubtreeTo`
    (`KSA/PartTree.cs:1919`), which moves the same `Part` instances.
  - `Camera.cs` and `Ray.cs` changes are local renames. Reverse-Z projection, `ScreenToEgoRay`
    and the Möller–Trumbore epsilon/culling are unchanged. `Cursor.cs` and the viewport family
    are identical, and `Brutal.Numerics` is identical.
- **purrTTY location:** in GameMod's `InWorld/`: `VehicleLookup.cs`, `Display/InWorldQuad.cs`
  and `InWorldTerminalManager.cs`.
- **Coupling kind:** game-state, render/behavioral.
- **Impact:** compatible. Part-follow identity (gotcha 31) and ego-space placement hold.
- **Change needed:** none.

### Build references and lifecycle — Low; compatible

- **KSA delta:** all 20 managed `$(KSAFolder)` DLLs purrTTY references or copies for tests
  still exist. The optional native `glfw3.dll`, `imgui.dll` and `VulkanEx.dll` copies remain
  absent, as in 5801, and their conditional copies tolerate that. The changed
  `Brutal.Vulkan*` binaries (rev 5867, Vulkan abstractions v2026.11.1) add extension types and
  remove `VxDestroy*` helpers that purrTTY does not use. StarMap is outside these snapshots.
- **purrTTY location:** project files, `TerminalMod.cs` and `mod.toml`.
- **Coupling kind:** build-ref, lifecycle.
- **Impact:** compatible. StarMap.API 0.3.6, Harmony 2.4.2 and the exported assemblies are
  unchanged.
- **Change needed:** none.

## Validation and verdict

- Debug and Release solution builds against the target DLLs: **0 warnings, 0 errors**.
- Full NUnit suite in Debug and Release on macOS arm64: **382 passed, 2 platform skips,
  0 failed** in each configuration.
- Offline Harmony 2.4.2: **all six patch classes applied, to six distinct targets**, using the
  built mod and the target DLLs. Only a scratch copy of IL-only `KSA.dll` had its PE machine
  flag changed from x64 to arm64; the snapshot was untouched. The probe follows the method in
  [the 5801 review](ksa-5801-compatibility.md) and reads `Patcher`'s own patch lists.
- Release-policy checks: **10 passed**. Builds used `PURRTTY_DIST_DIR` to stay in the
  workspace's `dist/`.
- **Not exercised:** no live KSA GPU session ran. The new dynamic-rendering scope and its
  barriers were not checked under Vulkan validation layers. Not visually confirmed: in-world
  terminal appearance (transparency, text, Kitty images) and in-world resize. Menu placement on
  Linux, keyboard/mouse isolation during a part pick, console capture and StarMap load/unload
  were also not tested in a running game.

| Touchpoint | Severity | Change needed |
|---|---|---|
| Off-screen ImGui backend / pass | High | Dynamic rendering, colour-only target, explicit barriers; done |
| Menu popups | Low | Use `ImGuiHelper.BeginMenu`; done (optional parity) |
| Required input gates and fallback menu hook | Low; compatible | None |
| Translucency, render order, shader asset | Low; compatible | None |
| ImGui textures, fonts, reflection, console sink | Low; compatible | None |
| Camera, rays, registry, part identity | Low; compatible | None |
| Build references and lifecycle | Low; compatible | None |

**Overall verdict: behavioral work required (render path), implemented.** In-game smoke
testing, ideally with Vulkan validation layers on, is still needed to validate the in-world
off-screen pass at runtime.
