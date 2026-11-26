---
name: upgrade-ksa
description: Validate purrTTY against an upstream Kitten Space Agency (KSA) game update. Use when a new KSA build lands and you must decide whether purrTTY needs code changes. Enumerates every coupling point between purrTTY and KSA/Brutal/Planet/StarMap game code (Harmony patch targets, the render/Vulkan pipeline, ImGui backend, game-state APIs, the input chain, menu injection, lifecycle, build refs, behavioral assumptions, and the kFS flight-computer extension with its language server and console command), and drives a diff-of-KSA → impact-on-purrTTY review. Requires the CURRENT (new) and PREVIOUS KSA decompiled sources to be supplied — never hard-codes machine paths.
---

# Upgrade / Re-validate purrTTY against a KSA game change

purrTTY is a terminal-emulator **mod** for KSA. It compiles against KSA's reference
assemblies, **patches** several game methods with Harmony, injects a draw into KSA's
render pipeline, drives a second ImGui context on a Brutal Vulkan backend, and reads
live game state (vehicles/parts/camera). It also extends **kFS**, the game's built-in
programmable flight computer: it adds rows to kFS's tables, hooks its host, registers a
console command and runs kFS's own compiler from a language server (section 9 — the
largest and newest coupling surface, and one that **has never run in the game**). When
the upstream game changes, any of that can break — sometimes at compile time, but the
dangerous cases break **silently at runtime**. This skill is the checklist and method for deciding what, if anything,
purrTTY must change.

Delegate to the **`ksa`** skill for how KSA game types/APIs/decomp work in general;
this skill is specifically about purrTTY's *coupling surface* to those APIs.

## Required inputs — you MUST have both KSA snapshots

This skill compares two decompiled KSA source trees. **Do not proceed without them**;
ask the user to supply the paths. Never hard-code paths — treat these as variables:

- **`<current-ksa>`** — the **new** KSA build you are upgrading *to* (the one that changed).
- **`<previous-ksa>`** — the KSA build purrTTY currently builds and runs against (the
  last known-good baseline).

Each snapshot is a KSA-game-assemblies checkout laid out as:

```
<snapshot>/
  decomp/        # decompiled C# sources, one folder/.csproj per assembly (KSA, Brutal.*, Planet.*)
  dll/           # reference assemblies purrTTY compiles against ($(KSAFolder) points here)
  version.json   # { build, date, fromRevision, toRevision, commits[] } — human-readable changelog
```

If the user gives you only one tree, or only DLLs, say what's missing and what you can
still check (a compile against the new DLLs) versus what you can't (behavioral/runtime
coupling — see below).

`version.json.commits[]` is the fastest first read: the changelog lines often name the
subsystem that moved (rendering, input, vehicles, console), telling you where to focus.
**But it can have gaps**: `fromRevision` is whatever the snapshot tool last saw, not
necessarily `<previous-ksa>`'s `toRevision` (the 5402 snapshot listed only rev 5401 while the
previous snapshot ended at 5348). If the two don't chain, the decomp diff is the only complete
record — never conclude "nothing moved" from a short `commits[]`.

The baseline is purrTTY's **last validated** build, not whatever the `<previous-ksa>` folder happens
to hold: if an intermediate build was never reviewed (the 5482 review found `<previous-ksa>` at 5438
while purrTTY was last validated on 5402), diff from the last validated build instead. Snapshot trees
that are git repos (one commit per build) make this easy:
`git diff <validated-sha> <current-sha> -- current/decomp/<path>`.

## The core insight — compile ≠ safe

purrTTY references some KSA members **directly** (the compiler catches those if they
change) but couples to others in ways the compiler cannot see. Classify every finding by
how it fails:

| Coupling kind | Breaks at | Detected by |
|---|---|---|
| Direct call to a referenced member (signature change) | **compile** | `dotnet build` against `<current-ksa>/dll` |
| **Harmony patch target** (rename / removed / signature drift) | **runtime, load** | patch silently fails to apply — logged, not thrown (`Patcher` swallows per-patch) |
| **Reflection** (string member names) | **runtime, silent** | member resolves to null; behavior degrades with no error |
| **Behavioral / semantic** (render draw order, premultiplied alpha, input-chain order, coordinate-frame convention, the print funnel) | **runtime, silent wrong output** | nothing — only diffing KSA source + reasoning catches it |
| **Asset id** (`ModLibrary.Get<>("UnlitMeshVert")`, `ModLibrary.Find("ModMenu")`) | **runtime** | throws / feature self-skips |
| **Build ref** (a `$(KSAFolder)` DLL renamed/removed/split) | **compile / copy** | `dotnet build` (missing `<Reference>`) |

**The whole point of this skill** is the middle rows: the Harmony, reflection, and
behavioral coupling that a green build will *not* catch. Budget most of your effort
there.

## Workflow

1. **Read `version.json` in both snapshots** — confirm you have the right two builds and
   skim `commits[]` for subsystems that moved.
   Then list which **binaries** actually changed (`git diff --stat <prev-sha> <cur-sha> -- current/dll`
   in a git-backed snapshot, or `cmp` per DLL). A decomp diff inside an assembly whose DLL is
   byte-identical is a decompiler/host artifact, not a game change (5901: `ConsoleWindow.cs` showed
   `LogLevel` cast noise because the snapshot host's .NET moved to 11.0, while
   `Brutal.ImGui.Abstractions.dll` was unchanged). This also confirms no referenced DLL was added or removed (§8).
   Naming-only hunks can also flood a diff. In 5949, rev 5904 changed Brutal's `SetDebugName` from
   `[Conditional("DEBUG")]` to a runtime call guarded by `HasDebugNames()`, so dozens of renderers
   gained `SetDebugName` calls. Skim past those hunks and diff the members you depend on.
