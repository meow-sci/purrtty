---
name: upgrade-ksa
description: Validate purrTTY against an upstream Kitten Space Agency (KSA) game update. Use when a new KSA build lands and you must decide whether purrTTY needs code changes. Enumerates every coupling point between purrTTY and KSA/Brutal/Planet/StarMap game code (Harmony patch targets, the render/Vulkan pipeline, ImGui backend, game-state APIs, the input chain, menu injection, lifecycle, build refs, and behavioral assumptions), and drives a diff-of-KSA → impact-on-purrTTY review. Requires the CURRENT (new) and PREVIOUS KSA decompiled sources to be supplied — never hard-codes machine paths.
---

# Upgrade / Re-validate purrTTY against a KSA game change

purrTTY is a terminal-emulator **mod** for KSA. It compiles against KSA's reference
assemblies, **patches** several game methods with Harmony, injects a draw into KSA's
render pipeline, drives a second ImGui context on a Brutal Vulkan backend, and reads
live game state (vehicles/parts/camera). When the upstream game changes, any of that
can break — sometimes at compile time, but the dangerous cases break **silently at
runtime**. This skill is the checklist and method for deciding what, if anything,
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

All in `purrTTY.GameMod`. A drifted target does **not** crash the mod (`Patcher.ApplyPatch`
wraps each in try/catch and logs), so failures are invisible unless you check the log or
reason about it here. Verify **type exists · method exists · exact parameter types ·
semantics of the arm you depend on**.

| Patch (file) | KSA target & signature | Depends on | If KSA changes… |
|---|---|---|---|
| `Patch01` (`Patcher.cs`) — **required**, input gate | `KSA.Program.OnKey(GlfwWindow, GlfwKey, int, GlfwKeyAction, GlfwModifier)` prefix | The `Press`/`Repeat`/`Release` **switch arms** inside `Program.OnKey`; that release-arm toggles (`ToggleFps`/`ToggleUi`/…) fire on `Release` | Signature drift → gate stops applying → terminal typing leaks into game controls. Semantics change (which actions fire on which arm) → the held-key model in `s_gameHeldKeys` may leak or strand keys. Re-read `Program.OnKey` in full. |
| `Patch02` (`Patcher.cs`) — optional, menu fallback | `KSA.Program.DrawProgramMenusHook()` postfix | It being an empty public method KSA calls inside its `BeginMenuBar()` right after the View menu; also `Program.MainViewport.MenuBarInUse` | Hook removed/renamed → fallback menu disappears (only matters when ModMenu absent). `MainViewport.MenuBarInUse` gone → menu auto-hides / game hotkeys leak while open. |
| `Patch03_HotkeyGuard` (`Patcher.cs`) — **required**, typing guard | `GameSettings.OnKeyAll` prefix (`ref bool __result`) | `Program.ConsoleWindow` static + `ConsoleWindow.IsOpen`; `ImGui.GetIO().WantTextInput` | `OnKeyAll` signature/return-model change → typing in mod text fields fires game hotkeys. `ConsoleWindow`/`IsOpen` rename → NRE guard or console-exemption logic wrong. |
| `ConsoleWindowPrintPatch` (`Patches/ConsoleWindowPrintPatch.cs`) — optional, game-console capture | `ConsoleWindow.Print(ReadOnlySpan<char>, ImColor8, int)` postfix | **The single-sink funnel**: all string/char console output routing through this exact overload | **Brutal-version-sensitive.** Older API was `Print(string, uint, int, ConsoleLineType)`. If a new build prints via `u8`/byte-span/`ImString` overloads that call `AddPendingMessage` directly, capture silently escapes. Re-verify every `ConsoleWindow.Print*` overload and which call sites use which. |
| `RenderTranslucencyPassPatch` (`InWorld/Patches/RenderTranslucencyPassPatch.cs`) — optional, in-world quad inject | `SuperMeshRenderSystem.RenderTranslucencyPass(CommandBuffer, bool useCustomRenderPass, Viewport)` postfix | `Viewport.OffscreenTarget` as `KSA.Rendering.RenderTarget`; `RenderTarget.{ColorAttachment,DepthAttachment}` (resolve to the MSAA images when multisampled); `Viewport.Size`; `ImageBarrierInfo.Presets.ColorAttachmentRead`; `CommandBuffer.{PipelineBarrier2,BeginRendering,EndRendering}` (dynamic rendering, `LoadOp.Load`); the **draw-order** guarantee (runs after atmosphere/cloud/ocean) and the fact that the method **closes its own** `BeginRendering`/`EndRendering` scope before returning | Highest-risk render coupling. Signature drift → in-world terminals vanish. **Draw-order change** (KSA moves atmosphere/ocean after this pass, or restructures `RenderTarget`) → planet-silhouette cutout returns (gotcha 32). Diff `SuperMeshRenderSystem`, `PlanetTransparenciesRenderer`, the ocean renderer, `PartModelGlass.WriteCommandsColor`, and `KSA.Rendering/RenderTarget`. |

