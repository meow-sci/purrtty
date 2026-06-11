# instructions

use the ksa, harmony, imgui, tomlyn skills as needed to properly implement the plan outlined in @LIBGHOSTTY_ANALYSIS.md

the goal of this plan is to completely replace our bespoke headless terminal emulator with libghostty-vt

the dirs for the required referenced projects are found at:

- ghostty: /Users/asherwin/repos/github/ghostty
- libghostty-vt-dotnet: /Users/asherwin/repos/github/libghostty-vt-dotnet

expertly implement this plan to fully realize our new purrtty ksa game mod fully powered by libghostty-vt

it's OK to pre-build libghostty-vt and copy the statically linked library file into some local csproj folder as reference to be included in the output.

later we'll work on multi-os support patterns for that but for now just the default/current macOS host platform is fine



# purrtty → libghostty-vt: Migration Analysis & Plan

> Status: proposal / implementation-ready spec · Date: 2026-06-07
> Direction: **no compromises.** Delete purrtty's custom VT emulator; **vendor `libghostty-vt-dotnet` into our solution** as our own csproj(s); fix/extend every gap at the source; and refactor purrtty into a **clean 3-layer separation** — headless libghostty backend ⟷ renderer-neutral seam ⟷ ImGui frontend — so a Vulkan or hybrid frontend can be dropped in later with no backend changes.
> Companion docs (frontend options): [`LIBGHOSTTY_VULKAN_FEASIBILITY.md`](./LIBGHOSTTY_VULKAN_FEASIBILITY.md) · [`LIBGHOSTTY_VULKAN_PLAN.md`](./LIBGHOSTTY_VULKAN_PLAN.md)

## NOTES ON building libghostty-vt

- sources for ghostty are at path `/Users/asherwin/repos/github/ghostty`
- to build libghostty-vt for the current platform (macOS) do this (note the default zig is 0.16 but ghostty needs 0.15):
  ```bash
  export PATH="/opt/homebrew/opt/zig@0.15/bin:$PATH"
  zig build -Demit-lib-vt
  ```

  the output is found at `/Users/asherwin/repos/github/ghostty/zig-out/lib/libghostty-vt.0.1.0.dylib`

## NOTES on libghostty-vt-dotnet sources

The latest sources for the project are at path `/Users/asherwin/repos/github/libghostty-vt-dotnet`

Copy the sources from here as needed into new csproj's under our solution

Make a note in the csproj folder for this about where it came from in a README.md under that and mention it was MIT licensed (copy the MIT license file from `/Users/asherwin/repos/github/libghostty-vt-dotnet/LICENSE`)

## NOTES on building

This mod is actually meant to run on Windows but since it's all dotnet and cross system compatible stuff like with libghostty-vt, it's OK to work on it on macOS (which the current host system is)

You can just `dotnet build` the purrty project and it works now on macOS (there is a copy of the KSA game DLLs already referenced properly in @Directory.Build.props based on os)

You can build libghostty-vt for the current platform (macOS) and link against it for tests etc.

## NOTES on testing

The existing test suite is large, huge and takes a long time with huge amounts of text output.

We need an ALL NEW test suite since libghostty-vt is a complete wholesale backend replacement.

- You MUST be more methodical in the test suite setup to have minimal output when run from the CLI so it doesnt poison ai context windows
- You DO NOT need to test terminal emulator behavior itself (you MUST trust libghostty-vt implementations inherently), we want tests which are covering our integration with libghostty-vt is working as expected



---

## 1. Executive summary