2. **Build purrTTY against `<current-ksa>/dll`** to sweep up all compile-visible breaks
   first (see [Build against the new KSA](#build-against-the-new-ksa)). Fix/triage those.
3. **Walk the coupling catalog below.** For each touchpoint, open the named KSA type in
   **both** `decomp/` trees and diff the specific member(s) purrTTY depends on. A quick
   way to diff one type across snapshots:
   ```bash
   diff <previous-ksa>/decomp/<Asm>/<Type>.cs <current-ksa>/decomp/<Asm>/<Type>.cs
   ```
   (Assembly folder names match the DLL names, e.g. `KSA`, `Brutal.ImGuiApi`,
   `Brutal.VulkanApi`, `Planet.Render.Core`.)
4. **For each change, map it to the purrTTY file(s)** listed for that touchpoint and
   decide: no-op / mechanical fix / behavioral redesign. Cross-reference
   `docs/gotchas.md` — many touchpoints carry a numbered gotcha explaining the intent.
5. **Report** using the [output format](#output-format): per touchpoint, the KSA delta,
   the impact, the severity, and the concrete purrTTY change (or "no change needed").

Do **not** silently edit purrTTY unless asked — the deliverable is a *review*. If asked
to also fix, do the compile-visible fixes first, then propose the behavioral ones.

---

## Coupling catalog

Every place purrTTY reaches into game code, grouped by kind. Locations are purrTTY repo
paths (stable within the repo); the KSA members are what to diff across the two snapshots.
Line numbers are hints as of writing — navigate by symbol.

### 1. Harmony patches — the most brittle surface

The patches in this table are in `purrTTY.GameMod` and are applied by `Patcher.ApplyPatch`.
The kFS extension applies five more targets through its own installer (`purrTTY.Kfs`,
`KfsExtension.Install`); they are in [section 9](#9-kfs-extension-language-server-and-console-command).
A drifted target does **not** crash the mod (`Patcher.ApplyPatch`
wraps each in try/catch and logs), so failures are invisible unless you check the log or
reason about it here. Verify **type exists · method exists · exact parameter types ·
semantics of the arm you depend on**.

| Patch (file) | KSA target & signature | Depends on | If KSA changes… |
|---|---|---|---|
| `Patch01` (`Patcher.cs`) — **required**, input gate | `KSA.Program.OnKey(GlfwWindow, GlfwKey, int, GlfwKeyAction, GlfwModifier)` prefix | `OnKey` being the single keyboard entry: since KSA 5482 it runs the gate chain and then calls the private `DispatchKeyEvent`, which holds the `Press`/`Repeat`/`Release` **switch arms**; that release-arm toggles (`ToggleFps`/`ToggleUi`/…) fire on `Release` | Signature drift → gate stops applying → terminal typing leaks into game controls. Semantics change (which actions fire on which arm) → the held-key model in `s_gameHeldKeys` may leak or strand keys. A new caller of `DispatchKeyEvent` (or another action dispatcher) that bypasses `OnKey` → that input leaks past the gate (this is how mouse bindings arrived — see `Patch04`). Re-read `Program.OnKey` **and** `DispatchKeyEvent` in full. Since KSA 5901 the `OnKey` gate chain also runs `PartPicking.OnKey` (consumes only `Cancel`/Esc press **and** its release while a part pick is active) before `WantCaptureKeyboard`. The held-key model keeps its swallowed-release flag consistent: a press the gate ate never reaches it, and a press it saw always gets its release forwarded. |
| `Patch02` (`Patcher.cs`) — optional, menu fallback | `KSA.Program.DrawProgramMenusHook()` postfix | It being an empty public method KSA calls inside its `BeginMenuBar()` right after the View menu; also `Program.MainViewport` (an `IGameViewport` since KSA 5402) whose `MenuBarInUse` is **read-only** — it is set via the public `IGameViewportLifecycle.SetMenuBarInUse(bool)` (the same call KSA's `DrawMenuBar` makes) | Hook removed/renamed → fallback menu disappears (only matters when ModMenu absent). `IGameViewportLifecycle`/`SetMenuBarInUse` gone → compile break; semantics drift → menu auto-hides / game hotkeys leak while open. |
| `Patch03_HotkeyGuard` (`Patcher.cs`) — **required**, typing guard | `GameSettings.OnKeyAll` prefix (`ref bool __result`) | `Program.ConsoleWindow` static + `ConsoleWindow.IsOpen`; `ImGui.GetIO().WantTextInput` | `OnKeyAll` signature/return-model change → typing in mod text fields fires game hotkeys. `ConsoleWindow`/`IsOpen` rename → NRE guard or console-exemption logic wrong. |
| `Patch04_MouseBindingGate` (`Patcher.cs`) — **required**, mouse-binding gate | private `KSA.Program.DispatchKeyEvent(in GlfwKeyEvent keyEvent)` prefix (`ref bool __result`; the patch takes `GlfwKeyEvent keyEvent` **by value** — Harmony dereferences the `in` arg) | `Program.OnMouseButton` dispatching mouse-bound actions via `Input.MouseBound(button) && DispatchKeyEvent(new GlfwKeyEvent(window, action, button, mods))` (KSA 5482, rev 5449; since 5901 `PartPicking.OnMouseButton` — left/right **release** while picking — short-circuits just before it, which is ordinary click handling, not a game action); `GlfwKeyEvent.{IsMouse,Button,Action}` (mouse `Press`/`Release` only); returning `false` = "not handled" so `OnMouseButton` falls through to ordinary click/camera handling; the swallowed-press model `s_swallowedButtons` | Private target — rename/signature drift → patch fails (logged) → clicking a player-bound mouse button while a terminal owns the keyboard fires game actions. A new mouse-dispatch path that skips `DispatchKeyEvent`, or `GlfwKeyEvent` losing `IsMouse`, → same leak. |
| `ConsoleWindowPrintPatch` (`Patches/ConsoleWindowPrintPatch.cs`) — optional, game-console capture | `ConsoleWindow.Print(ReadOnlySpan<char>, ImColor8, int)` postfix | **The single-sink funnel**: all string/char console output routing through this exact overload | **Brutal-version-sensitive.** Older API was `Print(string, uint, int, ConsoleLineType)`. If a new build prints via `u8`/byte-span/`ImString` overloads that call `AddPendingMessage` directly, capture silently escapes. Re-verify every `ConsoleWindow.Print*` overload and which call sites use which. |
| `RenderTranslucencyPassPatch` (`InWorld/Patches/RenderTranslucencyPassPatch.cs`) — optional, in-world quad inject | `SuperMeshRenderSystem.RenderTranslucencyPass(IViewport viewport, CommandBuffer commandBuffer, bool useCustomRenderPass)` postfix (params reordered in KSA 5482 — harmless because the postfix binds **by name** and the attribute names no arg types; `IViewport` replaced the deleted `Viewport` class in KSA 5402; `GameViewport`/`PartThumbnailViewport` implement it) | `IViewport.OffscreenTarget` as `KSA.Rendering.RenderTarget`; `RenderTarget.{ColorAttachment,DepthAttachment}` (resolve to the MSAA images when multisampled); `IViewport.Size` (`int2`); `ImageBarrierInfo.Presets.{ColorAttachmentReadWrite,DepthStencilAttachmentReadWrite}` (tracked `RenderImage` state — the declared state is the next barrier's source, so it must include the quad's writes; gotcha 32); `CommandBuffer.{PipelineBarrier2,BeginRendering,EndRendering}` (dynamic rendering, `LoadOp.Load`); the **draw-order** guarantee (runs after atmosphere/cloud/ocean; since KSA 5482 rev 5408 the main pass runs MSAA resolve → underwater → sun/global bloom → part-select outline **after** it, so the quad is bloomed — accepted) and the fact that the method **closes its own** `BeginRendering`/`EndRendering` scope before returning; and that the postfix fires for **every** visible viewport KSA renders through `RenderViewport` (secondary kitten cams / crew portraits, whose owned targets share the main target's formats + `GameSettings.GetSampleCount()`) — purrTTY draws with the main camera regardless (`ViewportEx.IsMain()` is the filter if that ever needs gating) | Highest-risk render coupling. Signature drift → in-world terminals vanish. **Draw-order change** (KSA moves atmosphere/ocean after this pass, or restructures `RenderTarget`) → planet-silhouette cutout returns (gotcha 32); a new post-process moved after it changes how the quad looks and consumes its writes through the tracked barrier state. Diff `SuperMeshRenderSystem`, `PlanetTransparenciesRenderer`, the ocean renderer, `PartModelGlass.WriteCommandsColor`, and `KSA.Rendering/RenderTarget`. |
| `KfsErrorEchoPatch` (`Kfs/KfsErrorEchoPatch.cs`) — optional, keeps `kfs` errors single in the console | `KSA.Kfs.KfsIntegration.Error(string line)` (public static void) prefix + finalizer | That `Error` logs the line (which the game console shows, because the console window is a log sink) **and** writes it to `KfsIntegration.Output` whenever `Output` is not the game's own `ConsoleWindowWriter`; that `Error` is called on the main thread; that the JIT does not inline it into `KfsConsoleCommands` | Target drift, or `Error` inlined → every `kfs` error appears twice in the console while the language server's output tee is installed (cosmetic). If `Error` stops writing to `Output`, the patch is dead code and editors stop receiving error lines as command output. Details in §9. |

After the diff, if in doubt, confirm the patch still applies by checking the mod log for
`REQUIRED/optional Harmony patch '…' failed to apply` at runtime.

**Offline patch-apply check (no game needed).** A throwaway console app (same `Lib.Harmony`
version as `purrTTY.GameMod.csproj`) can load the built `purrTTY.GameMod.dll` plus the
`<current-ksa>/dll` assemblies and run `harmony.CreateClassProcessor(type).Patch()` on every patch
class exactly as `Patcher` does — this catches target/param-name drift that the compiler can't. It
can also invoke a patched private method on a `RuntimeHelpers.GetUninitializedObject` instance to
exercise a prefix that skips the original (a pass-through faults in the uninitialized original,
which is itself the signal). Gotchas: register the `AssemblyLoadContext.Default.Resolving` probe
(mod bin dir + KSA dll dir) **before** any method that mentions KSA types is JIT-compiled (keep the
body in a separate `NoInlining` method); `Patch()` returns the generated *replacement* methods
(`DeclaringType == null`) — use `Harmony.GetPatchInfo`/`GetPatchedMethods` to report targets; and
`KSA.dll` is **IL-only but flagged x64** (PE32+, no ReadyToRun), so on an arm64 host copy the dll
folder to scratch and set the copy's COFF `Machine` to `0xAA64` (never touch the snapshot itself).

### 2. Render / GPU pipeline (Brutal Vulkan + KSA render objects)

The in-world terminal feature (`purrTTY.GameMod/InWorld/`) is deeply coupled to KSA's
renderer and offscreen pass. Compile catches type/member renames; it does **not** catch
semantic changes to formats, sample counts, layouts, or draw order.

- **`SharedQuadResource.cs`** (`InWorld/Display/`): builds the quad pipelines. Diff:
  - `ModLibrary.Get<ShaderReference>("UnlitMeshVert")` — the stock unlit-mesh **vertex**
    shader asset. Asset id rename → throws (feature self-skips). Its **shader interface**
    (UV out at location 0, a single `mat4` MVP vertex push constant) is assumed by the
    custom frag `QuadFragGlsl` and the pipeline layout — if KSA changes UnlitMesh's vertex
    layout or push-constant, the quad renders garbage. Check `UnlitMesh.vert` in decomp.
  - `Program.OffscreenTarget` (a `KSA.Rendering.RenderTarget`, same object as
    `Program.MainViewport.OffscreenTarget`): `.Samples`, `.ColorAttachment.Format`,
    `.DepthAttachment.Format` — pipeline MSAA + attachment formats fed into a
    `VkPipelineRenderingCreateInfo` (dynamic rendering; there is **no** `VkRenderPass` — KSA rev
    5154 deleted `Program.OffScreenPass`/`Core.RenderPassState`; `Program.MainPass` is now a
    `Core.SwapchainPassState`). Format/MSAA change → validation error or silent depth misbehavior.
    KSA 5801 uses `B10G11R11UfloatPack32` for scene colour; purrTTY keeps its own sampled
    terminal texture RGBA8 for alpha and reads the scene attachment format dynamically.
    HDR scene/UI brightness is applied later by KSA.
  - `RenderCore.ShaderModuleUtils.FromString(...)` — runtime shaderc compile (needs
    `Brutal.ShaderC`).
  - Presets: `RenderingPresets.ReverseZDepthStencil.{DepthTestNoWrite,NoDepthTest}`,
    `Presets.Rasterization.Fill.CullNone`, `Presets.InputAssembly.TriangleList`,
    `Presets.Entrypoint.Main`. **Reverse-Z** is assumed everywhere — if KSA leaves
    reverse-Z, depth compare flips.
  - `Renderer` members: `.Device`, `.Graphics`, `.Allocator`, `.DynamicStateInfo`,
    `.ViewportState`; `VertexInput`, `BufferEx`, `DescriptorSetLayoutEx`, `VkUtils.StageAndUploadToBuffer`.
    Since KSA 5801, `VertexInput` implements `IDisposable`: dispose the whole object, which
    owns both native lists **and** a private create-info allocation (gotcha 38). Brutal's new
    collections require explicit disposal; check all native owners on future bumps.
  - **Premultiplied-alpha assumption** (gotcha 28): `QuadFragGlsl` un-premultiplies →
    gamma-decodes (`pow(2.2)`) → re-premultiplies, because KSA's ImGui backend blends over
    a transparent clear (premultiplied output) and the stock frag would force alpha=1.
    If KSA's ImGui compositing or gamma handling changes, the quad's color/opacity is wrong.
- **`OffscreenRenderTarget.cs`** (`InWorld/`): purrTTY's **own** sampleable **colour-only** target (KSA rev 5154 deleted the old `KSA.RenderTarget`/`Framebuffer` it used to wrap; since KSA 5901 it has no render pass, framebuffer or depth attachment — gotcha 39). It reproduces the colour usage flags of the deleted `RenderTarget.BuildAttachments` directly on `ImageEx` and uses `Presets.Sampler.SamplerLinearClamped`. **`PerFrameRenderer.cs`** renders into it with a colour-only `BeginRendering` scope between two `CommandBuffer.TransitionImages2` calls (`KSA.CommandBufferEx`, `KSA.Rendering.ImageTransition`/`ImageBarrierInfo.Presets.{ColorAttachmentReadWrite,ColorAttachmentWrite,SampledReadF}`). If those helpers or presets change shape, or if the quad samples outside the fragment stage, revisit the barrier stages.
- **`InWorldQuad.cs`** (`InWorld/Display/`): `Program.GetMainCamera()`, `Camera.MVP.{projection,viewProjection}`, `Camera.VPInv.view`, `Program.SetViewport(cmd)`. Camera matrix conventions (row-vector `mvp = model * viewProjection`) are load-bearing — a handedness/convention change silently mislocates/mirrors the quad.
- **`PerFrameRenderer.cs`** / **`InWorldTerminalInstance.cs`**: `Program.GetRenderer()` (null before renderer live), device queue submit, `Device.WaitIdle()`.
- **`InWorldTerminalManager.cs`**: `Program.GetRenderer()?.Device.WaitIdle()`; `GameSettings.GetSampleCount()`.
- **Sun dimming and MRT (KSA 5949):**
  - Rev 5948 lets KSA's transparencies dim the sun through a 64×64 sun-transmittance buffer in
    the global set (`Shaders/Common/SunTransmittance.glsl`). Sun bloom reads it after the
    translucency pass. purrTTY's quad uses its own layout and does not write it, so a terminal
    in front of the sun does not dim it. That has been true since 5801 dropped scene alpha.
  - Revs 5923/5937 gave only the **pre-pass** two colour attachments. The translucency scope is
    still one colour attachment plus depth.
  - Rev 5915 added `KSA.Rendering.MultiRenderTarget` "for forthcoming MRT use by pre-passes".
    If the main offscreen target or `RenderTranslucencyPass` ever becomes MRT, the quad
    pipeline's single colour format will no longer match the scope.

Whole-feature check: diff `SuperMeshRenderSystem`, `OffscreenTarget`, `RenderTarget`,
`Renderer`, `Camera`, `RenderingPresets`, `Presets`, `ShaderModuleUtils`, and the
`Brutal.VulkanApi` structs used (`VkRenderingInfo`, `VkRenderingAttachmentInfo`,
`VkPipelineRenderingCreateInfo`, `ImageTransition`, `ImageBarrierInfo`) between snapshots.

### 3. ImGui backend (Brutal.ImGui + its Vulkan impl)

- **Secondary context / offscreen backend** (`InWorld/OffscreenImGuiBackend.cs`, `OffscreenImGuiContext.cs`): `ImGuiBackendVulkanImpl` with `.CreateInfo { Device, GraphicsQueue, ColorFormat (required), MinImageCount, ImageCount, SampleCount, DescriptorPoolSize }`, `.RenderDrawData(drawData, cmd)`, `.Dispose()`. Since KSA 5901 (rev 5816) `RenderPass`/`SubPass` are gone. The pipeline is built **once in the ctor** for dynamic rendering with `ColorFormat` and **no depth format**, so `RenderDrawData` must run inside a colour-only `BeginRendering` scope of that format. A format change, a new depth or stencil format, or a pipeline built lazily per render pass would each break the off-screen pass. Asserts `MinImageCount>=2`, `ImageCount>=MinImageCount`, `DescriptorPoolSize>0`. The context install/detach reads `ImGui.GetIO().BackendRendererUserData` and `ImGui.GetMainViewport().RendererUserData`. A backend-API change (ctor fields, the single-backend assertion, the userdata slots) breaks in-world rendering.
- **Kitty-graphics texture registration** (`purrTTY.Display/Ghostty/ImageTextureCache.cs`): `ImGuiBackend.Vulkan.AddTexture(sampler, imageView)` → `ImTextureRef`, `.RemoveTexture(texRef)`; `SimpleVkTexture.VkImageView` (renamed from `ImageView` in KSA 5801), `SimpleVkTexture(...)` ctor; `Renderer.LinearSampler`; shared **1000-slot descriptor pool** assumption (LRU eviction). If `AddTexture`/`ImTextureRef` semantics or the pool budget change, kitty images fail to display or exhaust descriptors. (`InWorldTerminalRenderer.cs` notes `ImTextureRef` is the raw `VkDescriptorSet`.)
- **Font atlas** (`purrTTY.Display/Rendering/PurrTTYFontManager.cs`): `ImGui.GetIO().Fonts` → `ImFontAtlasPtr.AddFontFromFileTTF(ImString, 32.0f)`; `FontManager.Fonts[name]`. Atlas-API or KSA `FontManager` change → fonts don't load.
- **IO reads** across `TerminalWindow*.cs`: `ImGui.GetIO().WantTextInput`, character queue, key-state queries, `ImGui.GetMainViewport()`. Mostly stock ImGui (stable), but Brutal's binding versions the exact ptr shapes.
- **Reflection into ImGui IO** (`ToggleHotkeyBinding.cs:303`): `typeof(ImGuiIOPtr).GetProperty("KeySuper", …)` — a possibly-absent property probed by name. If Brutal renames/removes `KeySuper`, the Super-modifier read silently returns null (no error). This is the only reflection touchpoint of the **terminal**, and it targets ImGui, not KSA game types. The kFS code reflects into KSA game types in four places — see §9.

Diff `Brutal.ImGuiApi` / `Brutal.ImGui` (esp. `ImGuiBackendVulkanImpl`, `ImGuiIOPtr`,
`ImFontAtlasPtr`) and KSA's `ImGuiBackend` / `FontManager`.

### 4. Game-state / world API

Read-only-ish reads of live game state; direct references, so signature changes are
compile-caught, but **semantic** changes (what "ego space" means, identity vs rebuild on
decouple) are not.

- **`InWorld/VehicleLookup.cs`**: `Universe.CurrentSystem?.All.UnsafeAsList().OfType<Vehicle>()`; `Program.ControlledVehicle`; `Vehicle.Id`; `Vehicle.Parts.Parts`; `Part.SubParts`. Relies on KSA **moving the same `Part` instance** into a new vehicle on decouple/dock (identity follow — gotcha 31). If KSA starts rebuilding parts on reparent, follow-tracking breaks.
  - **Batched detach (KSA 5949, rev 5939):** `Part.Disconnect` opens a transfer-out batch and closes
    it in try/finally within the same call. Until it closes, a transferred part can remain in the
    old tree's `_parts`. `FindContaining` returns the first identity match, so it is safe only
    because purrTTY never reads in the middle of that call. If KSA ever holds a batch open across
    frames, it can return the old vehicle.
  - **System switching (KSA 5949, rev 5916):** `Program.SwapSystem`, run from `OnFrame`, destroys
    every vehicle, waits for the device to go idle, rebuilds the system renderers and recreates
    `Universe.CurrentSystem`. Anchors re-resolve the same way as after a same-system save load.
- **`InWorld/Display/InWorldQuad.cs`**: `Vehicle.GetMatrixAsmb2Ego(camera)`, `Vehicle.Asmb2Ego`, `Part.PositionEgo(in double4x4)`, `Part.Asmb2Ego(doubleQuat)`, `Part.Id`, `Part.SubParts`. **Ego-space** transform conventions and the choice to exclude the part's own scale (not baking `MatrixAsmb2Ego`) are semantic assumptions — diff these methods in `Part`/`Vehicle`.
- **Ray picking**: `Cursor.GetEgoRay(IViewport)` (a `Ray`; purrTTY passes `Program.MainViewport` because quads are placed with the main camera — KSA 5402 deleted the once-per-frame `Cursor.InputRay`, the ray is now `viewport.GetCamera().ScreenToEgoRay(desktopPos - viewport.Position)` on demand), `Ray.RaycastMollerTrumbore(v0,v1,v2,out t)`. Used for click-to-focus and app-mouse mapping in `InWorldTerminalManager.cs` and `InWorldQuad.TryRaycast`. Ego-space assumption must match the transform above. KSA 5801 camera unprojection uses logical `Camera.ScreenSize` instead of framebuffer pixels (scaled-window input); keep delegating to `Cursor.GetEgoRay` rather than applying a second DPI correction.
- **Numerics** (`Brutal.Numerics`): `double3`, `float3`, `float2`, `float4x4`, `double4x4`, `doubleQuat`, `floatQuat` and their ops (`float3.Transform`, `float4x4.CreateFromQuaternion/CreateTranslation/CreateScale/CreateRotation*`, `floatQuat.Pack`, `double3.Dot`). Row-vector multiply order is assumed throughout.

Diff `Universe`, `Vehicle`, `Part`, `Program` (ControlledVehicle/GetMainCamera/GetRenderer),
`Cursor`, `Ray`, and `Brutal.Numerics`.

### 5. Input chain

Beyond the Harmony patches (§1), purrTTY assumes KSA's **key short-circuit order**:
`GameSettings.OnKeyAll → Popup.OnKeyAll → ConsoleWindow.OnKey → !ConsoleWindow.IsOpen → PartPicking.OnKey (5901) → !WantCaptureKeyboard → DispatchKeyEvent`, all inside `Program.OnKey`. If a
handler returns `true`, downstream is skipped. Since KSA 5482 `Program.OnKey` runs that
chain and then calls the private `DispatchKeyEvent`, and **mouse** input has a second way in:
`Program.OnMouseButton` → `Popup.OnMouseAll` → ImGui `WantCaptureMouse` → `PartPicking.OnMouseButton` (5901) → (if
`Input.MouseBound(button)`) `DispatchKeyEvent` — bypassing `OnKeyAll`/`OnKey` entirely, which is
why `Patch04` gates `DispatchKeyEvent` itself. `Patch03` (`OnKeyAll`) and `Patch01`
(`Program.OnKey`) sit at two ends of this chain; a reorder or a new early handler can
change whether the gate/guard actually intercepts. Diff `Program.cs`'s key dispatch and
`GameSettings.OnKeyAll`. GLFW enums (`GlfwKey/GlfwKeyAction/GlfwModifier/GlfwWindow` from
`Brutal.GlfwApi`) are used in `Patch01`; `GlfwMouseButton` + `RenderCore.Input.GlfwKeyEvent` in `Patch04`.

### 6. Menu injection

`Program.DrawProgramMenusHook()` (§1 Patch02) and `((IGameViewportLifecycle)Program.MainViewport).SetMenuBarInUse(true)`.
purrTTY opens its top-level fallback menu and every submenu (`TerminalMenus.cs`) through
`KSA.ImGuiHelper.BeginMenu` (KSA 5901, rev 5808). It pins the popup to the window's viewport,
which fixes a Linux windowed-mode hover offset, and caps its height so long menus scroll, matching
KSA's own menus. If that helper changes or is removed, fall back to `ImGui.BeginMenu`.
The `[ModMenuEntry("purrTTY")]` path (`TerminalMod.DrawMenu`) is the *preferred* route
when the ModMenu companion mod is present, probed via `ModLibrary.Find("ModMenu")`
(`Patcher.cs`). ModMenu is a separate mod (NuGet `ModMenu.Attributes`), not KSA — but the
probe and the fallback both depend on KSA's `ModLibrary.Find` and the hook. If the game
adds a real mod-menu API, prefer migrating to it.

### 7. Lifecycle & ALC (StarMap)

`purrTTY.GameMod/TerminalMod.cs` uses the StarMap attribute set: `[StarMapMod]`,
`[StarMapImmediateLoad]`, `[StarMapAllModsLoaded]`, `[StarMapAfterGui]`, `[StarMapUnload]`
(StarMap.API NuGet, currently 0.3.6). These are the **complete** StarMap interface. Also:
- **Ordering assumptions**: renderer is live only from `AllModsLoaded` onward
  (`Program.GetRenderer()` null before); ImGui frame active in `AfterGui`.
- **kFS ordering assumptions** (details in §9): at `AllModsLoaded` the game console exists
  (`Program.ConsoleWindow` / `Program.TerminalInterface`), so the `purr` command can be
  registered, and nothing has read `KfsIntegration.Tables` yet, so the augmented tables can be
  published; `AfterGui` runs on the main thread in the phase where the stock console runs
  commands, so the language server may run `kfs` verbs and read the workbench there; `Unload`
  runs on the main thread.
- **ALC assembly sharing** (`mod.toml`): `[StarMap] ExportedAssemblies =
  ["purrTTY.CustomShellContract", "purrTTY.Logging"]` — the published inter-mod ABI that
  lets gatOS share one `CustomShellRegistry.Instance`. If StarMap changes attribute names,
  the lifecycle contract, or the ALC import/export resolution, revisit `TerminalMod` +
  `mod.toml`. (StarMap is the loader, not KSA game code, but a StarMap bump ships
  alongside KSA builds often enough to check here.)
- **Exits that skip `[StarMapUnload]`** (KSA 5949, rev 5916):
  - When a system switch will not fit in VRAM, `SaveSystemRestart.RestartInto` runs
    `Process.Start(<game>)` and then `Environment.Exit(0)`.
  - purrTTY's explicit shell teardown lives in `Unload`. On such exits a shell ends only when
    the pty hangs up (Unix) or the ConPTY closes (Windows).
  - That requires the Unix pty master to be close-on-exec (gotcha 16) and the ConPTY pipes to be
    non-inheritable. Otherwise the relaunched game inherits them and the shells are orphaned.
  - On each bump, grep KSA for new `Process.Start` and `Environment.Exit` sites.

### 8. Build references & native libs

In `Directory.Build.props`, `$(KSAFolder)` resolves to `<current-ksa>/dll/` (or
`KSA_DLL_DIR`, or a per-OS default). Every project's `.csproj` `<Reference>` pulls DLLs
from there; a **renamed / removed / split** assembly is a compile break. The referenced
set (verify each still exists in `<current-ksa>/dll/`):

- `KSA.dll`
- `Brutal.ImGui`, `Brutal.ImGui.Abstractions`, `Brutal.ImGui.Extensions`
- `Brutal.Vulkan`, `Brutal.Vulkan.Abstractions`, `Brutal.Vulkan.Vma`
- `Brutal.Core`, `Brutal.{Logging,Numerics,Collections,Strings,Memory}` (KSA 5801 / Brutal 2026.11; older builds used `Brutal.Core.Common` and `Brutal.Core.*`)
- `Brutal.Glfw`, `Brutal.Render.Common`, `Brutal.ShaderC`
- `Planet.Core`, `Planet.Render.Core`
- Copied native/loose DLLs (Display csproj `<None>`): `glfw3.dll`, `imgui.dll`, `VulkanEx.dll`

Test runtime copies must follow assembly renames too: `purrTTY.Display.Tests` and
`purrTTY.CustomShells.Tests` copy the explicit Brutal core dependency family (including
Buffers and Maths); `purrTTY.CustomShellContract.Tests` copies Logging. An old
`Brutal.Core.*.dll` glob silently misses renamed DLLs even when compilation passes.
These are test-only copies; game references stay `Private=false` in the mod.

The kFS projects reference a smaller set, all compile-only (`Private=false`): `purrTTY.Kfs`
uses `KSA.dll`, `Brutal.Logging.dll` and `Brutal.ImGui.Abstractions.dll` (the console's
`TerminalInterface`, `TerminalActionAttribute`, `ArgParseMode`); `purrTTY.Kfs.Lsp` uses `KSA.dll`
and `Brutal.Logging.dll`; `purrTTY.Kfs.Testing`, `purrTTY.Kfs.Cli` and the two kFS test projects
use `KSA.dll` (`purrTTY.Kfs.Tests` also `Brutal.ImGui.Abstractions.dll`). The kFS namespaces all
live in `KSA.dll` (`KSA.Kfs`, `KSA.Kfs.Host`, `KSA.Kfs.Host.Workbench`, `KSA.Kfs.Vm`,
`KSA.Kfs.Vm.Tables`, `KSA.Kfs.KC`, `KSA.Kfs.KAsm`, `KSA.Kfs.Compiler` …): if the game moves kFS
into its own assembly, every one of these projects needs the new reference **and**
`KsaAssemblyResolver` (`purrTTY.Kfs.Testing`) must be able to load it. Two facts about `KSA.dll`
itself are assumed there: it is IL-only but flagged x64, so on an arm64 host the resolver patches
the PE machine field in memory; and the stock table files are embedded resources named
`KSA.Kfs.Vm.Tables.Data.<file>` (`GameTables.RESOURCE_PREFIX`).

`purrTTY.GameMod.csproj` also copies non-KSA deps into the mod (`Ghostty.Vt.dll`,
`ghostty-vt.dll` native, `Tomlyn`, `StbImageSharp`, `ModMenu.Attributes`,
`Microsoft.Extensions.Logging.Abstractions`), and the kFS payload: `purrTTY.Kfs.dll`,
`purrTTY.Kfs.Lsp.dll` (through the `purrTTY.*.dll` glob) and the `nvim/` folder. These are **not** KSA-coupled — the vendored
`vendor/Ghostty.Vt` VT engine and its native lib are pinned independently and are *not*
part of this validation (they don't move when KSA moves).

If a Brutal assembly is split or a type moves between assemblies, the fix is usually a
`<Reference>` add + a `using` change, not logic — but confirm the type didn't also change
shape.

### 9. kFS extension, language server and console command

purrTTY extends **kFS**, the game's programmable flight computer (kC and kASM programs run by a
VM on a vehicle's computer), with `x_` services for lights and part animations, a `purr` console
command, a language server over the game's own compiler, and a launcher for the player's Neovim.
The code is in `purrTTY.Kfs/` and `purrTTY.Kfs.Lsp/` (both shipped), `purrTTY.GameMod/Kfs/`, and —
not shipped but equally coupled — `purrTTY.Kfs.Testing/` and `purrTTY.Kfs.Cli/`. Background:
`docs/kfs-info/` and `plans/KFS_EXTENSION_FOR_PARTS_PLAN.md`; the invariants are gotchas 44–54 of
`docs/gotchas.md`.

Two things set this surface apart. First, **it has never run in the game**: every claim below was
read from the decompiled 2026.11.25.5949 sources and is exercised offline only, so a review of a
new build should re-read the cited game code, not just diff it. Second, kFS was a prototype in
that build (behind `KfsPrototype.Enabled`, switched by `kfs on` or `KFS_PROTOTYPE=1`): expect it
to change more between builds than the rest of the game.

The extension installs **all-or-nothing** (`KfsExtension.Install`): if a required target or
capability below is missing it disables itself with one log line
(`purrTTY kFS extension: disabled — <reason>`) and stock kFS is untouched. That turns most
structural breaks into a clean "Medium"; the dangerous rows are the behavioral ones.

#### 9.1 Harmony patch targets

Applied by `KfsExtension` with the mod's Harmony instance (id `purrTTY`); `KfsExtension.Patches`
is the registry. Targets are resolved with reflection (`TargetMethod()` in each file) before
anything is changed. Harmony binds postfix parameters **by name**, so a renamed parameter in the
game is a break even when the types are the same.

| Patch (file under `purrTTY.Kfs/Patches/`) | KSA target & signature | Depends on | If KSA changes… |
|---|---|---|---|
| `ApplyRequestPatch` — **required** | `KSA.Kfs.Host.VehicleComputer.ApplyRequest(ServiceRequest request)` (`protected override bool`); postfix `(VehicleComputer __instance, ServiceRequest request, ref bool __result)` | The host calling it on the dispatch (solver) thread for every request a finished tick queued, as `!ApplyHostRequest(request) && !ApplyRequest(request)` → reject; the stock switch returning `false` for unknown ids; `ServiceRequest.Id` / `.Args`; `VehicleComputer.RejectService(request, reason)` | Not found → install refused. Found but no longer called for unknown ids (ids validated earlier, requests routed elsewhere) → setters silently do nothing or are rejected "no vehicle to act on". |
| `IssueResultPatch` — **required** | `VehicleComputer`'s explicit implementation of `KSA.Kfs.Vm.ISyscallHost.IssueResult(ushort service, ReadOnlySpan<uint> args, Span<uint> results)`, found through the interface map; postfix with the same three parameter names | The VM calling it inside `SYSCALL`, mid-tick, for every result-bearing service, with a result cell in `results`; the stock body filling only its own services | Not found (interface renamed, implementation moved to a base class) → install refused. Called at another time (after the tick) → getters return stale or zero values. |
| `BeforeInputEventsPatch` — **required** | `KSA.Kfs.KfsIntegration.BeforeInputEvents()` (`public static void`, no parameters); postfix with none | **Where and when the game calls it** — see 9.4 "frame order"; this is the main-thread pump's only entry | Not found → install refused. Still found but called from another place in the frame, or not at all while the kFS flag is off → part writes race the physics job, or commands are never applied. Behavioral, silent. |
| `BenchApplyRequestPatch` — optional | `KSA.Kfs.Host.HostComputer.ApplyRequest(ServiceRequest request)` (`protected virtual bool`, body `return false`); postfix `(HostComputer __instance, ServiceRequest request, ref bool __result)` | `VehicleComputer` overriding it **without calling the base** (so this runs only for the workbench's `BenchMachine`); the public members it reproduces a rejection with: `Machine.CountRejectedRequest()`, `Console.EventAllowed(k, hz)` / `Event`, `LastK`, `TickHz` | Only a friendlier message on the bench is lost. If `VehicleComputer` starts calling the base, the patch's `VehicleComputerCalls` counter becomes non-zero (it ignores such calls). A one-line virtual: the JIT may inline it. |
| `LoadEmbeddedFallbackPatch` — fallback, normally **not applied** | `KSA.Kfs.Vm.Tables.GameTables.LoadEmbedded()` (`public static`, returns `GameTables`); postfix `(ref GameTables __result)` | Used only when the `_tables` field (9.2) is not found; relies on `KfsIntegration.Tables` being `LazyInitializer.EnsureInitialized(ref _tables, GameTables.LoadEmbedded)` | If both the field and this method are gone the extension cannot publish its tables → install refused. |
| `KfsErrorEchoPatch` (`purrTTY.GameMod/Kfs/`) — optional | `KfsIntegration.Error(string)` — row in §1 | see §1 | see §1 |

#### 9.2 Reflection into non-public game members (silent when they drift)

- **`KfsIntegration._tables`** — private static `GameTables?`, the field behind the public
  `KfsIntegration.Tables`. `purrTTY.Kfs/Tables/TablesPublisher.cs` assigns it while it is null, so
  the game's loader never runs and every later reader gets the augmented tables. Requires: the
  field's name and exact type; that the accessor returns the field's value untouched when it is
  not null; that nothing reads `Tables` before the mod loads (otherwise: `disabled — tables
  already built`). `purrTTY.Kfs.Testing/KfsIntegrationTables.cs` resets the same field in tests.
- **`KfsIntegration._workbench`** — private static `Workbench?`.
  `purrTTY.Kfs/Tables/StockEditorProbe.cs` reads it (never the public getter, which creates the
  workbench) and then the public `Workbench.Editor.Building`, to learn whether the stock editor is
  compiling in the background. If the field (or `Editor` / `Building`) cannot be found, the probe logs
  the missing member once, `purr status` shows `editor: build state unreadable …`, and the probe
  answers "may be building" — so `<purrtty.h>` stays registered at uninstall instead of
  `SystemHeaders` being changed under a possible editor build. After a game update, check that line.
- **`KfsVehicleHost.AllHosts`** — `internal static readonly List<KfsVehicleHost>`.
  `purrTTY.Kfs/Game/HostDirectory.cs` finds it by name and reads it only under `lock (list)`,
  because the game locks the list object itself for every access. Missing or another type →
  install refused (`game back end unavailable`). A change of the **locking discipline** (another
  lock object, a concurrent collection) is silent and would make the reads racy.
- **`KfsIntegration.ConsoleWindowWriter`** — private nested type, the default value of
  `KfsIntegration.Output`. `purrTTY.GameMod/Kfs/KfsLspService.cs` (`IsGameConsoleWriter`) compares
  the writer's type with it. Renamed → the comparison is false → `kfs` errors appear twice in the
  console while the language server runs (cosmetic).
- **Field-by-field copies of light templates** — `purrTTY.Kfs/Game/LightOverrides.cs` copies a
  `LightModule.TemplateData`, its `FloatReference`s and its `ColorRgbReference` by creating an
  uninitialised instance and copying **every instance field of the whole type hierarchy**. It
  assumes these are plain data holders: that skipping the constructor skips nothing a copy needs,
  that `ColorRgbReference.OnDataLoad(null!)` recomputes the rendered colour without using its
  argument, and that `IndexedColor.Invalid` stops it from re-deriving R/G/B from a named colour.
- (Tests only) `purrTTY.Kfs.Tools.Tests/Nvim/KasmSyntax.cs` reads the string literals in the IL of
  `KSA.Kfs.KAsm.Assembler` to list the kASM directives; an obfuscated or restructured assembler
  makes `KasmSyntaxTests` fail rather than pass silently.

#### 9.3 Public game members used (compile-checked — diff them for meaning, not just shape)

- **Tables and their JSON schema** (`purrTTY.Kfs/Tables/TableAugmenter.cs`,
  `Catalog/CatalogEmitter.cs`, `Catalog/CatalogSelfCheck.cs`): `GameTables.Load(Func<string,
  Stream?> open, string origin)`, `GameTables.Origin`, `GameTables.RESOURCE_PREFIX`,
  `GameTables.Arguments` / `CellsOf`, `GameTablesException`. The augmenter edits two of the
  embedded files as JSON before the game's loader parses them, so it depends on their shape:
  `services.json` is an object with a `services` array whose rows carry an integer `id`;
  `enums.json` is an object with an `enums` object keyed by enumeration name. The rows it appends
  use the keys `id`, `name`, `symbol`, `kind`, `args`, `arg_types`, `arg_domains`, `results`,
  `result_types`, `cost_class`, `summary`, `kc`, `kscript`, `host_checks`, `requires_stock`, and
  domain strings in the game's syntax (`any`, `finite`, `finite:>=0`, `enum:<key>[a..b]`). A new
  required key, a renamed key, a changed domain grammar or a new file that cross-references
  services → the game's own loader rejects the augmented tables and the extension disables itself
  (`tables rejected`); a new *optional* key is silent — compare a stock row with
  `purrTTY.Kfs.Tests/Snapshots/services.rows.json`. The service ids `0x5000`–`0x50FF`
  (`KfsBuildInfo`) must stay unused by the game: the self-check refuses the install if a stock row
  takes one.
- **Compiler statics** (`Tables/SystemHeaderRegistration.cs`): `KSA.Kfs.KC.SystemHeaders.Files`
  (name → header text) and `.Builtins` (names the compiler treats as services), both plain mutable
  collections read without a lock by every compile.
- **Host** (`Runtime/ServiceRouter.cs`, `Runtime/VehicleBinding.cs`): `VehicleComputer.Grid` (a
  new `TickGrid` per start, null after load/stop/fault — how a new run is detected),
  `RejectService`, `Console.EventAllowed`, `LastK`, `TickHz`, `Machine.CountRejectedRequest()`,
  `Enqueue(...)` + `Event(text)` for program-log reports.
- **Vehicle parts** (`Game/KsaVehicleParts.cs`, mirrored as pure rules in `Game/KsaPartRules.cs`):
  `Vehicle.IsDisposed` / `.Parts` / `.Id` / `.LightsOn`; `PartTree.Modules` (`ModuleList.Get<T>()`
  spans, `HasAny<T>()`) and `.States`; `ModuleBase.InstanceId` / `.Parent` / `.TemplateId`;
  `Part.Tree` / `.FullPart` / `.LightSwitch` / `.SubtreeModules` / `.InstanceId` /
  `.DisplayName` / `.Template.Id`; `LightModule.Template` (settable; `TemplateData.Type`,
  `Intensity`, `Range`, `ColorRgb`, `InnerAngle`, `OuterAngle`, `DisableInIva`) and
  `LightModule.IsActive`; `PowerConsumer.LightSwitch` / `.LightIsActive` (settable);
  `KeyframeAnimationModule.TimeGoal` (settable, seconds), `.Shared.Duration`,
  `.ShowDeployRetract`, `.StatesIdx`, its committed `State` (`TimeCurrent`, `DeploymentState`)
  through `ModuleStateful<…>.TryGetFrom`; `SolarPanel.KeyframeAnimationModule`; `Parachute`.
  The numeric values of `DeploymentState` (Deployed 0, Retracted 1, Deploying 2, Retracting 3)
  and of the light type (Spot 0, Point 1) are hard-coded in `KsaPartRules`.
- **Host discovery** (`Game/GameHostSource.cs`, `Game/KsaConsoleHost.cs`): `KfsVehicleHost.IsAlive`,
  `.Id`, `.Computer`, `.Vehicle`, `.Layout`, `KfsVehicleHost.Find(Vehicle)` (never `For`, which
  creates a host).
- **Console** (`Console/PurrConsole.cs`, `Game/KsaConsoleHost.cs`): `Program.ConsoleWindow`,
  `Program.TerminalInterface`, `TerminalInterface.RegisterCommand(Delegate, object?)`,
  `UnregisterObject(object)`, `[TerminalAction(name, description, ArgParseMode.Unparsed)]`,
  `Program.ControlledVehicle`, `KfsIntegration.Print(string)`.
- **Workbench and `kfs` verbs** (`purrTTY.GameMod/Kfs/KsaKfsGameSide.cs`, `KfsLspService.cs`,
  `KfsNeovimLauncher.cs`): `KfsIntegration.Workbench`, `.Output` (settable `TextWriter`),
  `.Tables`; `Workbench.Workspace` (`Folder`, `Exists`, `Write`, `List`, `Ensure`, static
  `Extensions`), `.TargetVehicle` (`IVehicleTarget.Computer` / `.Layout`), `.Bench`,
  `.Tables.BenchLayout`, `.TargetName`, `.Loaded` (`AssembleOutcome.Image` / `.Project` →
  `BuildProject.Name` / `.Folder` / `.Files`), `.TargetActiveVehicle()`; `KfsPrototype.Enabled`;
  `KfsConsoleCommands.Execute(string)`; `VersionInfo.Current.VersionString`;
  `ImGui.SetClipboardText` (`KfsMenu`).
- **Toolchain, for the language server and `purr-kfs`** (`purrTTY.Kfs/Runtime/ProjectCompiler.cs`,
  `purrTTY.Kfs.Lsp/Build/`, `Index/`, `Features/`, `Live/`): `Compiler.Compile`,
  `Compiler.CompileOnThisThread`, `Compiler.COMPILE_THREAD_STACK_BYTES`, `new Assembler(tables,
  part, layout) { ProjectFiles }`, `ImageReader.Read`, `Diagnostic`, `IrModule.Functions` /
  `.Globals`, `CompileReport`, `DiagnosticManual.Page`, `VehicleHeader.Generate` / `.Headers` /
  `.Names`, `BuiltinIncludes`; the `GameTables` data the index reads (`Services`, `ServicesData`,
  `OpcodesData`, `OpcodesByMnemonic`, `CostsData`, `SysBlockData`, `TrapsData`, `CatalogueData`,
  `MessagesData`, `AllowedTickHz`, `BenchPart`, `BenchLayout`); `IoLayout.Fingerprint` /
  `.CommandFields` / `.SensorFields`, `MessageLayout`, `PartDescription.Name` / `.Costs`; for live
  data `HostComputer.Console` (`ConsoleLog.Version`, `CopyTail`), `.Snapshot` /
  `WantSnapshot()`, `.LoadedBytes`, `.Traces`, `.Part`. A changed table shape here degrades hover
  and completion text; it does not affect what programs do.
- **Standalone host** (`purrTTY.Kfs.Cli/Commands/StandaloneLanguageHost.cs`): the format of the
  `vehicle.h` the game's `kfs build` writes (the three-line banner of `GeneratedHeaders.VehicleH`,
  `KFS_LAYOUT_FINGERPRINT`, the `THRUSTER_COUNT` / `ENGINE_COUNT` / `GIMBAL_COUNT` defines) and
  `LayoutGenerator.Vehicle` being a pure function of those three counts.
- **Offline runtime** (`purrTTY.Kfs.Testing/`): the game's own test doubles and host —
  `FakeVehicle` (+ `Dispatch`, `Props`), `BenchMachine`, `HostSample`, `KcFrontEnd.Register`.

#### 9.4 Behavioral assumptions (nothing but reading the game source catches these)

- **Frame order** (gotchas 45, 46). `Program.PrepareFrame` runs, in this order:
  `JobSystems.VehicleSolver.Wait()` → `Universe.ApplyVehicleSolvers()` (commits the finished
  physics job's part states) → `KfsIntegration.BeforeInputEvents()` → `InputEvents.ApplyInputEvents()`
  → … → `Universe.ExecuteNextVehicleSolvers(...)`. purrTTY writes to live part modules only inside
  the `BeforeInputEvents` postfix, on the assumption that **no vehicle-solver job is running
  there**. If the game moves that call, starts the next job earlier, or commits results later,
  the writes race the physics job. Re-read `PrepareFrame` in full on every update.
- **Dispatch on solver threads, before the part modules update.** Inside the job,
  `PhysicsBubble.RunFlightComputer` → `KfsDispatch.TryComputeControl` → `KfsVehicleHost.Dispatch`
  runs the kFS computer, and `VehicleUpdateState.UpdateModules` steps the parts afterwards. The
  service hooks therefore run on job threads and read part state committed by the *previous* job;
  the simulator (`SimVehicle`: step → pump → dispatch) and the documentation of what a program
  reads back are built on that order. An exception that escapes a dispatch is caught by
  `TryComputeControl`, which calls `KfsVehicleHost.Fault` — the vehicle's kFS computer is halted.
- **The stock body of `BeforeInputEvents` is gated and sorted.** It does nothing while
  `KfsPrototype.Enabled` is false, runs `KfsVehicleHost.ReleaseDead()` only when enabled, and
  drains hosts sorted by `string.CompareOrdinal` of `Vehicle.Id`. The pump mirrors the order and
  filters by `KfsVehicleHost.IsAlive` itself.
- **Light rendering reads the template every frame.** A `LightModule.TemplateData` is shared by
  all lights of a part template; replacing one module's `Template` reference with a private copy
  changes only that light, takes effect without any further call, and a zero range is skipped by
  the renderer. Animations move toward `TimeGoal` (seconds) at their own speed;
  `ShowDeployRetract` is what the stock part window uses to offer Deploy/Retract buttons instead
  of a slider. A light's switch is its full part's `LightSwitch`, ignored when the switch's part
  is on another part tree, and an interior (`DisableInIva`) light is lit whatever its switch says.
- **Console registration.** Same name + same owner replaces; `UnregisterObject` removes an
  owner's commands; the console's `help` builds one dictionary of all names with `Add` and
  **throws on a duplicate name across owners** (gotcha 49). A registered delegate must carry
  `[TerminalAction]`.
- **`kfs` verb parsing** (gotcha 50). `KfsConsoleCommands.Execute` splits the verb at the first
  space and catches the verbs' exceptions itself; `Workbench.Build` takes the whole rest as the
  file; **`Workbench.Load` splits its arguments at every space** (`--part <name>`, `--vehicle`,
  else the file); `target` takes `bench` | `vehicle`; `manual` takes one code. The language
  server builds exactly these lines (`purrTTY.Kfs.Lsp/Features/CommandProvider.cs`,
  `KfsCommandLine.Resolve`) — if a parser changes, an editor command runs something else than
  intended.
- **`KfsIntegration.Workbench` and `Output`** (gotchas 51, 52). The getter creates the workbench
  on first use over `<Documents>/My Games/Kitten Space Agency/kfs` without touching the disk,
  registers the kC front end, and assigns `workbench.Out = Output` on **every** read;
  `Workspace.List()` / `Ensure()` / `Write()` create the folder. `KfsIntegration.Error` logs and
  also writes to `Output` unless `Output` is the game's `ConsoleWindowWriter`. `Workbench.TargetVehicle`
  falls back to the bench (and prints once) when its vehicle is gone.
- **Project rules.** `purrTTY.Kfs/Runtime/ProjectResolver.cs` re-implements
  `Workspace.ProjectOf` (which files form a project) and `ProjectCompiler.cs` re-implements
  `Workbench.BuildText`, so that the language server and `purr-kfs` build what the game would.
  `ProjectParityTests` compares them with the game's own on every test run — a failure there
  after an update means the rules moved.
- **Thread-safety of the live reads.** The language server's feed reads `ConsoleLog` and the
  machine snapshot from a timer thread, relying on those members taking the log's lock or being
  volatile reads (cited in `purrTTY.Kfs.Lsp/Live/HostComputerLive.cs`).
- **Reload.** `KfsIntegration._tables`, `SystemHeaders`, `KfsIntegration.Output` and the
  console's command library are game statics that outlive an unload of the mod (gotcha 48).

Diff for this section: everything under `decomp/KSA.Kfs*` (especially `KfsIntegration`,
`KfsVehicleHost`, `KfsDispatch`, `KfsConsoleCommands`, `KfsPrototype`, `KSA.Kfs.Host/HostComputer`
and `VehicleComputer`, `KSA.Kfs.Host.Workbench/Workbench` and `Workspace`,
`KSA.Kfs.Vm.Tables/GameTables`, the embedded table files `decomp/KSA.Kfs.Vm.Tables.Data.*.json`
(first `services.json` and `enums.json`), `KSA.Kfs.KC/SystemHeaders`),
`KSA/Program.cs` (`PrepareFrame`), `KSA/PhysicsBubble.cs` (`RunFlightComputer`),
`KSA/VehicleUpdateState.cs` (`UpdateModules`), `KSA/LightModule.cs`,
`KSA/KeyframeAnimationModule.cs`, `KSA/PowerConsumer.cs`, `KSA/Part.cs` (`LightSwitch`), and
`Brutal.ImGuiApi.Abstractions/TerminalInterface.cs`. After the diff, run the kFS tests against
the new assemblies ("Build against the new KSA" below) and then the in-game checklist of the
plan's §10 — until that checklist has been worked through once, treat every row here as
unverified in the game.

---

## Build against the new KSA

Point the build at `<current-ksa>/dll/` and rebuild + test. This sweeps all
compile-visible breaks (§1 signatures on directly-called members, §8 refs):

```bash
# from the purrtty repo root; adjust the env var to the supplied snapshot
KSA_DLL_DIR="<current-ksa>/dll" PURRTTY_DIST_DIR="$PWD/dist/" dotnet build purrtty.slnx
KSA_DLL_DIR="<current-ksa>/dll" PURRTTY_DIST_DIR="$PWD/dist/" dotnet test purrtty.slnx --nologo -v quiet
```

Always set `PURRTTY_DIST_DIR`. Without it, GameMod's build deploys the mod into the per-OS
default mods directory, which on macOS/Linux is `$(HOME)/repos/meow-sci/mods/mods/`.
`KSA_DLL_DIR` is optional when the companion `ksa-game-assemblies` checkout sits next to the
repo. `Directory.Build.props` then resolves it automatically. To confirm the build used the
new DLLs, check the `/reference:…KSA.dll` path in a `-v diag` build log.

(On Windows PowerShell: `$env:KSA_DLL_DIR="<current-ksa>\dll"; dotnet build purrtty.slnx`.)

A clean build **does not** clear the mod — it means only that directly-referenced members
still line up. Harmony targets (§1, §9), reflection (§3, §9), asset ids (§2), and every behavioral
assumption still need the source diff. The **terminal's** tests are backend/logic only (they
mock/avoid the engine), so they exercise almost none of the terminal's KSA coupling.

The **kFS tests are different** and are worth reading closely after a game update:
`purrTTY.Kfs.Tests` and `purrTTY.Kfs.Tools.Tests` load the new `KSA.dll` and run the game's real
kFS — tables, kC compiler, assembler, VM, host — with purrTTY's extension installed through the
real installer. A green run therefore does prove, for the new build: the extension's patch
targets (§9.1) resolve, the patches apply, and their postfixes run when the tests call the patched
methods (it cannot prove that the *game* still calls those methods, or from where — and no test
applies `KfsErrorEchoPatch`); `KfsIntegration._tables` and
`KfsVehicleHost.AllHosts` are found; the augmented tables pass the game's loader; `<purrtty.h>`
and every example program compile; the project resolver still agrees with the game's
`Workspace.ProjectOf` and `Workbench.BuildText` (`ProjectParityTests`); and the Vim syntax file
still matches the opcode table and the assembler's directives (`KasmSyntaxTests` fails when it
does not — regenerate with `PURRTTY_UPDATE_SNAPSHOTS=1`). What they **cannot** prove is
everything that needs a running game: frame order and thread placement, JIT inlining of small
patched methods, live part modules, the console, the workbench and the editor. Those are the
behavioral rows of §9. If the kFS test count drops to zero, a game type leaked into a test type's
shape and the assembly no longer loads (`docs/build-and-test.md`).

## Severity triage

- **Critical** — a **required** Harmony patch (`Patch01`, `Patch03`) target drifted, or the
  input chain reordered: core typing/gating breaks for every user. (`Patch04` is required too, but
  its drift only leaks *player-bound* mouse actions — no defaults bind a mouse button — so rate it
  **Medium**.)
- **High** — render-pipeline coupling (`RenderTranslucencyPassPatch`, `SharedQuadResource`,
  `OffscreenRenderTarget`, camera conventions) or the ImGui backend changed: in-world
  terminals break or misrender.
- **Medium** — an **optional** patch (`Patch02`, `ConsoleWindowPrintPatch`) or an asset id
  drifted: a feature (fallback menu, game-console capture) silently degrades but the mod
  stays usable.
- **Low** — a build ref rename with a mechanical fix; a reflection probe (`KeySuper`) whose
  absence is already null-tolerant.

For the kFS coupling (§9) rate by what the player loses, remembering that the extension is
all-or-nothing and reports why it is disabled:

- **High** — a change that makes the extension act wrongly while it still installs: the frame
  order or thread placement moved (writes to parts while a solver job runs), the part/light/
  animation module fields changed meaning, `SystemHeaders` or `Output` semantics changed, the
  `kfs` verb parsers changed (the editor would run a different command than it shows).
- **Medium** — a required target or a reflected member is gone: the extension disables itself
  cleanly (`purr status` and the log say why), kFS programs lose the `x_` services, the terminal
  is unaffected. Also: the console registration API changed (`purr` missing; the menu still runs
  the verbs).
- **Low** — an optional patch (`BenchApplyRequest`, `KfsErrorEchoPatch`) drifted; hover or
  completion text is out of date; the Vim syntax file needs regenerating.

## Output format

Produce a review, not silent edits. For each affected touchpoint:

```
### <touchpoint> — <SEVERITY>
- KSA delta: <what changed between previous and current, with decomp file:line or version.json commit>
- purrTTY location: <repo file:symbol>
- Coupling kind: <harmony | reflection | render/behavioral | game-state | build-ref | asset-id | lifecycle>
- Impact: <what breaks at runtime/compile, who notices>
- Change needed: <none | mechanical: … | behavioral redesign: …>
```

End with a **summary table** (touchpoint · severity · change needed) and an overall verdict:
*no changes needed* / *mechanical fixes only* / *behavioral work required*. If both
snapshots weren't available, state exactly which checks you could not perform.

## Maintaining this skill

This file exists twice in the repository, at `.agents/skills/upgrade-ksa/SKILL.md` and
`.claude/skills/upgrade-ksa/SKILL.md` (the two skill folders mirror each other). Keep the copies
identical: an edit to one that is not made to the other is how the second copy once fell three
game builds behind.

Per the repo's Instruction Maintenance Mandate: when purrTTY's coupling surface changes
(a new Harmony patch, a new game-state read, a new render-injection point, a dropped one),
update this catalog in the same work item. The catalog is only useful if it stays an
exhaustive mirror of the real coupling — a missed touchpoint here is a silent breakage at
the next KSA bump. Grep signals to re-derive the surface: `HarmonyPatch`, `ModLibrary.`,
`Program.`, `Universe.`, `Cursor.`, `GameSettings.`, `Renderer`/`Vk*` in `InWorld/`,
`ImGuiBackend`, `<Reference Include=` in `.csproj`, and the StarMap attributes in
`TerminalMod.cs`. For the kFS surface (§9): `using KSA` in `purrTTY.Kfs/`, `purrTTY.Kfs.Lsp/`
and `purrTTY.GameMod/Kfs/`; `AccessTools.`, `GetField(`, `GetNestedType(`, `GetInterfaceMap`
(the patch targets and reflected members); `KfsIntegration.`, `KfsVehicleHost.`,
`KfsConsoleCommands.`, `SystemHeaders.`, `GameTables.`, `Workbench`, `Workspace.`; and the
decompiled file:line citations in the XML comments of those files — each one names a game
behavior the code relies on.
