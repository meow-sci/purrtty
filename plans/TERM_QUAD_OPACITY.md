# TERM_QUAD_OPACITY — Faithful per-pixel transparency for in-world terminal quads

> Status: **IMPLEMENTED** (uncommitted, pending in-game test). Phases 1–3 + 5 landed; Phase 4
> (depth-test-no-write) folded into Phase 1's pipeline. Builds clean (0 warnings, warnings-as-errors)
> and all 356 tests pass; mod deployed to the KSA user mods dir. Phase-by-phase, each phase builds
> clean and is in-game verifiable before proceeding, matching the cadence of
> [GAME_SPACE_QUAD_PLAN.md](GAME_SPACE_QUAD_PLAN.md) and
> [TERMINAL_MANAGEMENT_REFACTOR_PLAN.md](TERMINAL_MANAGEMENT_REFACTOR_PLAN.md).

## 0. Goal & locked decisions

Make the in-world (render-to-texture) terminal quad **honor the theme's opacity** so the 3D
world shows through it, exactly the way a 2D ImGui terminal window is translucent over the game.
Today every in-world quad is fully **opaque**.

**Decisions confirmed with the user:**

| Decision | Choice |
|---|---|
| Opacity source | **Reuse the existing theme opacities.** No new theme field. The three opacities (`BackgroundOpacity` / `ForegroundOpacity` / `CellBackgroundOpacity`) the theme bundle already carries drive the quad's per-pixel transparency. (`BackgroundOpacity` is the dominant "how see-through is the whole panel" knob, matching its 2D ImGui meaning.) |
| Faithfulness | **Per-pixel, faithful — now.** Not a uniform whole-quad fade. Text stays crisp while the background bleeds through, identical to a 2D window. We reuse the existing, stable three-opacity render path rather than re-deriving opacity on the quad. |

**Why this is (nearly) free — the key discovery.** The in-world terminal already renders through
the *same* `FrameGridRenderer` as 2D windows, and that renderer **already multiplies each drawn
color's alpha channel** by the opacities (`FrameGridRenderer.ToU32(color, opacity)`,
`purrTTY.Display/Ghostty/FrameGridRenderer.cs:42-44`). Today that per-pixel alpha is **computed and
then discarded**, because:

1. The offscreen color attachment is **cleared to opaque** (`alpha = 1`) —
   `PerFrameRenderer.cs:201` clears to `float4(0,0,0,1)`.
2. The terminal background rect is **forced opaque** (`WithAlpha(0xFF)`) —
   `InWorldTerminalRenderer.cs:166`, with the prescient comment *"Opaque — the quad applies its own
   opacity."*
3. The quad's fragment shader **discards the texture alpha and hardcodes `1.0`** — KSA's stock
   `UnlitMesh.frag` does `outColor = vec4(gammaToLinear(tex.xyz), 1.0)`.
4. The quad pipeline has **blending disabled** (`Presets.BlendState.BlendNone`) —
   `SharedQuadResource.cs:146`.

Undo those four and the alpha the renderer already produces flows all the way to the screen. No new
opacity field, no second render path, no change to `FrameGridRenderer` or any 2D code.

---

## 1. Verified current state (file:line)

### 1.1 The quad GPU resources — `purrTTY.GameMod/InWorld/Display/SharedQuadResource.cs`
- Loads KSA's stock `UnlitMeshVert` / `UnlitMeshFrag` by id (`:66-69`). The vertex shader outputs UV
  at `location 0`; the fragment shader samples a combined-image-sampler at `set 0, binding 0` and
  forces alpha `1.0`.