After the diff, if in doubt, confirm the patch still applies by checking the mod log for
`REQUIRED/optional Harmony patch '…' failed to apply` at runtime.

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
  - `RenderCore.ShaderModuleUtils.FromString(...)` — runtime shaderc compile (needs
    `Brutal.ShaderC`).
  - Presets: `RenderingPresets.ReverseZDepthStencil.{DepthTestNoWrite,NoDepthTest}`,
    `Presets.Rasterization.Fill.CullNone`, `Presets.InputAssembly.TriangleList`,
    `Presets.Entrypoint.Main`. **Reverse-Z** is assumed everywhere — if KSA leaves
    reverse-Z, depth compare flips.
  - `Renderer` members: `.Device`, `.Graphics`, `.Allocator`, `.DynamicStateInfo`,
    `.ViewportState`; `VertexInput`, `BufferEx`, `DescriptorSetLayoutEx`, `VkUtils.StageAndUploadToBuffer`.
  - **Premultiplied-alpha assumption** (gotcha 28): `QuadFragGlsl` un-premultiplies →
    gamma-decodes (`pow(2.2)`) → re-premultiplies, because KSA's ImGui backend blends over
    a transparent clear (premultiplied output) and the stock frag would force alpha=1.
    If KSA's ImGui compositing or gamma handling changes, the quad's color/opacity is wrong.
- **`OffscreenRenderTarget.cs`** (`InWorld/`): purrTTY's **own** sampleable colour+depth target (KSA rev 5154 deleted the old `KSA.RenderTarget`/`Framebuffer` it used to wrap). It reproduces the usage flags of the deleted `RenderTarget.BuildAttachments` directly on `ImageEx` and uses `Presets.Sampler.SamplerLinearClamped`. Low KSA coupling now; only the `Presets` sampler and the Brutal Vulkan image/allocator API matter.
- **`InWorldQuad.cs`** (`InWorld/Display/`): `Program.GetMainCamera()`, `Camera.MVP.{projection,viewProjection}`, `Camera.VPInv.view`, `Program.SetViewport(cmd)`. Camera matrix conventions (row-vector `mvp = model * viewProjection`) are load-bearing — a handedness/convention change silently mislocates/mirrors the quad.
- **`PerFrameRenderer.cs`** / **`InWorldTerminalInstance.cs`**: `Program.GetRenderer()` (null before renderer live), device queue submit, `Device.WaitIdle()`.
- **`InWorldTerminalManager.cs`**: `Program.GetRenderer()?.Device.WaitIdle()`; `GameSettings.GetSampleCount()`.

Whole-feature check: diff `SuperMeshRenderSystem`, `OffscreenTarget`, `RenderTarget`,
`Renderer`, `Camera`, `RenderingPresets`, `Presets`, `ShaderModuleUtils`, and the
`Brutal.VulkanApi` structs used (`VkRenderingInfo`, `VkRenderingAttachmentInfo`,
`VkPipelineRenderingCreateInfo`, `ImageTransition`, `ImageBarrierInfo`) between snapshots.

### 3. ImGui backend (Brutal.ImGui + its Vulkan impl)

