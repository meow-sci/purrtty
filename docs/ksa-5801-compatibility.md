# KSA 5801 compatibility review

Reviewed on **2026-11-08** using `.agents/skills/upgrade-ksa/SKILL.md`.

- Last validated baseline: **2026.10.10.5554**, assemblies commit `935a32b`.
- Supplied previous snapshot: **2026.10.25.5707**, commit `4b3e9e2`.
- Target: **2026.11.10.5801** (the supplied build identifier), snapshot date
  **2026-11-07**, assemblies commit `4de698e`. Checkout HEAD is `d07b870`, whose
  subsequent change only affects snapshot tooling.
- Reviewed the full **5554 → 5801** source/asset diff, including 5638 and 5707.
  The target's changelog only covers revisions 5708–5801; comparing only the supplied
  previous snapshot would miss unvalidated changes.

KSA paths below are relative to `current/decomp/` in the assemblies checkout at
`4de698e`; asset paths are relative to `current/Content/`. purrTTY paths are repo-relative.

### Brutal assembly references — Low

- **KSA delta:** revisions 5770/5775 update Brutal. `Brutal.Core.Common` becomes
  `Brutal.Core`; the Logging, Numerics, Collections, Memory and Strings assemblies drop
  `.Core` from their names. The old DLLs are absent.
- **purrTTY location:** KSA references in all affected project files; runtime copies in
  `purrTTY.CustomShellContract.Tests`, `purrTTY.CustomShells.Tests`, and `purrTTY.Display.Tests`.
- **Coupling kind:** build-ref.
- **Impact:** the solution cannot compile with the old reference names. The test projects'
  old `Brutal.Core.*.dll` glob also omits the renamed logging/numerics dependencies, causing
  runtime failures even after compilation succeeds.
- **Change needed:** mechanical, implemented: rename references and update test runtime
  copies to the new core assembly family, including Buffers. All **18 distinct managed
  game references** exist. Game references retain `Private=false`; test runtime copies
  do not enter the mod distribution. The optional `glfw3.dll`, `imgui.dll` and `VulkanEx.dll`
  copies are absent in this target; the existing conditional copies tolerate that.

### Kitty graphics texture handle — Medium

- **KSA delta:** `RenderCore/SimpleVkTexture.cs:79` renames `ImageView` to `VkImageView`
  (and `Image` to `VkImage`). Its constructor adds an optional image-create-flags argument.
- **purrTTY location:** `purrTTY.Display/Ghostty/ImageTextureCache.cs:UploadOrReplace`.
- **Coupling kind:** render/behavioral, direct API.
- **Impact:** Kitty graphics texture registration fails to compile. The replacement is
  the same raw Vulkan view; upload, texture ownership and disposal remain compatible.
- **Change needed:** mechanical, implemented: pass `texture.VkImageView` to
  `ImGuiBackend.Vulkan.AddTexture`. No descriptor, upload or eviction redesign.

### Quad native-resource ownership — Low

- **KSA delta:** `Brutal.VulkanApi.Abstractions/VertexInput.cs:333` now implements
  `IDisposable`, releasing `Bindings`, `Attributes` and its private `_createInfo` owner.
  The new Brutal collection family requires explicit disposal (revision 5770).
- **purrTTY location:** `purrTTY.GameMod/InWorld/Display/SharedQuadResource.cs:DestroyGpu`.
- **Coupling kind:** render/behavioral.
- **Impact:** the old code releases only the two lists and leaves the create-info allocation
  to its owner's finalizer instead of releasing the whole vertex-input object at teardown.
- **Change needed:** mechanical, implemented: call `_vertexInput?.Dispose()` once in place
  of disposing its lists individually. The stock vertex shader remains ModLibrary-owned;
  purrTTY still destroys only its own fragment module.

### Translucency, HDR and shader assets — Low; compatible

