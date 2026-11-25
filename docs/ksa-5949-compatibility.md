# KSA 5949 compatibility review

Reviewed on **2026-11-25** using `.agents/skills/upgrade-ksa/SKILL.md`.

- Last validated baseline: **2026.11.16.5901**, assemblies commit `8d2ad13`
  (see [the 5901 review](ksa-5901-compatibility.md)).
- Supplied previous snapshot: **2026.11.16.5901**, the validated baseline. The target's
  `fromRevision` (5901) chains to it, so the changelog has no gap.
- Target: **2026.11.25.5949**, snapshot date **2026-11-25**, assemblies commit `618c2a1`.
- Reviewed the **5901 → 5949** source and asset diff for every coupling-catalog touchpoint.
  Seven binaries changed: `KSA`, `Brutal.Vulkan`, `Brutal.Vulkan.Abstractions`, `Planet.Core`,
  `Planet.Render.Core`, `BepuPhysics` and `BepuUtilities`. No DLL was added or removed.
  The Brutal ImGui, Logging and Glfw assemblies are byte-identical, so their decomp diffs (the
  `LogLevel` casts in `ConsoleWindow.cs`) are decompiler artifacts.
- Much of the render diff is rev 5904 ("Vulkan abstractions … GPU debug labels in non-debug
  builds"). `DebugNameExtensions.SetDebugName` was `[Conditional("DEBUG")]` and is now a
  runtime call guarded by `device.HasDebugNames()`
  (`Brutal.VulkanApi.Abstractions/DebugNameExtensions.cs:50`). Dozens of renderers gained
  naming-only hunks. None changes behavior that purrTTY relies on.

KSA paths below are relative to `current/decomp/` in the assemblies checkout at `618c2a1`;
asset paths are relative to `current/Content/`. purrTTY paths are repo-relative.

### Game self-restart orphans Unix shells — Medium; fixed

- **KSA delta:** rev 5916 adds switching star systems at runtime.
  - `SaveSystemSwap.OnFrame` (`KSA/SaveSystemSwap.cs:168`, called from `KSA/Program.cs:2669`)
    runs `Program.SwapSystem` (`KSA/Program.cs:1905`).
  - If the new system would not fit in video memory, it calls `SaveSystemRestart.RestartInto`
    (`SaveSystemSwap.cs:177`). That method does `Process.Start(<the game>)` and then
    `Environment.Exit(0)` (`KSA/SaveSystemRestart.cs:33`–`34`).
  - The game exits without its normal shutdown, so StarMap never calls `[StarMapUnload]`.
- **purrTTY location:** `purrTTY.Terminal/Pty/Process/UnixPtySpawner.cs` and
  `UnixPtyNative.cs`; `purrTTY.GameMod/TerminalMod.cs` (`Unload`).
- **Coupling kind:** lifecycle, behavioral.
- **Impact:** on Linux and macOS, every running shell was orphaned and kept running
  after the game restarted.
  - purrTTY stops shells explicitly in `Unload` (SIGHUP to the session, then SIGKILL). On any
    other exit, a shell ends only when its pty master closes and the kernel hangs up the pty.
  - `posix_openpt` returned a master without close-on-exec, so copies leaked into children:
    - each shell inherited its own master, and every earlier sibling's, through `posix_spawnp`;
    - the relaunched game inherited all of them through .NET's `Process.Start`.
  - With a copy still open, the hangup never came. This was verified on macOS arm64: a .NET
    `Process.Start` child sees the master fd. A shell whose master was closed without an
    explicit stop was still running after 10 s.
  - Windows is unaffected. The ConPTY pipes are created non-inheritable
    (`ConPtyPipeManager.cs`, `CreatePipe(…, IntPtr.Zero, 0)`).
  - The defect already existed for crashes and kills. KSA 5949 turns it into a normal gameplay
    path.
- **Change needed:** mechanical, implemented (gotcha 16).
  - `UnixPtySpawner.Spawn` sets `FD_CLOEXEC` on the master immediately after `posix_openpt`.
  - The new `UnixPtyNative.SetCloseOnExec` wraps `fcntl(F_SETFD, FD_CLOEXEC)` with the same
    Apple arm64 variadic padding as `WinSizeIoctl`.
  - A failure is logged and does not stop the spawn.
  - New test: `UnixProcessManagerTests.ClosingMaster_WithoutStop_HangsUpShell`. It fails at the
    10 s timeout without the fix and passes in about 30 ms with it.

### Input gates and fallback menu — Low; compatible

- **KSA delta:** none on the coupled members. After normalizing the source-path line numbers
  in log calls, `Program.cs` hunks touch only:
  - the constructor;
  - `Dispose`;
  - the new `SwapSystem`/`BuildSystemRenderers`/`DisposeSystemRenderers`;
  - `OnFrame` (swap/restart polling);
  - debug UI;
  - a per-frame `SunTransmittance` reset.

  These are unchanged:
  - `OnKey` (`KSA/Program.cs:2102`) and `DispatchKeyEvent(in GlfwKeyEvent)` (2113);
  - `OnMouseButton` (2369), `DrawMenuBar` (3876) and `DrawProgramMenusHook` (4479);
  - `GameSettings.OnKeyAll` (`KSA/GameSettings.cs:3789`).

  `PartPicking`, `Popup`, `Input`, `GlfwKeyEvent`, `IGameViewportLifecycle` and `ImGuiHelper`
  are source-identical. `GameSettings` only adds a shadow-cascade setting and a borderless-window
  fix.
- **purrTTY location:** `purrTTY.GameMod/Patcher.cs` (`Patch01`, `Patch02`,
  `Patch03_HotkeyGuard`, `Patch04_MouseBindingGate`).
- **Coupling kind:** Harmony, input/behavioral.
- **Impact:** compatible.
- **Change needed:** none.

### Translucency injection, render order and shader assets — Low; compatible

- **KSA delta:**
  - `SuperMeshRenderSystem.RenderTranslucencyPass` (`KSA/SuperMeshRenderSystem.cs:456`) is
    unchanged. It still opens a `BeginRendering` scope on `viewport.OffscreenTarget` with one
    colour attachment and depth, both `LoadOp.Load`.
  - Revs 5923/5937 split the pre-pass normal/roughness target. The pre-pass pipelines now have
    two colour attachments (`SuperMeshRenderSystem.cs:675`, static ctor at 1040). That stays in
    the pre-pass and does not reach the main offscreen target. `KSA.Rendering/RenderTarget`,
    `ImageBarrierInfo` and `CommandBufferEx` are source-identical. `RenderImage`, `Utils`,
    `Renderer`, `KSADeviceContextEx` and `SingleToMultisamplePass` gain debug labels only.
  - Main-viewport order is unchanged:
    ocean (`KSA/Program.cs:5375`) → atmosphere/clouds (5384) → plume trails and exhausts →
    transparency MSAA resolve (5402) → **translucency pass** (5410) → resolve (5413) →
    underwater (5417) → sun bloom (5424) → bloom (5425) → part outline (5426).
    `RenderTranslucencyPass` keeps its three `Program` callers.
  - Rev 5948 restores sun dimming by transparencies. KSA's cloud, ring, exhaust and
    static-object shaders multiply their transmittance into a 64×64 sun tile, a global-set
    storage buffer (`Shaders/Common/SunTransmittance.glsl`), which sun bloom reads afterwards.
    purrTTY's quad uses its own pipeline layout and does not take part. A terminal in front of
    the sun does not dim the sun, as in 5901, because the scene colour has had no alpha since
    5801.
  - `Core/DefaultAssets.xml:55` still registers `UnlitMeshVert`, and
    `Shaders/Mesh/UnlitMesh.vert` is unchanged. Its include `Common/Shared.glsl` only gains two
    pure helpers (`packSignedToUnsigned`/`unpackUnsignedToSigned`), with no bindings or push
    constants. `ShaderModuleUtils.FromString` and `UnlitMeshRenderTechnique` gain debug labels
    only.
- **purrTTY location:** in GameMod's `InWorld/`: `Patches/RenderTranslucencyPassPatch.cs` and
  `Display/SharedQuadResource.cs`.
- **Coupling kind:** Harmony, render/behavioral, asset-id.
- **Impact:** compatible. The postfix still runs after every pass that rederives colour from
  depth. Its scope, tracked barriers, attachment formats and MSAA are read dynamically, as
  before.
- **Change needed:** none.

### Off-screen ImGui backend, textures and console capture — Low; compatible

- **KSA delta:**
  - `KSA/ImGuiBackendVulkanImpl.cs` gains only debug labels and local renames.
    - `CreateInfo` (line 21) has the same required fields.
    - `AddTexture`/`RemoveTexture` (1123/1132), the `DescriptorPoolSize >= 256` assert (1299)
      and the 1000-descriptor main pool (`KSA/ImGuiBackend.cs:44`) are unchanged.
    - The new names come from the static UTF-8 `Name` (584) and internal strings, so a backend
      constructed by a mod cannot hit a null name.
  - `RenderCore/SimpleVkTexture.cs` adds names in previously empty lock scopes.
  - `ConsoleWindow.Print(ReadOnlySpan<char>, ImColor8, int)`
    (`Brutal.ImGuiApi.Abstractions/ConsoleWindow.cs:598`) is in a byte-identical DLL.
  - `FontManager` is source-identical.
- **purrTTY location:** in `purrTTY.GameMod/InWorld/`: `OffscreenImGuiBackend.cs` (passes
  `descriptorPoolSize: 256`), `PerFrameRenderer.cs` and `OffscreenRenderTarget.cs`.
  Also `purrTTY.Display/Ghostty/ImageTextureCache.cs`, `PurrTTYFontManager.cs`, the
  `ToggleHotkeyBinding.cs` `KeySuper` probe, `Patches/ConsoleWindowPrintPatch.cs` and
  `purrTTY.CustomShells/GameConsoleShell.cs`.
- **Coupling kind:** render, reflection, Harmony.
- **Impact:** compatible.
- **Change needed:** none.

### Vehicle identity, part transforms and system switching — Low; compatible

- **KSA delta:**
  - Rev 5939 batches symmetric detachments. `Part.Disconnect` (`KSA/Part.cs:2311`) opens a
    transfer-out batch (`KSA/PartTree.cs:1968`) and closes it in try/finally within the same
    call.
  - While the batch is open, `TransferPartSubtreeTo` (1933) defers removing transferred parts
    from the old tree. Rendering is now updated per part instead of with full rebuilds.
  - `PartTree.Split` (1890) still moves the same `Part` instances.
  - `Part.SubParts` (1172) and `Part.PositionEgo`/`Asmb2Ego` (1281/1286) are unchanged.
  - `Vehicle.GetMatrixAsmb2Ego` (`KSA/Vehicle.cs:1539`) and `Asmb2Ego` (595) are unchanged.
  - `Cursor`, `Ray`, the camera, the viewport family and `Brutal.Numerics` are source-identical.
  - Rev 5916 `SwapSystem` destroys every vehicle, waits for the device to go idle, rebuilds the
    system renderers and recreates `Universe.CurrentSystem`. It does this from `OnFrame`, before
    rendering.
- **purrTTY location:** in GameMod's `InWorld/`: `VehicleLookup.cs`, `Display/InWorldQuad.cs`
  and `InWorldTerminalManager.cs`.
- **Coupling kind:** game-state, render/behavioral.
- **Impact:** compatible. Part-follow identity (gotcha 31) and ego-space placement hold.
  - purrTTY reads parts only from frame callbacks, so it never sees a half-finished batch.
  - After a system switch, a tracked part that has disappeared is dropped and re-resolved from
    the vehicle and part ids. If nothing resolves, the quad is hidden. This is the same path as
    loading a save in the same system.
  - purrTTY's GPU resources are not system renderers, so a switch does not touch them.
- **Change needed:** none.

### Build references and lifecycle — Low; compatible

- **KSA delta:**
  - Every `$(KSAFolder)` DLL that purrTTY references or copies for tests still exists.
  - The changed `Brutal.Vulkan*` binaries are the rev 5904 debug-label preview, which purrTTY
    does not call.
  - StarMap is outside these snapshots. The new self-restart exit is covered above.
- **purrTTY location:** project files, `TerminalMod.cs` and `mod.toml`.
- **Coupling kind:** build-ref, lifecycle.
- **Impact:** compatible. StarMap.API 0.3.6, Harmony 2.4.2 and the exported assemblies are
  unchanged.
- **Change needed:** none.

## Validation and verdict

- The build resolved `KSAFolder` to the companion `ksa-game-assemblies/current/dll/`. The
  compiler's `/reference:` arguments confirm that the 5949 `KSA.dll` was used.
- The solution compiled cleanly against 5949 before any change. There were no compile-visible
  breaks.
- Debug and Release solution builds: **0 warnings, 0 errors**.
- Full NUnit suite in Debug and Release on macOS arm64: **383 passed, 2 platform skips,
  0 failed** in each configuration. The baseline was 382; the hangup regression test is new.
- Offline Harmony 2.4.2: **all six patch classes applied, to six distinct targets**, against
  both the Debug and Release mod builds.
  - The method is the one in [the 5801 review](ksa-5801-compatibility.md). The probe reads
    `Patcher`'s own patch lists.
  - Only a scratch copy of IL-only `KSA.dll` had its PE machine flag changed from x64 to arm64.
    The snapshot was untouched.
- Release-policy checks: **10 passed**. Builds used `PURRTTY_DIST_DIR` to stay in the
  workspace's `dist/`, which contains all three native platforms and no KSA, Brutal, Planet,
  Harmony or StarMap DLLs.
- **Not exercised:**
  - No live KSA GPU session ran.
  - No real system switch or VRAM restart ran. The orphan fix is verified at the PTY level,
    not through KSA's `RestartInto`.
  - Not visually confirmed: in-world terminal appearance, part-follow across a symmetric
    decouple, and in-world terminals across a system switch.
  - Input isolation and StarMap load/unload were not tested in a running game.

| Touchpoint | Severity | Change needed |
|---|---|---|
| Unix shell lifetime across KSA self-restart | Medium | Close-on-exec pty master; done |
| Required input gates and fallback menu hook | Low; compatible | None |
| Translucency, render order, shader asset | Low; compatible | None |
| Off-screen ImGui backend, textures, console sink | Low; compatible | None |
| Part identity, transforms, system switching | Low; compatible | None |
| Build references and lifecycle | Low; compatible | None |

**Overall verdict: one mechanical fix, now implemented; no API changes were required.** In a
running game, check two things: in-world terminals across a system switch, and that no shells
survive a VRAM restart on Linux/macOS.