- **Secondary context / offscreen backend** (`InWorld/OffscreenImGuiBackend.cs`, `OffscreenImGuiContext.cs`): `ImGuiBackendVulkanImpl` with `.CreateInfo { Device, GraphicsQueue, RenderPass, SubPass, MinImageCount, ImageCount, SampleCount, DescriptorPoolSize }`, `.RenderDrawData(drawData, cmd)`, `.Dispose()`. Asserts `MinImageCount>=2`, `ImageCount>=MinImageCount`, `DescriptorPoolSize>0`. The context install/detach reads `ImGui.GetIO().BackendRendererUserData` and `ImGui.GetMainViewport().RendererUserData`. A backend-API change (ctor fields, the single-backend assertion, the userdata slots) breaks in-world rendering.
- **Kitty-graphics texture registration** (`purrTTY.Display/Ghostty/ImageTextureCache.cs`): `ImGuiBackend.Vulkan.AddTexture(sampler, imageView)` → `ImTextureRef`, `.RemoveTexture(texRef)`; `SimpleVkTexture(...)` ctor; `Renderer.LinearSampler`; shared **1000-slot descriptor pool** assumption (LRU eviction). If `AddTexture`/`ImTextureRef` semantics or the pool budget change, kitty images fail to display or exhaust descriptors. (`InWorldTerminalRenderer.cs` notes `ImTextureRef` is the raw `VkDescriptorSet`.)
- **Font atlas** (`purrTTY.Display/Rendering/PurrTTYFontManager.cs`): `ImGui.GetIO().Fonts` → `ImFontAtlasPtr.AddFontFromFileTTF(ImString, 32.0f)`; `FontManager.Fonts[name]`. Atlas-API or KSA `FontManager` change → fonts don't load.
- **IO reads** across `TerminalWindow*.cs`: `ImGui.GetIO().WantTextInput`, character queue, key-state queries, `ImGui.GetMainViewport()`. Mostly stock ImGui (stable), but Brutal's binding versions the exact ptr shapes.
- **Reflection into ImGui IO** (`ToggleHotkeyBinding.cs:303`): `typeof(ImGuiIOPtr).GetProperty("KeySuper", …)` — a possibly-absent property probed by name. If Brutal renames/removes `KeySuper`, the Super-modifier read silently returns null (no error). This is the **only reflection touchpoint** and it targets ImGui, not KSA game types.

Diff `Brutal.ImGuiApi` / `Brutal.ImGui` (esp. `ImGuiBackendVulkanImpl`, `ImGuiIOPtr`,
`ImFontAtlasPtr`) and KSA's `ImGuiBackend` / `FontManager`.

### 4. Game-state / world API

Read-only-ish reads of live game state; direct references, so signature changes are
compile-caught, but **semantic** changes (what "ego space" means, identity vs rebuild on
decouple) are not.

- **`InWorld/VehicleLookup.cs`**: `Universe.CurrentSystem?.All.UnsafeAsList().OfType<Vehicle>()`; `Program.ControlledVehicle`; `Vehicle.Id`; `Vehicle.Parts.Parts`; `Part.SubParts`. Relies on KSA **moving the same `Part` instance** into a new vehicle on decouple/dock (identity follow — gotcha 31). If KSA starts rebuilding parts on reparent, follow-tracking breaks.
- **`InWorld/Display/InWorldQuad.cs`**: `Vehicle.GetMatrixAsmb2Ego(camera)`, `Vehicle.Asmb2Ego`, `Part.PositionEgo(in double4x4)`, `Part.Asmb2Ego(doubleQuat)`, `Part.Id`, `Part.SubParts`. **Ego-space** transform conventions and the choice to exclude the part's own scale (not baking `MatrixAsmb2Ego`) are semantic assumptions — diff these methods in `Part`/`Vehicle`.
- **Ray picking**: `Cursor.InputRay` (a `Ray`), `Ray.RaycastMollerTrumbore(v0,v1,v2,out t)`. Used for click-to-focus and app-mouse mapping in `InWorldTerminalManager.cs` and `InWorldQuad.TryRaycast`. Ego-space assumption must match the transform above.
- **Numerics** (`Brutal.Numerics`): `double3`, `float3`, `float2`, `float4x4`, `double4x4`, `doubleQuat`, `floatQuat` and their ops (`float3.Transform`, `float4x4.CreateFromQuaternion/CreateTranslation/CreateScale/CreateRotation*`, `floatQuat.Pack`, `double3.Dot`). Row-vector multiply order is assumed throughout.

Diff `Universe`, `Vehicle`, `Part`, `Program` (ControlledVehicle/GetMainCamera/GetRenderer),
`Cursor`, `Ray`, and `Brutal.Numerics`.

### 5. Input chain

Beyond the Harmony patches (§1), purrTTY assumes KSA's **key short-circuit order**:
`GameSettings.OnKeyAll → Popup.OnKeyAll → ConsoleWindow.OnKey → … → Program.OnKey`. If a
handler returns `true`, downstream is skipped. `Patch03` (`OnKeyAll`) and `Patch01`
(`Program.OnKey`) sit at two ends of this chain; a reorder or a new early handler can
change whether the gate/guard actually intercepts. Diff `Program.cs`'s key dispatch and
`GameSettings.OnKeyAll`. GLFW enums (`GlfwKey/GlfwKeyAction/GlfwModifier/GlfwWindow` from
`Brutal.GlfwApi`) are used in `Patch01`.

### 6. Menu injection