purrtty ships its **own** ~26K-LOC headless VT emulator (`purrTTY.Core`: a hand-written ANSI/VT parser, 39 "EmulatorOps", ~18 managers, screen/scrollback buffers, charset tables, UTF-8 decoder). It is clean but is a perpetual maintenance burden. **libghostty-vt** is the standalone, conformance-tested VT engine from [Ghostty](https://github.com/ghostty-org/ghostty); **`libghostty-vt-dotnet`** is an MIT C# binding (`Ghostty.Vt`).

**Plan:** delete purrtty's emulation and delegate to libghostty-vt, and — because we will not accept the binding's current gaps — **vendor the binding's source into our solution and own it**. We then refactor purrtty into three clean layers:

1. **`Ghostty.Vt` (vendored, owned)** — the raw libghostty-vt surface in C#. We extend it: selection, configurable scrollback, default cursor style/blink, batched cell reads, idiomatic cleanup.
2. **Backend (`purrTTY.Terminal`, headless, renderer-neutral)** — combines the vendored engine + PTY/process + sessions, and exposes a **renderer-neutral contract**: a `TerminalFrame` snapshot (what to draw) + a command/event sink (write bytes, resize, scroll, selection gestures, theme). **Zero ImGui or Vulkan types cross this boundary.**
3. **Frontend(s)** — the ImGui frontend (now) consumes only the backend contract. A future **Vulkan / hybrid** frontend implements the same consumption — no backend changes.

**Why this is now low-risk:** every former "gap" is fixable in the vendored **C# binding** without forking the C library:
- selection is first-class in the C API (`GHOSTTY_TERMINAL_OPT_SELECTION`, `selection.h` gesture/format, per-row selection in `RenderState`) — just unwrapped;
- `max_scrollback` is already a field on the options struct — just hardcoded to 1000 in the binding;
- `OPT_DEFAULT_CURSOR_STYLE`/`BLINK` exist for engine-level cursor styling;
- a **batch cell read** (`ghostty_render_state_row_cells_get_multi`) already exists, so per-cell P/Invoke chatter is a binding implementation detail we control;
- OSC 52 clipboard / OSC 1 icon are not C callbacks, handled by a clean **sidecar `OscParser`** on the PTY tee (pure managed).

The native library is built from **pinned upstream `ghostty-org/ghostty`** — we **pin, not fork, the C library**; we own only the managed binding.

**Net effect:** delete ~20K LOC of emulation, gain a best-in-class engine, and land a clean backend/frontend split that makes the Vulkan work a frontend swap rather than a rewrite. The real remaining costs are operational (native-lib loading in KSA's plugin ALC, native build/CI, single-threading the engine) — addressed in §8–§9.

---

## 2. Goals & non-goals

**Goals**
- Remove purrtty's custom emulation; delegate to libghostty-vt.
- **Vendor & own** the C# binding; fix every gap at the source; keep an **extremely clean, fully-featured** headless backend.
- Establish a **renderer-neutral seam** so frontends (ImGui now, Vulkan/hybrid later) are swappable with no backend changes.
- Refactor purrtty's projects freely to land the cleanest structure.
- Preserve features: multi-session, scrollback + scrollbar, resize/reflow, colors/themes, selection + copy/paste, cursor styles, alt-screen apps, bracketed paste, the in-game `GameConsoleShell`.

**Non-goals (this doc)**
- Building the Vulkan renderer (separate companion docs).
- Forking the **C library** (ghostty/Zig). We pin upstream; all our changes live in the vendored C# binding + a managed OSC sidecar.
- A cross-platform OS-shell PTY (purrtty's PTY is ConPTY/Windows-only; orthogonal — libghostty provides no PTY).

---

## 3. Target architecture — three clean layers

```
┌──────────────────────────── FRONTEND  (swappable) ─────────────────────────────┐
│  purrTTY.Display (ImGui)   │   later: purrTTY.Display.Vulkan / hybrid            │
│  • window/tabs/menus/settings/fonts (chrome)                                     │
│  • input capture: ImGui events → neutral KeyEvent/MouseEvent/SelectionGesture    │
│  • draw: consume TerminalFrame → ImGui draw-lists (or Vulkan instances)          │
└───────────────▲───────────────────────────────────────────────┬────────────────┘
   reads (OUT)  │  TerminalFrame snapshot                        │  commands/events (IN)
   rows·cells·  │  (renderer-neutral value types — NO ImGui/Vk)  │  Write/Resize/ScrollBy/
   cursor·sel·  │                                                │  Selection*/SetTheme/
   scrollbar    │                                                │  EncodeKey/EncodeMouse
┌───────────────┴──────────────── BACKEND  (headless, neutral) ──▼────────────────┐
│  purrTTY.Terminal                                                                │
│  • ITerminalSurface: command sink + TerminalFrame producer                       │
│  • GhosttyTerminalSurface: wraps Ghostty.Vt.Terminal + RenderState + selection   │
│  • OSC sidecar (OSC 52 clipboard / OSC 1 icon) via Ghostty.Vt.OscParser tee      │
│  • PTY/process (ConPTY + CustomShellPtyBridge) · SessionManager/TerminalSession  │
└───────────────▲──────────────────────────────────────────────┬─────────────────┘
   PTY bytes IN │                                               │ OnWritePty OUT → PTY
┌───────────────┴──────────── VENDORED BINDING  (owned) ────────▼─────────────────┐
│  Ghostty.Vt  (our fork, in-solution; MIT-attributed; retargeted net10)           │
│  Terminal · RenderState · KeyEncoder · MouseEncoder · OscParser · Formatter       │
│  + OUR ADDITIONS: Selection (gesture/format/per-row) · max_scrollback ·           │
│    default cursor style/blink · batched cell reads (get_multi)                     │
│  native libghostty-vt.* built from PINNED ghostty-org/ghostty (not forked)         │
└──────────────────────────────────────────────────────────────────────────────────┘
```

### 3.1 The renderer-neutral seam (the heart of the design)

**OUT — `TerminalFrame`** (pure purrtty value types; no `Ghostty.Vt`/ImGui/Vulkan):
- `int Cols, Rows`, `long Generation` (bump on change for skip-render)
- `IReadOnlyList<FrameRow>` where each `FrameRow` = cells `{ string? Grapheme, RgbaColor Fg, RgbaColor Bg, CellFlags Flags /*bold,italic,underline:3,blink,inverse,strike,faint,invisible,overline*/, CellWidth Width /*narrow,wide,spacer*/ }`, plus optional `SelectionRange { int StartCol, EndCol }`
- `CursorState { int X, Y; bool Visible; CursorShape Shape; bool Blinking }`
- `Scrollbar { int Offset, ViewportHeight, ScrollbackHeight }`
- `FrameColors { RgbaColor DefaultFg, DefaultBg, Cursor; RgbaColor[16/256] Palette }` (for fallback)

**IN — `ITerminalSurface`** (commands + neutral events):
- `void Write(ReadOnlySpan<byte>)` · `void Resize(int cols,int rows,int cellPxW,int cellPxH)`
- `void ScrollBy(int delta)` · `ScrollToTop()` · `ScrollToBottom()`
- selection: `BeginSelect(GridPoint, SelectMode)` · `ExtendSelect(GridPoint)` · `EndSelect()` · `SelectWord/Line/All(GridPoint)` · `ClearSelection()` · `string GetSelectionText()`
- theme: `SetTheme(palette, fg, bg, cursor)` · `SetCursorStyle(shape,blink)`
- input encoding (engine owns it, knows the modes): `int EncodeKey(in KeyEvent, Span<byte>)` · `int EncodeMouse(in MouseEvent, Span<byte>)`
- events: `WriteToPty(bytes)` (replies), `Bell`, `TitleChanged`, `IconNameChanged`, `ClipboardRequest`, `FrameChanged`

The frontend never touches `Ghostty.Vt` or terminal logic — it forwards neutral input and draws `TerminalFrame`. This is what makes ImGui↔Vulkan a pure frontend swap.

### 3.2 Bidirectional I/O (how state round-trips through libghostty)

| Concern | IN (frontend → backend → libghostty) | OUT (libghostty → backend → frontend) |
|---|---|---|
| Grid/text | PTY bytes → `VTWrite` | `RenderState` → `TerminalFrame` rows (grapheme + resolved fg/bg + flags) |
| Cursor | (stream) / `SetCursorStyle` (`OPT_DEFAULT_CURSOR_STYLE/BLINK`) | `RenderState.CursorViewport*` → `CursorState` |
| Scrollback | wheel/keys → `ScrollViewportBy/To*` | `Terminal.Scrollbar`; `RenderState.Rows` already = viewport |
| Selection | mouse → gesture (`ghostty_selection_gesture_event`) / `select_word/line/all` | per-row `ROW_DATA_SELECTION` → highlight; `selection_format_alloc` → clipboard text |
| Color/theme | theme → `SetColorPalette` + `Set{Fg,Bg,Cursor}Color` | pre-resolved `FgColor`/`BgColor` per cell |
| Input | neutral KeyEvent/MouseEvent → `EncodeKey/Mouse` (libghostty `KeyEncoder`/`MouseEncoder`) → bytes → `VTWrite` | — |
| Title/Bell/replies/OSC52/icon | — | `OnWritePty`→PTY, `OnBell`, `OnTitleChanged`; OSC52/icon via sidecar parser |

---

## 4. Vendoring `libghostty-vt-dotnet`

- **What we bring in:** the managed binding `src/Ghostty.Vt` (and its tests) into our solution as `Ghostty.Vt.csproj` (+ optionally `Ghostty.Vt.Tests`). License is **MIT** — keep the upstream `LICENSE`/copyright and add attribution (e.g. `THIRD-PARTY-NOTICES`).
- **Retarget:** binding is `net9.0`; retarget the vendored copy to **`net10.0`** to match purrtty/KSA (purrtty already builds net10). Drop the binding's `global.json` SDK pin (9.0.203) in favor of purrtty's.
- **Native library:** keep building `libghostty-vt.{dll,so,dylib}` from **pinned upstream `ghostty-org/ghostty`** (current pin: `main` @ `7092b394…`, 1.3.2-dev). We own the build/CI (`build/build-native.ps1` + Zig) and the per-RID artifacts, but **do not fork the C source**. If a future need truly requires a C-level change, that becomes a separate, explicit decision — the §5 work needs none.
- **Native loading:** add `NativeLibrary.SetDllImportResolver` so `ghostty-vt` resolves next to the mod inside KSA's plugin `AssemblyLoadContext`, and copy the native beside `purrTTY.GameMod.dll` (mod ALCs don't reliably probe `runtimes/<rid>/native`).

---

## 5. Binding work — extend the vendored `Ghostty.Vt` (planned features, not "gaps")

All confirmed available at the C level; this is C#-side wrapping/refactor we own:

| Item | C-level support (verified) | Our binding work |
|---|---|---|
| **Configurable scrollback** | `GhosttyTerminalOptions.max_scrollback` (size_t) exists | Thread a `MaxScrollback` option into `BuildNativeOptions` (replace hardcoded `1000`). |
| **Selection** | `GHOSTTY_TERMINAL_OPT_SELECTION = 21`; `selection.h`: gesture state machine (`ghostty_selection_gesture_event/_set/_get/_new/_free/_reset`), `ghostty_terminal_select_word/_word_between/_line/_all/_output`, `selection_format_buf/_alloc`, `selection_adjust/_order/_ordered/_contains/_equal`; `RenderState` per-row `GHOSTTY_RENDER_STATE_ROW_DATA_SELECTION → {start_col,end_col}` | Add native imports + a `Selection`/`SelectionGesture` wrapper; add `RenderStateRow.Selection`. Surface as backend selection commands + per-row ranges in `TerminalFrame`. |
| **Default cursor style/blink** | `OPT_DEFAULT_CURSOR_STYLE = 22`, `OPT_DEFAULT_CURSOR_BLINK = 23` | Wrap as setters; back `SetCursorStyle(shape,blink)`. (No longer a no-op.) |
| **Batched cell reads (perf)** | `ghostty_render_state_row_cells_get_multi`, `ghostty_terminal_get_multi`, `..._row_cells_select`, and a "has_styling without materializing raw cell" fast path | Reimplement the cell enumerator to fetch fields per cell in **one** `get_multi` call (today it does ~12 `ghostty_cell_get` + a `StringBuilder` alloc per cell). Optional: decode the raw `GhosttyCell` (uint64) in managed code. |
| **OSC 52 clipboard / OSC 1 icon** | **Not** in the OPT callback set (callbacks stop at DEVICE_ATTRIBUTES=8); OSC is parsed (`osc.h`) but not surfaced as a terminal effect | **Sidecar**: tee the PTY stream through `Ghostty.Vt.OscParser` in the backend; raise `ClipboardRequest`/`IconNameChanged`. Pure managed, no C change. |
| **PwdChanged** | no native callback (observe via `OnTitleChanged` + read `.Pwd`) | Drive pwd UI from title changes. |
| **Idiomatic cleanup** | — | Optional: replace hardcoded struct byte-offsets (e.g. `RenderState.Colors`) with `[StructLayout]` types; tighten `SafeHandle`/dispose; expose `TerminalFrame`-friendly accessors. Because we own + pin the native, ABI is locked to our build. |

---

## 6. Current purrtty — keep / move / delete

**Delete (emulation):** `purrTTY.Core/Parsing/`, `Terminal/EmulatorOps/` (39), `Terminal/ParserHandlers/`, most `Managers/`, `Utf8Decoder`, `CharacterSetManager`, `ScreenBuffer`/`ScrollbackBuffer` impls, `TerminalEmulator`/`TerminalEmulatorBuilder`, and purrtty's parallel render-state in Display (`IScreenBuffer` materialization, `ScrollbackManager` blend, `TextSelection`/`TextExtractor`, SGR→theme color resolver).

**Move into the backend (`purrTTY.Terminal`):** PTY/process (`Terminal/Process/*` ConPTY, `ProcessManager`, `CustomShellPtyBridge`), sessions (`SessionManager`, `TerminalSession`, `TerminalSessionFactory`).

**Keep unchanged:** `purrTTY.CustomShellContract` + `purrTTY.CustomShells/GameConsoleShell` (emit VT bytes → `Write`); `purrTTY.GameMod` (StarMap hooks, Harmony patches for menu + `OnKey` + `ConsoleWindow.Print`); `purrTTY.Logging`.

**Frontend (`purrTTY.Display`):** keep ImGui chrome/fonts/input-capture; rewrite the grid renderer to draw `TerminalFrame`; move selection to backend commands; keep the ImGui-coupled `ITerminalDrawTarget` as a **private detail of the ImGui frontend** (it must not appear in the backend contract).

**Abstraction leaks to retire (6 sites / 5 files)** that cast `(TerminalEmulator).State` — `TerminalGridRenderer.cs` (×2), `TerminalUiSelection.cs`, `TerminalUiRender.cs`, `TerminalUiMouseTracking.cs`, `KeyboardInputHandler.cs`, `MouseWheelHandler.cs`. In the new design these reads come from `TerminalFrame`/`ITerminalSurface` (mode/cursor/selection state), so the casts disappear by construction.

**Construction seam:** `TerminalSessionFactory.CreateSession` is the single `TerminalEmulator.Create(...)` site → constructs the new `GhosttyTerminalSurface` instead.

### Proposed solution layout (names adjustable)
```
src/vendor/Ghostty.Vt/            # vendored binding (owned, net10) + native build pipeline
purrTTY.Terminal/                 # backend: ITerminalSurface, TerminalFrame, GhosttyTerminalSurface,
                                  #   OSC sidecar, PTY/process, sessions
purrTTY.CustomShellContract/  purrTTY.CustomShells/     # unchanged
purrTTY.Display/                  # ImGui frontend (chrome + input capture + draw TerminalFrame)
  (future) purrTTY.Display.Vulkan/                      # Vulkan/hybrid frontend over TerminalFrame
purrTTY.GameMod/  purrTTY.Logging/
```

---

## 7. Migration plan (phased)

- **P0 — Vendor + retarget the binding.** Bring `Ghostty.Vt` into the solution as a csproj; retarget net10; wire native build (pinned ghostty) + per-RID artifacts; attribution. Build green; run vendored tests.
- **P1 — Native loading in KSA.** `NativeLibrary.SetDllImportResolver` + copy native beside the mod; isolated load smoke test (`new Terminal(80,25); VTWrite; RenderState.Update`) inside KSA's ALC.
- **P2 — Extend the binding (§5).** Add `MaxScrollback`, selection (gesture + per-row + format), default cursor style/blink, and the `get_multi` batched cell reader. Unit-test against the vendored test suite + new cases.
- **P3 — Backend `purrTTY.Terminal`.** Define `ITerminalSurface` + `TerminalFrame`; implement `GhosttyTerminalSurface` (wraps `Terminal`+`RenderState`+selection; OSC sidecar; theme push; key/mouse encoding via libghostty encoders). **Single-thread** native access: queue PTY bytes from the read thread, apply `VTWrite`+`Update` on the frontend tick. Move PTY/sessions in. Build the `TerminalFrame` from `RenderState` via `get_multi`, gated on `Dirty`.
- **P4 — Swap construction + ImGui frontend rewrite.** Point `TerminalSessionFactory` at `GhosttyTerminalSurface`. Rewrite `TerminalGridRenderer` to draw `TerminalFrame` (full graphemes, wide/spacer, pre-resolved colors, underline 0-5, inverse, cursor, per-row selection). Replace `ScrollbackManager` with `Scrollbar`/`ScrollBy`. Push theme into engine; reduce color resolver to opacity/selection styling. Rewire mouse → selection gestures; copy via `GetSelectionText`. Map ImGui keys/mouse → neutral events → `EncodeKey/Mouse`.
- **P5 — OSC sidecar + parity.** Restore `ClipboardRequest`/`IconNameChanged` via the sidecar parser; verify bracketed paste, title, bell.
- **P6 — Delete legacy.** Remove emulation + dead Display state code (§6). Port/keep the conformance harness to drive the new backend.
- **P7 — Verify** (§10). Land `IGhosttyRenderSource`-equivalent already satisfied by `TerminalFrame`/`ITerminalSurface` (the Vulkan frontend consumes the same contract).

---

## 8. Mapping tables

### 8.1 RenderState cell / `Style` → `TerminalFrame` cell
| libghostty | TerminalFrame | Notes |
|---|---|---|
| `Cell.Grapheme (string?)` | `Grapheme` | Full string (fixes old `char` truncation of emoji/ZWJ). `null` → blank. |
| `Cell.Wide` (Narrow/Wide/Spacer*) | `Width` | Wide → 2 cols; Spacer* → render nothing. |
| `Style.Underline` (0-5) | `Flags.Underline` | 0-5 maps exactly to None/Single/Double/Curly/Dotted/Dashed. |
| `Style.Bold/Italic/Faint/Blink/Inverse/Strikethrough/Invisible/Overline` | `Flags.*` | 1:1 (`Invisible`→Hidden). Overline now representable in the new flags. |
| `Cell.FgColor/BgColor` (pre-resolved `ColorRgb?`) | `Fg`/`Bg` | Direct; `null` → frame default. Theme pushed into engine so these are theme-correct. |
| `Cell.HasHyperlink` | (on-demand) | URI via `GridRef` on hover/click only. |
| per-row `ROW_DATA_SELECTION` | `FrameRow.SelectionRange` | One range per row; trivial highlight. |

### 8.2 Callbacks / events → backend events
| Backend event | libghostty | Status |
|---|---|---|
| `WriteToPty` | `OnWritePty(bytes)` | 1:1 (DA/DSR/etc.). |
| `Bell` / `TitleChanged` | `OnBell` / `OnTitleChanged`→`.Title` | 1:1. |
| `FrameChanged` | `RenderState.Dirty != None` after `VTWrite` | derive. |
| `ClipboardRequest` / `IconNameChanged` | sidecar `OscParser` (OSC 52 / OSC 1) | implemented in backend. |
| cursor style | `OPT_DEFAULT_CURSOR_STYLE/BLINK` | engine-level setter. |

---

## 9. Risk register (post-pivot)

| # | Risk | Severity | Mitigation |
|---|---|---|---|
| D1 | **Native lib loading in KSA's plugin ALC.** | High | `SetDllImportResolver` + copy native beside mod; isolated load test (P1) first. |
| D2 | **RID coverage** (`win-x64`, `linux-x64`, `osx-arm64`; we own the build, can add RIDs). | Medium | Build all needed RIDs in our CI; guard ctor in try/catch; degrade gracefully. |
| D3 | **Native build/CI burden** — we now own building libghostty-vt with Zig per-RID and tracking the ghostty pin. | Medium | Reuse upstream `build/build-native.ps1`/`justfile`; pin commit; scheduled rebase + ABI smoke test. |
| D4 | **C-core thread-safety undocumented** (owning the C# binding doesn't make the Zig core thread-safe). | High | Single-thread native `Terminal` access (apply `VTWrite`+`Update` on the frontend tick). |
| D5 | **ABI coupling** native↔binding (struct offsets). | Low (was Med) | We pin + build both together; ABI smoke test; optionally replace hardcoded offsets with `[StructLayout]`. |
| D6 | **Selection/binding extension effort** (§5). | Medium | Self-contained (P2); land grid/scroll/color first so the product is usable while selection lands. |
| D7 | **Grapheme/wide differences** vs old `char`. | Low | `TerminalFrame` carries full graphemes + width; mostly an improvement. |
| D8 | **ConPTY-only / macOS has no OS shell.** | Medium | Orthogonal; use `GameConsoleShell` for macOS dev, ConPTY on Windows. Unix PTY is separate future work. |
| ~~gaps~~ | selection / scrollback / cursor / OSC — **now planned work (§5), not risks.** | — | — |

---

## 10. Verification strategy

**Automated (`purrTTY.Terminal.Tests`, macOS-runnable):** feed known VT → assert `TerminalFrame` (grapheme, fg/bg post-theme, underline style, inverse, wide, cursor, alt-screen, scrollback counts); selection (`select_word/line` + gesture drag → `GetSelectionText` + per-row ranges); **ABI smoke test** (fixed VT → fixed cell values) against our pinned native. Keep the vendored `Ghostty.Vt.Tests`.

**Manual in KSA:** `GameConsoleShell` output; colors/themes + switching; scrollback + scrollbar; resize/reflow; alt-screen TUIs; drag/word/line select + copy/paste; bracketed paste; cursor shapes/blink; bell; title; OSC 52 clipboard. macOS via `GameConsoleShell`; ConPTY end-to-end on Windows.

**Acceptance:** feature parity (incl. selection/clipboard, no knowingly-deferred items), no main-thread stalls under output floods, clean mod load/unload (native released), and a backend contract with **no ImGui/Vulkan types** (verified by the frontend depending only on `purrTTY.Terminal`).

---

## 11. Frontend options

The backend is renderer-neutral; frontends consume `TerminalFrame` + drive `ITerminalSurface`:
1. **ImGui (this plan).**
2. **Direct Vulkan / hybrid (no ImGui for the grid)** — companion docs, now consuming the same `TerminalFrame` seam:
   - [`LIBGHOSTTY_VULKAN_FEASIBILITY.md`](./LIBGHOSTTY_VULKAN_FEASIBILITY.md)
   - [`LIBGHOSTTY_VULKAN_PLAN.md`](./LIBGHOSTTY_VULKAN_PLAN.md)

---

## 12. Appendix — key references

**purrtty:** `purrTTY.Core/Terminal/ITerminalEmulator.cs` (old seam, replaced by `ITerminalSurface`/`TerminalFrame`); `Terminal/Sessions/TerminalSessionFactory.cs` (construction swap); `Display/Rendering/{TerminalGridRenderer,ITerminalDrawTarget,ImGuiDrawTarget}.cs` (rewrite to draw `TerminalFrame`; keep draw-target private to ImGui frontend); `Display/Controllers/TerminalUi/*` (selection + leak sites); `GameMod/TerminalMod.cs` (native loader).

**vendored binding (`Ghostty.Vt`):** `src/Ghostty.Vt/{Terminal,RenderState,TerminalOptions}.cs`, `Native/NativeMethods.cs` (`[LibraryImport("ghostty-vt")]`; add selection imports + `get_multi` + max_scrollback); `OscParser.cs` (sidecar); `examples/GhostlingDotNet/*` (reference); `build/build-native.ps1` + `ghostty-upstream.json` (native pin); `LICENSE` (MIT).

**native headers (`ghostty/include/ghostty/vt`):** `terminal.h` (OPT enum: SELECTION=21, DEFAULT_CURSOR_STYLE=22/BLINK=23; `max_scrollback`), `render.h` (`row_cells_get_multi`, `ROW_DATA_SELECTION`), `selection.h`, `style.h`/`color.h`, `osc.h`, `key/`, `mouse/`.