- **KSA delta:** HDR arrives in revision 5645, scene/UI brightness separate in 5714,
  and revisions 5779/5796 parameterize image formats. `KSA/Program.cs:227` changes scene
  colour from `R16G16B16A16SFloat` to `B10G11R11UfloatPack32`. Secondary viewports gain
  clouds in 5663. HDR scene composition and UI presentation now use separate targets
  (`KSA/Program.cs:4976`, `Core/Renderer.cs:701`).
- **purrTTY location:** `InWorld/Patches/RenderTranslucencyPassPatch.cs`,
  `InWorld/Display/SharedQuadResource.cs`, and `InWorld/OffscreenRenderTarget.cs` in GameMod.
- **Coupling kind:** Harmony, render/behavioral, asset-id.
- **Impact:** compatible. The quad pipeline already reads colour/depth formats and MSAA
  from `Program.OffscreenTarget`; its sampled terminal texture keeps its own RGBA8 alpha.
  A scene target without alpha still blends using fragment-source alpha. The patch target
  (`KSA/SuperMeshRenderSystem.cs:449`) retains its signature and closes its rendering scope.
  Atmosphere/cloud/ocean still precede the quad; resolve, underwater, bloom and final
  composition follow it (`KSA/Program.cs:5210`). Secondary clouds also run before the
  patched pass (`KSA/Program.cs:4888`). Tracked attachment read/write barriers and reverse-Z
  remain valid. `Core/DefaultAssets.xml:55` still registers `UnlitMeshVert`, whose vertex
  source, UV output and MVP push-constant interface are unchanged.
- **Change needed:** none beyond vertex-input disposal above. In-world terminals remain
  scene geometry and receive scene post-processing/brightness. Drawing with the main camera
  in secondary viewports remains an existing limitation, not newly validated functionality.

### Keyboard, mouse bindings and menus — Low; compatible

- **KSA delta:** `KSA/Program.cs:1986` adds `ToggleHabitatZones` to the release-action arm.
  `OnKey`, `GameSettings.OnKeyAll`, and the menu hook are unchanged; the mouse-binding route
  still enters the private `DispatchKeyEvent` from `OnMouseButton` (`KSA/Program.cs:2254`).
- **purrTTY location:** `purrTTY.GameMod/Patcher.cs`: `Patch01`, `Patch03_HotkeyGuard`,
  `Patch04_MouseBindingGate`, and fallback-menu `Patch02`.
- **Coupling kind:** Harmony, input/behavioral, asset-id.
- **Impact:** compatible. All target signatures/argument names bind. The held-key and
  swallowed-mouse-press models also cover the new release action. The short-circuit chain,
  console exemption, ModMenu lookup, and `IGameViewportLifecycle.SetMenuBarInUse` survive.
- **Change needed:** none. All four input/menu patches applied in the offline probe.

### ImGui backend, fonts, reflection and console capture — Low; compatible

- **KSA delta:** backend/font/console diffs largely change decompiler expression forms.
  `KSA/ImGuiBackendVulkanImpl.cs:647` retains the secondary-context userdata contract,
  its render-pass configuration, and assertions. `AddTexture`/`RemoveTexture` remain at
  lines 1112/1121, blending at 1399, and the main pool remains 1000 slots
  (`KSA/ImGuiBackend.cs:44`).
- **purrTTY location:** `OffscreenImGuiBackend.cs`, `OffscreenImGuiContext.cs`,
  `Patches/ConsoleWindowPrintPatch.cs`, Display's `PurrTTYFontManager.cs`, and
  `ToggleHotkeyBinding.cs`.
- **Coupling kind:** Harmony, reflection, render/behavioral.
- **Impact:** compatible. The shared font atlas, secondary backend's 256-descriptor pool,
  premultiplied output and `KeySuper` property remain available. String/character console
  output still reaches `ConsoleWindow.Print(ReadOnlySpan<char>, ImColor8, int)`
  (`Brutal.ImGuiApi.Abstractions/ConsoleWindow.cs:598`). No changed console-print call sites
  were found. Byte-span/ImString overloads still bypass that sink as before.
- **Change needed:** none beyond the Kitty texture property above. Console patch application
  passed; a live output-capture test remains part of the game smoke check.