`Program.DrawProgramMenusHook()` (§1 Patch02) and `Program.MainViewport.MenuBarInUse`.
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
- **ALC assembly sharing** (`mod.toml`): `[StarMap] ExportedAssemblies =
  ["purrTTY.CustomShellContract", "purrTTY.Logging"]` — the published inter-mod ABI that
  lets gatOS share one `CustomShellRegistry.Instance`. If StarMap changes attribute names,
  the lifecycle contract, or the ALC import/export resolution, revisit `TerminalMod` +
  `mod.toml`. (StarMap is the loader, not KSA game code, but a StarMap bump ships
  alongside KSA builds often enough to check here.)

### 8. Build references & native libs

In `Directory.Build.props`, `$(KSAFolder)` resolves to `<current-ksa>/dll/` (or
`KSA_DLL_DIR`, or a per-OS default). Every project's `.csproj` `<Reference>` pulls DLLs
from there; a **renamed / removed / split** assembly is a compile break. The referenced
set (verify each still exists in `<current-ksa>/dll/`):

- `KSA.dll`
- `Brutal.ImGui`, `Brutal.ImGui.Abstractions`, `Brutal.ImGui.Extensions`
- `Brutal.Vulkan`, `Brutal.Vulkan.Abstractions`, `Brutal.Vulkan.Vma`
- `Brutal.Core.{Logging,Numerics,Collections,Common,Strings,Memory}`
- `Brutal.Glfw`, `Brutal.Render.Common`, `Brutal.ShaderC`
- `Planet.Core`, `Planet.Render.Core`
- Copied native/loose DLLs (Display csproj `<None>`): `glfw3.dll`, `imgui.dll`, `VulkanEx.dll`

`purrTTY.GameMod.csproj` also copies non-KSA deps into the mod (`Ghostty.Vt.dll`,
`ghostty-vt.dll` native, `Tomlyn`, `StbImageSharp`, `ModMenu.Attributes`,
`Microsoft.Extensions.Logging.Abstractions`). These are **not** KSA-coupled — the vendored
`vendor/Ghostty.Vt` VT engine and its native lib are pinned independently and are *not*
part of this validation (they don't move when KSA moves).

If a Brutal assembly is split or a type moves between assemblies, the fix is usually a
`<Reference>` add + a `using` change, not logic — but confirm the type didn't also change
shape.

---

## Build against the new KSA

Point the build at `<current-ksa>/dll/` and rebuild + test. This sweeps all
compile-visible breaks (§1 signatures on directly-called members, §8 refs):

```bash
# from the purrtty repo root; adjust the env var to the supplied snapshot
KSA_DLL_DIR="<current-ksa>/dll" dotnet build purrtty.slnx
KSA_DLL_DIR="<current-ksa>/dll" dotnet test purrtty.slnx --nologo -v quiet
```

(On Windows PowerShell: `$env:KSA_DLL_DIR="<current-ksa>\dll"; dotnet build purrtty.slnx`.)

A clean build **does not** clear the mod — it means only that directly-referenced members
still line up. Harmony targets (§1), reflection (§3), asset ids (§2), and every behavioral
assumption still need the source diff. Tests are backend/logic only (they mock/avoid the
engine), so they exercise almost none of the KSA coupling.

## Severity triage

- **Critical** — a **required** Harmony patch (`Patch01`, `Patch03`) target drifted, or the
  input chain reordered: core typing/gating breaks for every user.
- **High** — render-pipeline coupling (`RenderTranslucencyPassPatch`, `SharedQuadResource`,
  `OffscreenRenderTarget`, camera conventions) or the ImGui backend changed: in-world
  terminals break or misrender.
- **Medium** — an **optional** patch (`Patch02`, `ConsoleWindowPrintPatch`) or an asset id
  drifted: a feature (fallback menu, game-console capture) silently degrades but the mod
  stays usable.
- **Low** — a build ref rename with a mechanical fix; a reflection probe (`KeySuper`) whose
  absence is already null-tolerant.

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

Per the repo's Instruction Maintenance Mandate: when purrTTY's coupling surface changes
(a new Harmony patch, a new game-state read, a new render-injection point, a dropped one),
update this catalog in the same work item. The catalog is only useful if it stays an
exhaustive mirror of the real coupling — a missed touchpoint here is a silent breakage at
the next KSA bump. Grep signals to re-derive the surface: `HarmonyPatch`, `ModLibrary.`,
`Program.`, `Universe.`, `Cursor.`, `GameSettings.`, `Renderer`/`Vk*` in `InWorld/`,
`ImGuiBackend`, `<Reference Include=` in `.csproj`, and the StarMap attributes in
`TerminalMod.cs`.
