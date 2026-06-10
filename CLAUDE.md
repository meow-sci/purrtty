# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

purrTTY is a terminal emulator mod for the Kitten Space Agency (KSA) game engine.

As of the libghostty-vt migration, purrTTY **no longer ships its own VT emulator**. It delegates
all terminal emulation to **libghostty-vt** — the standalone, conformance-tested VT engine from
[Ghostty](https://github.com/ghostty-org/ghostty) — via a **vendored, owned** C# binding
(`Ghostty.Vt`). purrtty owns a clean three-layer architecture on top of it.

What this project/mod does:
- Runs real shell sessions (ConPTY shells on Windows; an in-game `GameConsoleShell` cross-platform) inside an in-game terminal window.
- Feeds shell output to libghostty-vt and renders the resulting grid through ImGui in KSA.
- Encodes keyboard/mouse/paste input through libghostty-vt's encoders and writes it back to the shell.

## Architecture — three clean layers

```
FRONTEND   purrTTY.Display (ImGui)          — GhosttyTerminalController + FrameGridRenderer
   │  consumes TerminalFrame (OUT) / drives ITerminalSurface (IN) — NO engine types cross this seam
BACKEND    purrTTY.Terminal (headless, renderer-neutral)
   │  ITerminalSurface + TerminalFrame + GhosttyTerminalSurface + OSC sidecar + sessions
BINDING    vendor/Ghostty.Vt (vendored, owned, net10)  — Terminal/RenderState/encoders + purrtty extensions
NATIVE     vendor/Ghostty.Vt/native/{libghostty-vt.dylib | ghostty-vt.dll} (built from pinned ghostty; osx-arm64 + win-x64)
```

The **renderer-neutral seam** is the heart of the design: the backend produces a `TerminalFrame`
snapshot (rows of pre-resolved cells, cursor, scrollbar, colors) and accepts commands/events via
`ITerminalSurface`. **No ImGui/Vulkan/KSA types cross this boundary**, so a future Vulkan/hybrid
frontend is a frontend swap, not a backend rewrite.

### Project dependencies

```
purrTTY.Logging
vendor/Ghostty.Vt              # vendored libghostty-vt binding (+ native lib) — MIT, see its README
purrTTY.CustomShellContract    # ICustomShell, CustomShellRegistry (namespace purrTTY.Core.Terminal)
purrTTY.CustomShells           # GameConsoleShell
purrTTY.Terminal               # BACKEND: surface + sessions + RELOCATED PTY layer (Pty/)
    │  refs vendor/Ghostty.Vt + purrTTY.CustomShellContract + purrTTY.Logging
    └── purrTTY.Terminal.Tests (NUnit integration tests)
purrTTY.Display                # ImGui FRONTEND: refs purrTTY.Terminal + KSA ImGui DLLs
purrTTY.GameMod                # final mod DLL: refs Display + CustomShells; StarMap hooks + Harmony patches
```

> The PTY/process layer was relocated out of the retired `purrTTY.Core` project into
> `purrTTY.Terminal/Pty/` (its types keep the `purrTTY.Core.Terminal` namespace, which is shared
> with `CustomShellContract`). There is no longer a `purrTTY.Core` project.

## Build and Test Commands

```bash
dotnet build purrtty.slnx              # Build the whole solution
dotnet build purrTTY.Terminal          # Backend only
dotnet build purrTTY.GameMod           # Game mod (also deploys to the KSA mods dir, incl. native lib)
```

### Testing

The terminal-emulation behavior is **trusted to libghostty-vt** and is not re-tested. The tests
cover purrtty's **integration** with the engine.

```bash
dotnet test purrTTY.Terminal.Tests/purrTTY.Terminal.Tests.csproj --nologo -v quiet
```

`purrTTY.Terminal.Tests` (NUnit) validates frame production, theming, selection, OSC sidecar,
key/mouse encoding, bracketed paste, DSR replies, scrollback, and the session-wiring data flow.
Keep test output minimal so it does not flood the CLI.

> The legacy emulator test/app projects (`purrTTY.Core.Tests`, `purrTTY.Display.Tests`,
> `purrTTY.Display.Playground`, `purrTTY.TestApp`) were deleted with the emulator.

### Building the native libghostty-vt

P/Invoke needs the **shared library** (`.dylib`/`.dll`), built from pinned ghostty with zig 0.15.
Do **not** vendor the static archive (`ghostty-vt-static.lib`) or the import lib (`ghostty-vt.lib`) —
neither is loadable at runtime. Copy the result into `vendor/Ghostty.Vt/native/`.

```bash
# macOS (osx-arm64) → zig-out/lib/libghostty-vt.dylib
export PATH="/opt/homebrew/opt/zig@0.15/bin:$PATH"
cd /path/to/ghostty && zig build -Demit-lib-vt
```

```powershell
# Windows (win-x64) → zig-out/bin/ghostty-vt.dll   (the .lib files in zig-out/lib are NOT used)
# Use the gnu target (compiles the highway/simdutf C++ SIMD deps) at BASELINE cpu — never -mcpu native:
#   a host-tuned build is non-portable AND was observed to AV inside vt_write. The SIMD libs still
#   runtime-dispatch to AVX2 etc., so baseline costs little.
$env:PATH = "C:\zig-x86_64-windows-0.15.2;$env:PATH"
cd C:\path\to\ghostty ; zig build -Demit-lib-vt -Dtarget=x86_64-windows-gnu -Doptimize=ReleaseFast
```

The native lib is **pinned, not forked** (current pin recorded in `vendor/Ghostty.Vt/README.md`).
osx-arm64 + win-x64 are vendored today (gitignored; built locally); linux-x64 / full multi-RID
packaging is follow-up work.

## Code Navigation Guide

Start here:
- Solution: `purrtty.slnx`
- Shared build config + KSA DLL paths (per-OS): `Directory.Build.props`
- Migration plan + status: `LIBGHOSTTY_ANALYSIS.md`; provenance/licensing: `vendor/Ghostty.Vt/README.md`, `THIRD-PARTY-NOTICES.md`

Vendored binding (`vendor/Ghostty.Vt/`):
- Engine surface: `src/Terminal.cs`, `src/RenderState.cs`, `src/TerminalOptions.cs`, encoders (`src/KeyEncoder.cs`, `src/MouseEncoder.cs`)
- Native P/Invoke: `src/Native/NativeMethods.cs` (+ `NativeMethods.Selection.cs`)
- Native loader (KSA ALC): `src/Native/NativeLibraryResolver.cs` (ModuleInitializer + `SetDllImportResolver`)
- purrtty additions: `src/Terminal.Selection.cs` (selection + default cursor style/blink), `MaxScrollback` in `TerminalOptions.cs`, per-row `RowSelection` in `RenderState.cs`, and the **render-hot frame read path** in `src/RenderState.FrameReader.cs`: `RenderFrameReader` (forward-only row/cell reader — ~2 native calls per cell, 3 for styled cells, one reused cells handle per frame), `RawCell` (managed bit-decode of the packed `page.Cell` u64: content tag / codepoint / style_id / wide / content-bg — replaces per-field `ghostty_cell_get` round-trips), `RenderState.ClearDirty()` + `RenderFrameReader.ClearRowDirty()` (the engine only ever RAISES dirty flags — the consumer must clear them after each frame or `Dirty` reads Full forever), UTF-8 grapheme-cluster reads into caller buffers, and `RawCellLayout.Validate()` — a runtime cross-check of the managed decode against `ghostty_cell_get` so a native pin bump that changes the bit layout fails loudly (run once per process by `GhosttyTerminalSurface`, and as the `RawCellLayout_MatchesNativeAccessors` test). The older `RenderStateRowEnumerator`/`RenderStateCellEnumerator` remain but are off the render path; use `GridRef.GetCell()` for a fully-populated cell.

Backend (`purrTTY.Terminal/`):
- Seam contract: `ITerminalSurface.cs`; frame value types: `Rendering/` (`TerminalFrame`, `FrameRow`, `FrameCell`, `RgbaColor`, `CellFlags`/`UnderlineStyle`/`CellWidth`/`CursorShape`)
- Engine wrapper: `Ghostty/GhosttyTerminalSurface.cs` (single-threads native; theme push; key/mouse encode; drag selection via `BeginSelectCells`/`ExtendSelectCells` with a content-pinned `GridRef` anchor that survives viewport scroll; **dirty-aware frame production** — see gotcha 12 — with cell fg/bg resolved managed-side from style + palette in `FillCell`, a per-surface `GraphemeCache` interning cell strings so steady-state rebuilds allocate nothing, DEC 2026 synchronized-output gating — gotcha 13 — and `LastFrameStats` for the perf HUD)
- OSC 52 clipboard / OSC 1 icon: `Ghostty/OscSidecar.cs` (managed tee of the output stream)
- Neutral input types: `Input/` (`TerminalKey`, `TerminalKeyEvent`, `TerminalMouseEvent`, `GridPoint`, `KeyModifiers`)
- Sessions: `Sessions/` (`TerminalSession`, `SessionManager`, `TerminalSessionFactory` — the construction seam)

Frontend (`purrTTY.Display/`):
- Multi-window controller: `Ghostty/GhosttyTerminalController.cs` (implements `Controllers/ITerminalController.cs`;
  manages a list of `TerminalWindow`s, routes game-menu actions to the focused window, persists
  display defaults + first-window geometry through a single shared `ThemeConfiguration` instance)
- Per-window terminal: `Ghostty/TerminalWindow.cs` (owns a `SessionManager` whose sessions are tabs —
  tab bar hidden with one tab; per-window theme/font/opacity via `TerminalWindowSettings`; chrome hiding:
  transparent WindowBg/MenuBarBg + zero border + `Alpha=0` menu/tab strips when the mouse is not over the
  window, even while focused; menu-bar strip is the drag handle (no title bar) and an `InvisibleButton`
  over the grid keeps drag-selection from moving the window; Ctrl+Shift+C/V copy/paste; grid snap:
  when an interactive resize ends, `TrackResizeSnap` shrinks the window by the fractional-cell
  remainder so the grid exactly fills the content region — chrome is *measured* as
  `windowSize - avail`, not computed from style metrics, and a resize is recognized only as
  size-change-while-LMB-held with the snap firing on release, so the snap's own
  `SetNextWindowSize` can never re-trigger detection (no timers needed))
- Theming: `Theming/` (`ThemeDefinition`/`ThemeColors` + `ToEngineTheme()`, `ThemeTomlFormat` (Tomlyn DOM
  read/write), `ThemeCatalog` — code-built "Default" + bundled `TerminalThemes/*.toml` beside the
  assemblies + user themes in `<config>/.purrTTY/themes/`). Theme TOML = alacritty-style `[colors.*]`
  sections; user-saved themes also carry `[font]` (family/size) and `[window]` (3 opacities) — those
  fields are optional and "keep current" when absent (bundled themes are colors-only).
- Grid drawing: `Ghostty/FrameGridRenderer.cs` (bold/italic/bold-italic variant via
  `Ghostty/FrameFonts.cs`; takes foreground/cell-background opacity multipliers). Submission is
  **run-batched**: consecutive same-color backgrounds merge into one `AddRectFilled`; consecutive
  same-font/same-color glyphs merge into one `AddText` from a reusable UTF-8 scratch (blank cells
  inside a run are bridged with spaces). Batching is metric-gated: `TerminalWindow` measures each
  variant's printable-ASCII advance against the cell width once per (family, size) — see
  `IsAsciiMonospace` — and non-ASCII glyphs batch only after their individual advance is validated
  by `GlyphBatchCache` (per-codepoint, cached). Anything unvalidated falls back to per-cell
  `AddText`, which guarantees grid alignment. **Block Elements (U+2580–U+259F) never go through the
  font at all**: they are drawn as exact rects (half/eighth blocks, quadrants; ░▒▓ as fg-alpha
  fills), merged into horizontal strips — pixel-perfect coverage with no glyph-hinting seams, and
  half-block "pixel" apps (doom, chafa) collapse into color-run strips. The decoration pass is
  skipped for rows whose `FrameRow.HasDecorations` is false (computed by the backend). `Render`
  returns `GridRenderStats` (draw-call counts) for the perf HUD.
- Session-manager factory: `Ghostty/GhosttySessionManagerFactory.cs` (one `SessionManager` per window via
  `CreateSessionManager(config)`; `EnsureGameShellsDiscovered()` runs custom-shell discovery once)
- Reused chrome: `Rendering/PurrTTYFontManager.cs` (font family registry → per-window `FrameFonts`),
  `Configuration/` (fonts, `ThemeConfiguration` = global defaults/hotkey/geometry, shell config)

PTY/process (`purrTTY.Terminal/Pty/`, namespace `purrTTY.Core.Terminal`):
- ConPTY host: `Process/*`, `ProcessManager.cs`; interface: `IProcessManager.cs`
- Custom-shell adapter: `CustomShellPtyBridge.cs`; launch options: `ProcessLaunchOptions.cs`
- Session/process event args + `SessionState`: `SessionEventArgs.cs`, `ProcessEventArgs.cs`

Game/integration (`purrTTY.GameMod/`):
- Lifecycle + toggle + game menus: `TerminalMod.cs`. The full menu content lives in
  `TerminalMod.DrawMenuContent()` and is registered two ways with identical content: via the
  `[ModMenuEntry("purrTTY")]` attribute when the ModMenu companion mod is present, and via the
  `Patcher.cs` `DrawMenuBar` transpiler fallback otherwise. Menus: Toggle Terminal / Toggle Hotkey,
  New Tab / New Window (shell submenus filtered by `ShellAvailabilityChecker`, WSL distros via
  `WslDistributionDetector` — prewarmed at init; Game Console always offered), Theme (built-in +
  saved lists, Save Current As... modal with name input, Refresh), Font (size slider + family list),
  Window (hide-chrome + performance-HUD checkboxes + 3 opacity sliders). Menu actions target `controller.FocusTarget`.
- Bundled assets: `TerminalThemes/*.toml` (18 color schemes) + `TerminalFonts/*.iamttf`, both copied
  to the build output and the deployed mod dir by the csproj.
- Harmony patches: `Patcher.cs` (gates `KSA.Program.OnKey` via `GhosttyTerminalController.IsAnyTerminalActive`;
  `Patch03_HotkeyGuard` blocks `GameSettings.OnKeyAll` while `ImGui.GetIO().WantTextInput` is set so typing
  in mod text fields never fires game hotkeys), `Patches/ConsoleWindowPrintPatch.cs` (captures game-console output; targets the Brutal sink `ConsoleWindow.Print(ReadOnlySpan<char>, ImColor8, int)` → `GameConsoleShell.OnConsolePrint` — **Brutal-version-sensitive**: an older API used `Print(string, uint, int, ConsoleLineType)`)

Custom shells:
- Contract: `purrTTY.CustomShellContract/` (`ICustomShell`, `CustomShellRegistry`, `BaseLineBufferedShell`)
- Built-in game shell: `purrTTY.CustomShells/GameConsoleShell.cs` (emits VT bytes → `Surface.Write`)

## Key behaviors & gotchas

1. **Single-threaded native access.** `ITerminalSurface.Write` is the only thread-safe entrypoint
   (it enqueues PTY bytes). The engine is mutated only on the frontend tick inside `BuildFrame()`.
   All other surface members must be called on the tick thread.

2. **Data flow per session** (`Sessions/TerminalSession`):
   PTY output → `ProcessManager.DataReceived` → `Surface.Write`; engine replies → `Surface.PtyReply`
   → `ProcessManager.Write`; user input → frontend `Surface.EncodeKey`/`EncodeMouse` → `session.SendInput`.

3. **Encoders return owned bytes.** `KeyEncoder.Encode`/`MouseEncoder.Encode` copy the native
   result into a `byte[]` before returning. (Returning a span into their `stackalloc` scratch was a
   use-after-scope: the caller read clobbered stack memory — `0x00` on macOS/arm64, `0xB0` on win-x64
   — which had been misdiagnosed as a "first-use NUL" libghostty quirk and worked around with a
   re-encode self-heal. Both the bug and the workaround are gone.) Named keys encode from `Key`
   alone; `Text` is only for printable input.

4. **Pre-resolved colors.** Push the theme via `Surface.SetTheme` so `TerminalFrame` cells carry
   final RGB; the frontend draws them directly (no SGR resolution in the frontend). Resolution
   happens in `GhosttyTerminalSurface.FillCell` from the engine style + palette, mirroring the
   native rules exactly: a `bg_color_*` content tag wins over the style bg (a blank cell erased
   under a background carries its bg in the content tag while reporting no styling — how htop
   paints rows); style palette/RGB colors otherwise; theme defaults as fallback; no bold-is-bright
   promotion (the native C path passes no bold option either). **Reverse video (SGR 7) is resolved
   in `FillCell`** by swapping the resolved fg/bg: the frontend never swaps. (The `Inverse` flag is
   still surfaced as metadata.)

5. **Custom shell discovery** is reflection-based; `GhosttySessionManagerFactory.EnsureGameShellsDiscovered`
   forces the `purrTTY.CustomShells` assembly to load before discovery (do not remove without replacing it).
   It runs once at mod init so Game Console sessions can be launched from the menus at any time.

6. **One `ThemeConfiguration` instance.** The controller owns the loaded config; every writer
   (display defaults, hotkey, window geometry) must mutate **that** instance before `Save()`, or a
   later save from another path silently reverts fields (the hotkey modal reads it via
   `controller.Configuration` for exactly this reason).

7. **Per-window display state lives in `TerminalWindowSettings`,** not in the config: the config only
   stores the *defaults for new windows* (last applied theme/font/opacities) plus the first window's
   geometry. "Save Current As..." snapshots the focused window's settings into a user theme TOML.

8. **Chrome hiding must keep the layout stable.** While hidden, the menu strip and tab bar are still
   submitted (alpha 0 / transparent style colors) so the grid size — and therefore the PTY size —
   does not change when chrome fades in on hover. Only style colors/alpha may differ between the
   hidden and shown states, never layout.

9. **Mouse needs a canvas `InvisibleButton`.** `TerminalWindow.Render` reserves the grid rect with
   `ImGui.InvisibleButton("##grid", ...)` over the painted frame. The window is title-bar-less, so
   without an item under the cursor ImGui treats a body click-drag as a **window move** — text
   selection silently breaks and the window slides instead. The button also supplies the
   `IsItemHovered` (`gridHovered`) state the mouse handlers gate on; the menu-bar strip stays the
   drag handle. Input is dispatched while the window holds ImGui focus. The grid is painted via the
   draw list, so the button stays invisible.

10. **Neutral `MouseButton` is remapped, not cast.** The renderer-neutral `MouseButton`
   (Left=0/Middle=1/Right=2) is **not** libghostty's order (Left=1/Right=2/Middle=3; scroll = 4/5).
   `GhosttyTerminalSurface.EncodeMouse` translates via `ToNativeButton` — a straight `(int)` cast
   mis-sends every button (Left→UNKNOWN, Middle→Left). App-mouse coordinates are **surface-local**
   (mouse − canvas), not screen-global, since the engine's encoder maps pixels→cells itself.

11. **App-mouse motion is reported live, gated on cell change, and filtered by the engine.**
   `HandleAppMouse` sends a `MouseAction.Motion` event (with the held button from `HeldMouseButton`,
   or `None` for hover) whenever the pointer crosses into a new grid cell — so drags update live in
   nvim/tmux instead of only on release. It always *offers* the motion; the libghostty mouse encoder
   is **mode-aware** and emits a report only when the active mode wants it (button-event 1002 needs a
   held button; any-event 1003 reports hover too; normal 1000 drops all motion), returning 0 bytes
   otherwise. Reporting on **cell change**, not per pixel, matches xterm/ghostty granularity and keeps
   the PTY from flooding.

12. **Engine dirty flags are consume-and-clear; the frame rebuild honors them.** ghostty's
   `RenderState` raises `Dirty` (False/Partial/Full) plus per-row dirty flags on `update()` and
   **never lowers them** — `GhosttyTerminalSurface.PopulateFrame` clears both after consuming
   (binding additions `ClearDirty`/`ClearRowDirty`); skip the clears and every tick rebuilds the
   whole grid forever (the pre-optimization behavior). Per tick: `False` → no cell reads at all,
   `Partial` → only dirty rows are refilled (clean rows keep their cached `FrameRow` contents —
   safe because anything that shifts row identity — viewport scroll, resize, screen switch,
   selection or palette change — yields `Full` from the engine), `Full`/`_pendingChange` → full
   rebuild including colors. Cursor + scrollbar are read every tick (not covered by row dirt) and
   compared, so `TerminalFrame.Generation`/`FrameChanged` move **only on real changes**. Don't set
   `_pendingChange` when feeding PTY bytes — the engine decides whether they changed anything.

13. **Synchronized output (DEC 2026) gates the frame, with a 1s safety timeout.** While an app has
   mode 2026 set (batching a redraw), `BuildFrame` keeps feeding PTY bytes to the engine but skips
   the render-state update + frame populate, so the previous *complete* frame stays on screen — no
   mid-redraw tearing for apps that use it. If the mode stays set past `SyncOutputTimeout` (1s),
   frames render live until the app clears it (a stuck app cannot freeze the terminal).

14. **The ConPTY output pump must never sleep.** `ConPtyOutputPump` runs a blocking `ReadFile` loop
   on a dedicated long-running thread with a 64 KB buffer; the pipe read itself blocks until data
   arrives, so there is no loop to throttle. (A `Task.Delay(1)` after every 4 KB read previously
   capped throughput at single-digit MB/s, which slowed fast TUIs *and* smeared their full-screen
   redraws across many render ticks — the primary cause of visible tearing.) Teardown unblocks the
   read by closing the pipe handle (ERROR_BROKEN_PIPE/ERROR_INVALID_HANDLE exit the loop quietly).

15. **Perf HUD for render diagnostics.** Window menu → "Show performance HUD"
   (`TerminalWindow.ShowPerfHud`, all windows) overlays per-tick numbers: engine write / update /
   populate ms (`GhosttyTerminalSurface.LastFrameStats`), ImGui submit ms, dirty state
   (clean/partial/full/sync-hold), PTY MB/s, and the renderer's draw-call breakdown
   (`GridRenderStats`). Use it before optimizing anything further.

### Changing terminal/rendering behavior
- Frame production / cell mapping: `purrTTY.Terminal/Ghostty/GhosttyTerminalSurface.cs`
- Drawing: `purrTTY.Display/Ghostty/FrameGridRenderer.cs`
- Window/tab/chrome/input behavior: `purrTTY.Display/Ghostty/TerminalWindow.cs`
- Add a backend integration test in `purrTTY.Terminal.Tests` and run it.

### Adding or changing themes
- Bundled color schemes: drop an alacritty-style TOML into `purrTTY.GameMod/TerminalThemes/`.
- Format/parsing: `purrTTY.Display/Theming/ThemeTomlFormat.cs`; discovery: `ThemeCatalog.cs`.
- User themes are written to `<config>/.purrTTY/themes/` by the Save Current As... menu action.

### Extending the engine binding
1. Add the native P/Invoke in `vendor/Ghostty.Vt/src/Native/` (verify the symbol exists in the dylib with `nm`).
2. Wrap it in the relevant `Ghostty.Vt` class (mark purrtty additions clearly).
3. Surface it through `ITerminalSurface`/`TerminalFrame` if the frontend needs it.

### Adding a custom shell
1. Implement `ICustomShell` (or inherit `BaseLineBufferedShell`) in `purrTTY.CustomShells`.
2. Ensure metadata + a stable shell ID; verify discovery via `CustomShellRegistry`.

### Deploying the game mod
1. `dotnet build purrTTY.GameMod` — copies the mod DLLs **and the native libghostty-vt** to the mods dir.
2. Launch KSA; toggle the terminal with the configured hotkey (default F12).

## Code Standards (from Directory.Build.props)
- .NET 10 / C# 13; nullable enabled; warnings-as-errors (except CS1591).
- The vendored `Ghostty.Vt` and the backend value types relax CS1591/warnings-as-errors in their csproj.

## Instruction Maintenance Mandate (MUST)

Whenever you make meaningful repository changes, you MUST evaluate and update this file in the same
work item if it affects: project structure/dependencies, the backend/frontend seam, the engine
binding surface, build/test/deploy commands, or feature status. Remove defunct guidance immediately;
prefer verified code paths over plans when documenting behavior; keep navigation pointers current.
Do not document the deleted bespoke emulator as if it still exists.