- Pipeline layout: descriptor-set layout + a single **vertex-stage** `mat4` push constant (the MVP)
  — `:88-98`, `:141`. (No fragment push constant; **we won't need to add one** — see §3.)
- Two pipelines differing only in depth state: `PipelineDepthWrite`
  (`ReverseZDepthStencil.DepthTestWrite`, part mode / occluding billboard) and `PipelineNoDepth`
  (`NoDepthTest`, always-on-top billboard) — `:145`, `:152-155`.
- **`ColorBlendState = Presets.BlendState.BlendNone`** — `:146`. This is why the quad is opaque.
- Bound to `Program.OffScreenPass` (the MSAA scene pass), `CullNone`, MSAA matched to the scene —
  `:128-148`.

### 1.2 The offscreen terminal texture
- `OffscreenRenderTarget` color format is **`R8G8B8A8UNorm`** — it already has an 8-bit alpha channel
  (`InWorldTerminalInstance.cs:93-94`). `UNorm` (not `SRGB`) is deliberate: the frag applies
  `gammaToLinear()` itself (`:90-92`).
- `PerFrameRenderer.RecordRenderPass` owns `BeginRenderPass` and the clear values; clears color to
  `float4(0,0,0,1)` (opaque) — `:198-217`.
- `OffscreenImGuiBackend` wraps KSA's `ImGuiBackendVulkanImpl`, which **only records draw commands**
  into our command buffer — it does **not** begin the render pass or touch clear values on the
  `RenderDrawData(drawData, cmd)` path. So the clear is entirely ours to change.

### 1.3 KSA ImGui backend blend (the make-or-break, verified in decomp)
`KSA.ImGuiBackendVulkanImpl.CreatePipeline` (`ksa-game-assemblies/.../decomp/KSA/ImGuiBackendVulkanImpl.cs:1332-1340`)
uses the canonical Dear ImGui blend:

| Field | Value |
|---|---|
| `BlendEnable` | `true` |
| Color | `SrcAlpha` / `OneMinusSrcAlpha`, `Add` |
| **Alpha** | **`One` / `OneMinusSrcAlpha`, `Add`** |
| `ColorWriteMask` | `R \| G \| B \| A` |

The alpha equation `dstA = srcA + dstA·(1-srcA)` is exactly "over" coverage accumulation, and **A is
written**. So clearing the offscreen target to `(0,0,0,0)` and rendering the terminal yields a
texture whose alpha channel is **meaningful coverage**. The blend is hardcoded (no `CreateInfo`
knob), but we don't need to change it — it already does what we want.

**Premultiplied caveat:** because the color blend uses `Src = SrcAlpha` over a black-transparent
clear, the texture's RGB ends up **premultiplied** (`rgb ≈ color·a`). The quad must therefore
composite **premultiplied**, and un-premultiply before the gamma decode, or translucent regions
render too dark. Math worked out in §3.

### 1.4 Opacity plumbing (already wired for in-world)
- `ThemeDefinition` carries `BackgroundOpacity?`, `ForegroundOpacity?`, `CellBackgroundOpacity?`
  (`ThemeDefinition.cs:116-118`); `TerminalWindowSettings` holds the live non-null values
  (`:20-22`, default `1f`); `ApplyThemeOverrides` clamps+applies them (`:79-92`).
- `InWorldTerminalRenderer` holds a `TerminalWindowSettings _settings`; `BuildSettings` seeds the
  three opacities from config (`InWorldTerminalRenderer.cs:272-274`); `ApplyTheme` applies a full
  bundle live (`:245-261`); `DrawContent` already passes `ForegroundOpacity` + `CellBackgroundOpacity`
  into `FrameGridRenderer.Render` (`:208-211`). **`BackgroundOpacity` is the only one currently
  ignored in-world** (the bg rect is forced opaque at `:166`).
- `InWorldTerminalInstance.ApplyTheme` forwards a theme to the renderer (`:142-145`).

**Net:** applying any theme that carries opacities to an in-world terminal already lands the values
in `_settings`. After this plan, they take visible effect. With all three at `1.0` (the default),
the texture is fully opaque and the quad looks **identical to today** — transparency only appears
when a user lowers an opacity.

---

## 2. The faithful result (why per-pixel "just works")

With the four blockers removed, per-pixel alpha falls out of the existing draw order
(`InWorldTerminalRenderer.DrawContent`): **bg rect → cell backgrounds → glyphs/decorations →
cursor**, each drawn at its opacity, accumulating coverage into the texture alpha.

- **All opacities = 1 (default):** bg rect is opaque → texture alpha = 1 everywhere → quad fully
  opaque. **Pixel-identical to today.**
- **`BackgroundOpacity < 1`:** empty/background pixels get `alpha = BackgroundOpacity` → world shows
  through there; glyph pixels stay near-opaque (`fgCov + (1-fgCov)·BackgroundOpacity`) → **text stays
  crisp while the panel goes see-through**, exactly like a 2D window.
- **`ForegroundOpacity < 1`:** dims text toward the cell background *within* the texture (its 2D
  meaning); only reaches the world where the background is itself translucent. Faithful.
- **`CellBackgroundOpacity < 1`:** colored cell backgrounds (e.g. `ls` highlights) become
  translucent per-pixel. Faithful.

This is the user's "get it for free from the work that's already done and stable" — we reuse the
identical `FrameGridRenderer` output and only change how the texture is cleared/sampled/blended.

---

## 3. The premultiplied-alpha math (correctness)

The offscreen ImGui blend writes, over a `(0,0,0,0)` clear, a **premultiplied gamma** texel:
`t.rgb = gammaColor · a`, `t.a = a`.

The scene offscreen pass composites in **linear** space. For a correct premultiplied "over" we want
the fragment to output **linear-premultiplied** color and use a **premultiplied blend** on the quad
pipeline:

- Quad blend attachment (define inline — KSA has no premultiplied preset):
  `BlendEnable = true; SrcColorBlendFactor = One; DstColorBlendFactor = OneMinusSrcAlpha;
   ColorBlendOp = Add; SrcAlphaBlendFactor = One; DstAlphaBlendFactor = OneMinusSrcAlpha;
   AlphaBlendOp = Add; ColorWriteMask = R|G|B|A`.

- Quad fragment shader (un-premultiply → gamma decode → re-premultiply):
  ```glsl
  #version 450
  layout(set = 0, binding = 0) uniform sampler2D uTex;
  layout(location = 0) in  vec2 vUv;
  layout(location = 0) out vec4 oColor;
  void main() {
      vec4 t = texture(uTex, vUv);
      vec3 straight = t.a > 0.0 ? t.rgb / t.a : vec3(0.0); // un-premultiply
      vec3 lin      = pow(straight, vec3(2.2));            // gammaToLinear (matches stock UnlitMesh.frag)
      oColor = vec4(lin * t.a, t.a);                       // linear, premultiplied
  }
  ```
  The un-premultiply (`/ t.a`) is **required**: skipping it makes translucent backgrounds render too
  dark (e.g. a `BackgroundOpacity = 0.5` panel would come out at ~0.21 instead of 0.5 brightness),
  because the gamma `pow(2.2)` would be applied to the already-premultiplied color. The divide is
  cheap and bounded (`t.rgb ≤ t.a`).

**No fragment push constant is needed** — the opacity is entirely baked into the texture's per-pixel
alpha. The quad keeps its existing vertex-stage `mat4` MVP push constant unchanged.

---

## 4. Implementation phases

### Phase 1 — Quad GPU: custom frag + premultiplied blend (`SharedQuadResource.cs`)
1. Keep the stock `UnlitMeshVert` module (ModLibrary-owned, never destroyed).
2. Compile **our** fragment shader at runtime via the engine's bundled shaderc:
   ```csharp
   VkShaderModule fragModule = RenderCore.ShaderModuleUtils.FromString(
       device, FRAG_GLSL_UTF8, VkShaderStageFlags.FragmentBit, options: null,
       debugName: "purrTTY-Quad-Frag"u8);
   ```
   (`ShaderModuleUtils` lives in `Planet.Render.Core.dll` / `RenderCore`, already referenced; the
   `using RenderCore;` is already present in this file. `null` options use the engine's default
   Vk/SPIR-V target, correct for this no-`#include` shader.)
3. Store `fragModule` in a private field; **destroy it** in `DestroyGpu()` and on the constructor's
   `catch` cleanup path (`device.DestroyShaderModule`). Unlike the stock modules, we own it.
4. Replace `ColorBlendState = Presets.BlendState.BlendNone` with a **premultiplied** blend state
   built inline (§3). Keep a single `PtrOwner`/struct so both pipelines share it; dispose if owned.
5. Descriptor-set layout, vertex input, push-constant range, geometry: **unchanged.**

Build check: solution builds; in-game, with default themes (opacity 1) the quads look **exactly as
before** (the premultiplied path with alpha=1 is identical to opaque).

### Phase 2 — Offscreen: transparent clear + background honors `BackgroundOpacity`
1. `PerFrameRenderer.RecordRenderPass` (`:201`): clear color → `float4(0,0,0,0)`.
2. `InWorldTerminalRenderer.DrawContent` (`:164-167`): draw the terminal background rect at
   `BackgroundOpacity` instead of forced opaque, mirroring `TerminalWindow.cs:397-401`:
   ```csharp
   byte a = (byte)Math.Clamp(_settings.BackgroundOpacity * 255f, 0f, 255f);
   var bg = _settings.Colors.Background.WithAlpha(a);
   drawList.AddRectFilled(canvasPos, canvasPos + avail, FrameGridRenderer.ToU32(bg));
   ```
   Update the now-stale `// Opaque — the quad applies its own opacity.` comment.
3. Nothing else changes: `ForegroundOpacity` / `CellBackgroundOpacity` already flow into
   `FrameGridRenderer.Render` (`:208-211`).

Build check: in-game, apply a saved theme with `background_opacity = 0.6` to an in-world terminal →
the panel becomes see-through over the world while text stays readable. Verify default themes are
unchanged.

### Phase 3 — Live opacity editing for in-world terminals
Today the Theme dialog only shows live opacity sliders for 2D `TerminalWindow` targets; in-world
targets get apply-a-saved-theme only (`ThemeDialog.DrawPaletteApply`). To let users *tune* in-world
opacity live (not just via saved themes), add sliders to the in-world manager's **Configure** form
(`InWorldManagerUI.DrawConfigure`, after the Theme row):
1. Three `SliderInt` (0–100 %) for Background / Foreground / Cell-background opacity, reading/writing
   the instance's live settings.
2. Add a thin setter on `InWorldTerminalInstance` → `InWorldTerminalRenderer` to mutate
   `_settings.{Background,Foreground,CellBackground}Opacity` (clamped). No texture rebuild needed —
   the next offscreen frame picks them up. (Foreground/Cell also need no session re-theme; they're
   pure draw-time multipliers.)
3. Session-only, matching in-world's no-persistence rule — these are not saved unless the user
   captures them into a named theme via the Theme dialog (2D path, unchanged).

(Alternative considered: generalize `ThemeDialog.DrawOpacitySection` to in-world targets via
`INamedTerminal`. Rejected for this pass — it would push `TerminalWindowSettings` through the
renderer-neutral interface; the Configure-form sliders are simpler and live next to the other
in-world placement controls.)

### Phase 4 — Depth ordering for the blended part-mode pipeline
A translucent surface should depth-**test** but not depth-**write** (so it doesn't reject other
translucent fragments). Part mode currently uses reverse-Z `DepthTestWrite`. Switch the blended
pipeline to a reverse-Z **depth-test, no-write** state so multiple/overlapping translucent quads
composite correctly and the quad is still occluded by opaque scene geometry in front of it.
- Verify a reverse-Z depth-test-no-write preset exists (`RenderingPresets.ReverseZDepthStencil.*`);
  if only `DepthTestWrite` / `NoDepthTest` exist, construct the no-write variant inline.
- In-game check: a part-anchored translucent quad is still correctly occluded by parts in front and
  draws over parts behind; two overlapping translucent quads both show through.

(Low risk: with opacity = 1 the texture alpha is 1, so this is a no-op for the common case; it only
matters once a quad is actually translucent.)

### Phase 5 — Docs + tests
- `docs/gotchas.md`: new entry — "In-world quad transparency is premultiplied-alpha: the offscreen
  target clears to `(0,0,0,0)`, the terminal bg rect honors `BackgroundOpacity`, the custom quad
  frag un-premultiplies before the gamma decode, and the quad pipeline blends premultiplied
  (`One/OneMinusSrcAlpha`). Don't 'fix' it back to straight alpha — you'll get dark fringes."
- `docs/code-navigation.md`: note the custom quad fragment shader (runtime-compiled GLSL string in
  `SharedQuadResource`) — purrtty's first owned shader module.
- `docs/how-to.md`: "Make an in-world terminal see-through" recipe (lower Background opacity in the
  Theme dialog / In-World Configure form, or apply a theme with `background_opacity < 1`).
- `CLAUDE.md`: the in-world section already mentions "theme"; add that in-world terminals honor the
  theme's opacities via premultiplied-alpha compositing.
- Tests: the existing opacity round-trip is covered by pure-logic theme tests (no ImGui/GPU). No new
  unit test can exercise the GPU blend; this phase is **in-game verification** (the checks listed per
  phase). Keep the suite quiet / no fixed sleeps per the project rules.

---

## 5. Files touched (summary)

| File | Change |
|---|---|
| `purrTTY.GameMod/InWorld/Display/SharedQuadResource.cs` | Runtime-compile a custom frag (un-premultiply → gamma → premultiply); own/destroy it; swap `BlendNone` → inline premultiplied blend. |
| `purrTTY.GameMod/InWorld/PerFrameRenderer.cs` | Offscreen color clear `(0,0,0,1)` → `(0,0,0,0)`. |
| `purrTTY.Display/Ghostty/InWorldTerminalRenderer.cs` | Background rect honors `BackgroundOpacity`; add live opacity setter; update stale comment. |
| `purrTTY.GameMod/InWorld/InWorldTerminalInstance.cs` | Thin pass-through setter for live opacity → renderer. |
| `purrTTY.GameMod/InWorld/UI/InWorldManagerUI.cs` | Three opacity sliders in the Configure form. |
| `SharedQuadResource.cs` (Phase 4) | Blended part-mode pipeline → reverse-Z depth-test-no-write. |
| `docs/*`, `CLAUDE.md` | Documentation updates (Instruction Maintenance Mandate). |

**No changes to:** `ThemeDefinition`, `ThemeTomlFormat`, `TerminalWindowSettings`, `FrameGridRenderer`,
`ThemeCatalog`, any 2D window path, or the theme TOML schema. The feature reuses the existing
opacity bundle end-to-end.

---

## 6. Risks & mitigations

- **Premultiplied fringing / brightness.** Mitigated by the un-premultiply-before-gamma frag (§3). If
  edges still look off, the fallback is to render the offscreen terminal content itself with a
  premultiplied source — not possible here (KSA's ImGui blend is hardcoded), so the frag-side
  un-premultiply is the correct fix and is already specified.
- **Custom shader compile fails at load.** `ShaderModuleUtils.FromString` throws on bad GLSL;
  `SharedQuadResource`'s constructor is already wrapped by the coordinator's try/catch
  (`InWorldTerminalManager.Create` logs and skips the feature cleanly). A compile failure disables
  in-world terminals without crashing the game — acceptable, and caught immediately in dev.
- **Depth-write change alters occlusion feel.** Phase 4 is isolated and a no-op while opaque; verify
  in-game before keeping.
- **Regression to the "wrapped up and working" opaque look.** Guaranteed avoided: with the default
  opacities (all `1.0`) the texture is fully opaque and premultiplied-with-alpha-1 ≡ opaque. Ship
  Phase 1+2 and confirm default quads are pixel-unchanged before exposing the sliders.

## 7. Fallback (only if §3 proves visually unacceptable)

If, in-game, the premultiplied per-pixel result is undesirable, the contingency is a **uniform
whole-quad opacity**: add a fragment-stage push-constant `float opacity`, output
`vec4(gammaToLinear(tex.rgb), opacity)`, straight-alpha (`BlendColorAlpha`) blend, and drive
`opacity` from `BackgroundOpacity` per instance (push it in `InWorldQuad.RecordDraw`, extend the
pipeline-layout push range to the fragment stage). This is simpler but fades text with the
background. The per-pixel approach in §1–§4 is strictly more faithful and is the recommended path;
this section exists only as a documented escape hatch.