### Camera, ray picking and vehicle identity — Low; compatible

- **KSA delta:** camera projection/unprojection uses logical `ScreenSize` instead of
  framebuffer size (`KSA/Camera.cs:419`, `:678`); main viewport supplies the window size
  (`KSA/GameViewport.cs:311`). Revision 5784 fixes scaled-window input. Part data loading,
  variants and vehicle systems also change extensively.
- **purrTTY location:** `InWorld/Display/InWorldQuad.cs`, `InWorld/VehicleLookup.cs`,
  and `InWorldTerminalManager.cs` in GameMod.
- **Coupling kind:** game-state, render/behavioral.
- **Impact:** compatible. purrTTY already delegates desktop-pointer conversion to
  `Cursor.GetEgoRay(Program.MainViewport)`, so it benefits from KSA's unprojection fix.
  `Vehicle.GetMatrixAsmb2Ego` (`KSA/Vehicle.cs:1486`), `Part.PositionEgo`/`Asmb2Ego`
  (`KSA/Part.cs:1263`), row-vector matrix order, and the live vehicle registry remain valid.
  `PartTree.Split`/`Merge`/`TransferPartSubtreeTo` (`KSA/PartTree.cs:1829`) still move the
  same Part objects, preserving anchor identity across decouple/dock. Numerics operations
  compile against the renamed assembly; ray-triangle epsilon behavior is equivalent.
- **Change needed:** none. Scaled-window clicks and part following need live visual checks.

### Lifecycle and inter-mod ABI — Low; compatible

- **KSA delta:** HDR and other render systems add owned teardown work; no required game
  renderer-access contract changes were found. StarMap itself is outside these snapshots.
- **purrTTY location:** `purrTTY.GameMod/TerminalMod.cs`, in-world disposal paths and `mod.toml`.
- **Coupling kind:** lifecycle.
- **Impact:** offline compatible. Renderer access and AfterGui use are unchanged; shutdown
  still avoids touching an already-destroyed device. The project retains StarMap.API 0.3.6,
  Harmony 2.4.2, and both exported assemblies (`CustomShellContract`, `Logging`).
- **Change needed:** none. Actual StarMap load/unload and cross-mod sharing were not run.

## Validation and verdict

- Debug and Release solution builds against target DLLs: **0 warnings, 0 errors**.
- Full NUnit suite in Debug and Release on macOS arm64: **382 passed, 2 platform skips, 0 failed** in each configuration.
- Offline Harmony 2.4.2: **six patches applied to six distinct targets** using the built
  mod and supplied DLLs. Only a scratch copy of IL-only `KSA.dll` had its PE machine flag
  changed from x64 to arm64 to permit loading on this host; snapshot files were untouched.
- Release-policy checks: **10 passed**. The Release distribution is generated under
  `dist/purrTTY/` using `PURRTTY_DIST_DIR`; all three native platforms are present and no
  KSA/Brutal/Planet/Harmony/StarMap DLLs are bundled.
- No live KSA GPU session was exercised. Patch binding and headless tests do not prove
  HDR appearance, scaled-window picking, in-world occlusion/transparency, Kitty textures,
  keyboard/mouse isolation, console capture or loader lifecycle in a running game.

| Touchpoint | Severity | Change needed |
|---|---|---|
| Brutal references and test runtime copies | Low | Rename assemblies and replace old copy globs; done |
| Kitty texture view | Medium | Use `VkImageView`; done |
| Quad resource ownership | Low | Dispose whole `VertexInput`; done |
| Translucency, HDR, MSAA, shader assets | Low; compatible | None |
| Required input gates and fallback menu | Low; compatible | None |
| ImGui, fonts, reflection, console sink | Low; compatible | None |
| Coordinates, rays, registry, part identity | Low; compatible | None |
| Lifecycle and exported ABI | Low; offline compatible | None |

**Overall verdict: mechanical fixes only, implemented.** In-game smoke testing remains
necessary before claiming live rendering/loader validation.
