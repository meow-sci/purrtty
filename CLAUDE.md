# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

purrTTY is a terminal emulator mod for the Kitten Space Agency (KSA) game engine.

As of the libghostty-vt migration, purrTTY **no longer ships its own VT emulator**. It delegates
all terminal emulation to **libghostty-vt** — the standalone, conformance-tested VT engine from
[Ghostty](https://github.com/ghostty-org/ghostty) — via a **vendored, owned** C# binding
(`Ghostty.Vt`). purrtty owns a clean three-layer architecture on top of it.

What this project/mod does:
- Runs real shell sessions (ConPTY shells on Windows; POSIX-pty shells on Linux/macOS; an in-game `GameConsoleShell` cross-platform) inside an in-game terminal window.
- Feeds shell output to libghostty-vt and renders the resulting grid through ImGui in KSA.
- Encodes keyboard/mouse/paste input through libghostty-vt's encoders and writes it back to the shell.

## Architecture — three clean layers

```
FRONTEND   purrTTY.Display (ImGui)          — GhosttyTerminalController + FrameGridRenderer
   │  consumes TerminalFrame (OUT) / drives ITerminalSurface (IN) — NO engine types cross this seam
BACKEND    purrTTY.Terminal (headless, renderer-neutral)
   │  ITerminalSurface + TerminalFrame + GhosttyTerminalSurface + OSC sidecar + sessions
BINDING    vendor/Ghostty.Vt (vendored, owned, net10)  — Terminal/RenderState/encoders + purrtty extensions
NATIVE     vendor/Ghostty.Vt/native/<rid>/ — prebuilt shared libs from pinned ghostty, checked in (osx-arm64, win-x64, linux-x64)
```

The **renderer-neutral seam** is the heart of the design: the backend produces a `TerminalFrame`
snapshot (rows of pre-resolved cells, cursor, scrollbar, colors) and accepts commands/events via
`ITerminalSurface`. **No ImGui/Vulkan/KSA types cross this boundary**, so a future Vulkan/hybrid
frontend is a frontend swap, not a backend rewrite.

### Project dependencies

```
purrTTY.Logging
vendor/Ghostty.Vt              # vendored libghostty-vt binding (+ native lib) — MIT, see its README
purrTTY.CustomShellContract    # ICustomShell, CustomShellRegistry, WellKnownShellEnvironment (namespace purrTTY.Core.Terminal)
purrTTY.CustomShells           # GameConsoleShell — refs CustomShellContract + KSA DLLs ONLY (deliberately
    │                          #   no purrTTY.Display: shell config arrives via launch-option env vars)
purrTTY.Terminal               # BACKEND: surface + sessions + RELOCATED PTY layer (Pty/)
    │  refs vendor/Ghostty.Vt + purrTTY.CustomShellContract + purrTTY.Logging
    └── purrTTY.Terminal.Tests (NUnit integration tests)
purrTTY.Display                # ImGui FRONTEND: refs purrTTY.Terminal + KSA ImGui DLLs
    └── purrTTY.Display.Tests  (pure-logic theming/config tests — no ImGui)
purrTTY.GameMod                # final mod DLL: refs Display + CustomShells; StarMap hooks + Harmony patches
```

> The PTY/process layer was relocated out of the retired `purrTTY.Core` project into
> `purrTTY.Terminal/Pty/` (its types keep the `purrTTY.Core.Terminal` namespace, which is shared
> with `CustomShellContract`). There is no longer a `purrTTY.Core` project.

## Build and Test Commands

```bash
dotnet build purrtty.slnx              # Build the whole solution
dotnet build purrTTY.Terminal          # Backend only
dotnet build purrTTY.GameMod           # Game mod (also deploys to the KSA mods dir, incl. native libs)
```

The mod output is **platform-agnostic**: every build bundles the prebuilt native libs for all
three RIDs (see below), so one build from any host OS — including the Linux CI runner — produces a
single mod dist that runs on Windows, macOS, and Linux.

### KSA paths (Directory.Build.props)

`KSAFolder` — the KSA reference assemblies (KSA.dll, Brutal.\*.dll, Planet.\*.dll) the projects
compile against — resolves in order, first match wins, every tier host-OS-agnostic:
1. `KSA_DLL_DIR` env var (or `-p:KSA_DLL_DIR=...`) — what CI uses
2. a `ksa-game-assemblies` checkout cloned **next to this repo** (DLLs in its `current/dll/`) — clone-and-go
3. per-OS defaults (Windows game install; `~/repos/meow-sci/ksa-game-assemblies/current/dll/` otherwise)

The deploy destination (`SelectedDistModDir`, where GameMod's `CopyCustomContent` writes the
`purrTTY/` mod folder) honors `PURRTTY_DIST_DIR` the same way; otherwise it defaults to the
per-OS KSA mods dir.

### CI / releases (`.github/workflows/release.yml`)

Two jobs. A **test matrix** runs `dotnet test purrtty.slnx -c Release` on ubuntu-latest,
windows-latest, and macos-14 — each runner loads **its own** vendored native libghostty-vt
(linux-x64 / win-x64 / osx-arm64), so `RawCellLayout.Validate()` and the per-OS PTY backends get
real coverage on every platform after a pin bump. Both jobs check out
`meow-sci/ksa-game-assemblies` (private repo holding the KSA DLLs under `current/dll/`; access
via the `KSA_GAME_ASSEMBLIES_PAT` secret — a read-only fine-grained PAT, same pattern as flexo).
TRX results are published as per-OS check runs (`dorny/test-reporter`, pinned to the v3 commit
SHA because it holds `checks: write`) and uploaded as `test-results-<os>` artifacts.

A **build job** (ubuntu, `needs: test`) stamps the `mod.toml` version, builds the Release dist
via `PURRTTY_DIST_DIR`, zips `purrTTY/`, and publishes a GitHub release. Branch-derived values
(release name, base version) are validated against `^[0-9A-Za-z._-]+$` and reach run scripts via
`env:`, never raw `${{ }}` interpolation (injection hardening — the job has `contents: write`):
- push to `main` → prerelease tagged `tip-<UTC stamp>`, asset `purrTTY-tip-<stamp>.zip`;
  mod.toml version becomes `<base>-tip.<stamp>`; older tip releases are pruned (keep-count set in the workflow)
- push to `release/<v>` → release tagged `v<v>`, asset `purrTTY-<v>.zip`, mod.toml version `<v>`;
  re-pushing the branch deletes and recreates the release + tag (`cancel-in-progress` is disabled
  on `release/*` refs so a superseding push cannot cancel between release delete and re-create)
- push to `feature/*`, `fix/*`, `chore/*`, or a PR into `main` → build + tests only (no version
  stamp, no release)

A missing/misresolved KSA assemblies dir fails fast with one actionable MSBuild error
(`ValidateKSAAssemblies` in `Directory.Build.props`; projects with KSA refs set
`RequiresKSAAssemblies=true`) instead of a CS0246 avalanche.

### Testing

The terminal-emulation behavior is **trusted to libghostty-vt** and is not re-tested. The tests
cover purrtty's **integration** with the engine.

```bash
dotnet test purrtty.slnx --nologo -v quiet                                  # full suite (4 test projects)
dotnet test purrTTY.Terminal.Tests/purrTTY.Terminal.Tests.csproj --nologo -v quiet   # engine integration only
```

`purrTTY.Terminal.Tests` (NUnit) validates frame production, theming, selection, the OSC
sidecar (incl. split-chunk/ST framing, the clipboard-query path, oversize-discard, and CAN
abort), key/mouse encoding, bracketed paste, DSR replies, scrollback, the DEC 2026 hold +
safety timeout, the session-wiring data flow and `SessionManager` lifecycle
(configurator-before-publication, restart reuse — driven through a registered headless custom
shell), the `CustomShellPtyBridge` adapter contract, and the PTY input contracts
(`PtyInputContractTests`: Windows argv quoting, Unix discrete-argv + `Auto` resolution, and
`PtyInputQueue` ordering/overflow/failure/stuck-writer semantics — pure logic, runs on every
host OS). `UnixProcessManagerTests` exercises the POSIX pty backend against real `/bin/sh`
children (output, exit codes, pty echo, initial winsize, working directory, shell detection);
it runs on macOS (dev) and linux-x64 (CI) and self-skips on Windows — this is the pre-player
coverage for Linux shell launching. `ConPtyProcessManagerTests` is its Windows twin (real
cmd.exe under ConPTY; pins the unified fast-exit contract — spawn-then-die reports once via
`ProcessExited` with the real code, never as a start failure — and the dying shell's tail
output being flushed); it runs on the windows-latest CI leg and self-skips elsewhere. `purrTTY.CustomShellContract.Tests` +
`purrTTY.CustomShells.Tests` cover the custom-shell layer (incl. the Brutal-version-sensitive
`OnConsolePrint` capture, exercised against the real `ImColor8`/`ConsoleWindow` color fields).
`purrTTY.Display.Tests` covers the pure-logic display pieces headlessly: theme TOML round-trip,
all 18 bundled schemes parse, theme-override clamping, and `AtomicFile`.

#### Tests must be quiet by default (MUST)

A test that passes or skips must produce **zero output** — no stdout/stderr, no recorded test
messages. Test-run output is read by CLIs, CI logs, and AI assistants; per-test chatter bloats
that context on every run while carrying no information (a 2026-06 pass removed all of it).
Concretely:

- **Never `Assert.Pass("message")`.** It records the message as test output on every run. For
  "does not crash" smoke tests, assert the real postcondition (state unchanged, all tasks
  completed) or wrap the act in `Assert.DoesNotThrow(...)` — a test that runs to completion
  passes without any `Assert.Pass`.
- **Skip silently: `Assert.Ignore()` with no message,** with the reason as a code comment at the
  ignore site (the recorded message prints on every skipped run — e.g. the Windows CI runner
  printed the POSIX-skip reason on all of `UnixProcessManagerTests`). The skipped test's *name*
  still appears in results; that plus the comment is enough to diagnose.
- **No `Console.Write*`/`TestContext.WriteLine`/`TestContext.Progress` on success paths.**
  Diagnostic detail belongs in the assertion *failure message* (the optional last argument of
  `Assert.That`), which is printed only when the assertion fails — that is the one place to be
  generous with context (include the observed state, like `WaitForOutput`'s "Output so far").

#### No fixed sleeps in tests (MUST)

Never use a bare `Task.Delay`/`Thread.Sleep` to "give async work time to finish" before
asserting. A 2026-06 cleanup removed ~400 such sleeps (≈26 s of pure dead time per run — the
custom-shell suites went from ~31 s to <0.5 s) with zero assertion changes; do not reintroduce
the pattern. It is both slow (the sleep always runs in full) and flaky (a loaded CI runner can
need longer than the guess). Synchronize on the actual completion signal instead:

- **Know what is already synchronous.** `BaseLineBufferedShell.WriteInputAsync` processes input
  fully before returning — line buffer, cursor, history, and command execution are assertable
  immediately, no wait at all. Only **output delivery** is async (queued to a channel, pumped to
  `OutputReceived` on a background task). `BaseChannelOutputShell.StopAsync` drains that pump
  before returning, so post-stop output/`Terminated` assertions also need no wait.
- **Channel-output shells: flush with a sentinel.** The test-shell subclasses in
  `BaseLineBufferedShellTests` and `GameConsoleShellTests` have `FlushOutputAsync()`: queue a
  unique zero-length sentinel via `QueueOutput` and await its `OutputReceived` event — the pump
  is single-reader FIFO, so the sentinel arriving proves every earlier output was delivered.
  Reuse/copy it for new channel-shell fixtures. Two rules: collectors must skip zero-length
  events (so the sentinel can't pollute captured output/`LastOutputType`), and never flush after
  `StopAsync` (channel completed — the sentinel would never arrive; no wait is needed there anyway).
- **Waiting for an event or a count:** block on the signal with a generous timeout —
  `ManualResetEventSlim`/`CountdownEvent`/`TaskCompletionSource` + `Wait(timeout)`/`WaitAsync`
  (see `BaseChannelOutputShellTests`), or a bounded poll-until-condition loop
  (`WaitForCapturedCountAsync` in `BaseChannelOutputShellTypedOutputTests`). The timeout is a
  failure bound, not a wait: tests pass as fast as the code runs and only burn time when broken.
- **Legitimate fixed delays** are only the polling *interval inside* a deadline-bounded
  condition loop — required when no completion signal exists, e.g. waiting on a real external
  process (`UnixProcessManagerTests.Harness.WaitForOutput` polls every 25 ms against live
  `/bin/sh` output, 10 s deadline) — or a test whose *subject* is timeout behavior itself
  (`StopAsync_WithTimeout_CancelsPumpIfNotDrained`). If you believe a new fixed sleep is
  justified, document the reason in a comment at the sleep site.

> The legacy emulator test/app projects (`purrTTY.Core.Tests`, `purrTTY.Display.Tests`,
> `purrTTY.Display.Playground`, `purrTTY.TestApp`) were deleted with the emulator.

### Building the native libghostty-vt

P/Invoke needs the **shared library** (`.dylib`/`.dll`/`.so`). Prebuilt binaries for all three
RIDs are **checked in** at `vendor/Ghostty.Vt/native/{osx-arm64,win-x64,linux-x64}/`; the
`Ghostty.Vt` csproj copies all of them flat into every build output (the filenames are
platform-distinct) and `NativeLibraryResolver` loads the one matching the running OS.

Rebuilding is only needed on a ghostty pin bump. All three targets cross-compile from a single
host with zig 0.15 — full commands, strip step, and gotchas live in
`vendor/Ghostty.Vt/README.md`. The short version:

```bash
export PATH="/opt/homebrew/opt/zig@0.15/bin:$PATH"     # zig 0.15 on this machine
cd /path/to/ghostty                                    # at the pinned commit
zig build -Demit-lib-vt -Dtarget=aarch64-macos -Doptimize=ReleaseFast         # → zig-out/lib/libghostty-vt.dylib (symlink: cp -L)
zig build -Demit-lib-vt -Dtarget=x86_64-windows-gnu -Doptimize=ReleaseFast    # → zig-out/bin/ghostty-vt.dll
zig build -Demit-lib-vt -Dtarget=x86_64-linux-gnu.2.31 -Doptimize=ReleaseFast # → zig-out/lib/libghostty-vt.so.0.1.0 (llvm-strip --strip-debug)
```

Gotchas: never `-mcpu native` (non-portable; was observed to AV inside `vt_write` — the SIMD deps
runtime-dispatch to AVX2 etc. anyway); Windows must use the `gnu` ABI (msvc fails to compile the
highway/simdutf C++ deps); vendor **only** the shared library — never `ghostty-vt-static.lib` or
the import lib `ghostty-vt.lib` (not loadable at runtime); after a bump run the test suite
(`RawCellLayout.Validate()` fails loudly if the native cell layout changed). The native lib is
**pinned, not forked** (current pin recorded in `vendor/Ghostty.Vt/README.md`).

## Code Navigation Guide

Start here:
- Solution: `purrtty.slnx`
- Shared build config + KSA DLL paths (per-OS): `Directory.Build.props`
- Migration plan + status: `LIBGHOSTTY_ANALYSIS.md`; provenance/licensing: `vendor/Ghostty.Vt/README.md`, `THIRD-PARTY-NOTICES.md`

Vendored binding (`vendor/Ghostty.Vt/`):
- Engine surface: `src/Terminal.cs`, `src/RenderState.cs`, `src/TerminalOptions.cs`, encoders (`src/KeyEncoder.cs`, `src/MouseEncoder.cs`)
- Native P/Invoke: `src/Native/NativeMethods.cs` (+ `NativeMethods.Selection.cs`, `NativeMethods.TrackedGridRef.cs`)
- Native loader (KSA ALC): `src/Native/NativeLibraryResolver.cs` (ModuleInitializer + `SetDllImportResolver`)
- purrtty additions: `src/Terminal.Selection.cs` (selection + default cursor style/blink + `Terminal.TrackGridRef` + `Terminal.HasSelection` — a cheap native no-value probe surfaced as `ITerminalSurface.HasSelection` for UI enable-state), `src/TrackedGridRef.cs` (tracked grid refs — engine-owned references that follow their cell across mutations and survive scrollback pruning; used for the drag-selection anchor, see gotcha 17), `MaxScrollback` in `TerminalOptions.cs`, per-row `RowSelection` in `RenderState.cs`, and the **render-hot frame read path** in `src/RenderState.FrameReader.cs`: `RenderFrameReader` (forward-only row/cell reader — ~2 native calls per cell, 3 for styled cells, one reused cells handle per frame), `RawCell` (managed bit-decode of the packed `page.Cell` u64: content tag / codepoint / style_id / wide / content-bg — replaces per-field `ghostty_cell_get` round-trips), `RenderState.ClearDirty()` + `RenderFrameReader.ClearRowDirty()` (the engine only ever RAISES dirty flags — the consumer must clear them after each frame or `Dirty` reads Full forever), `RenderState.ReadColors(...)` (allocation-free palette read for the per-frame path — the convenience `Colors` getter allocates), UTF-8 grapheme-cluster reads into caller buffers, and `RawCellLayout.Validate()` — a runtime cross-check of the managed decode against `ghostty_cell_get` so a native pin bump that changes the bit layout fails loudly (run once per process by `GhosttyTerminalSurface`, and as the `RawCellLayout_MatchesNativeAccessors` test). The older `RenderStateRowEnumerator`/`RenderStateCellEnumerator` remain but are off the render path; use `GridRef.GetCell()` for a fully-populated cell. The binding is **pruned to the called surface** (kitty graphics, OSC/SGR parsers, formatter, sys hooks etc. were removed — see the divergence section in `vendor/Ghostty.Vt/README.md` before re-vendoring anything from upstream).

Backend (`purrTTY.Terminal/`):
- Seam contract: `ITerminalSurface.cs`; frame value types: `Rendering/` (`TerminalFrame`, `FrameRow`, `FrameCell`, `RgbaColor`, `CellFlags`/`UnderlineStyle`/`CellWidth`/`CursorShape`)
- Engine wrapper: `Ghostty/GhosttyTerminalSurface.cs` (single-threads native; theme push; key/mouse encode; drag selection via `BeginSelectCells`/`ExtendSelectCells` with a **tracked** grid-ref anchor that survives viewport scroll *and* scrollback pruning — gotcha 17; bounded PTY inbox with chunked catch-up — gotcha 18; **dirty-aware frame production** — see gotcha 12 — with cell fg/bg resolved managed-side from style + palette in `FillCell`, a per-surface `GraphemeCache` interning cell strings so steady-state rebuilds allocate nothing, DEC 2026 synchronized-output gating — gotcha 13 — and `LastFrameStats` for the perf HUD)
- OSC 52 clipboard / OSC 1 icon: `Ghostty/OscSidecar.cs` (managed tee of the output stream;
  decides interest in raw bytes before allocating, **discards** payloads that hit the 64 KiB cap
  — a truncated OSC 52 cut on a base64 boundary would otherwise paste a silent prefix — and
  treats CAN/SUB as abort-current-sequence, which the inbox-drop heal sequence relies on)
- Neutral input types: `Input/` (`TerminalKey`, `TerminalKeyEvent`, `TerminalMouseEvent`, `GridPoint`, `KeyModifiers`)
- Sessions: `Sessions/` (`TerminalSession`, `SessionManager`, `TerminalSessionFactory` — the construction seam). `SessionManager.SessionConfigurator` runs against each new session *before* it is initialized/published — the safe place for theme push + surface event wiring (`TerminalWindow.WireSession` uses it); a `SessionCreated` subscriber runs post-publication, possibly on a pool thread, and must not touch the surface. Session close (`TerminalSession.CloseAsync`) completes synchronously on the calling thread — see gotcha 19.

Frontend (`purrTTY.Display/`):
- Multi-window controller: `Ghostty/GhosttyTerminalController.cs` (implements `Controllers/ITerminalController.cs`
  — deliberately minimal: IsVisible/Update/Render/Dispose; everything else is reached through the
  concrete type; manages a list of `TerminalWindow`s, routes game-menu actions to the focused
  window, persists display defaults + first-window geometry through a single shared
  `ThemeConfiguration` instance — including when the **last** window is closed via its "x")
- Per-window terminal: `Ghostty/TerminalWindow.cs` — a partial class: core
  (lifecycle/render/chrome/tabs/geometry) in `TerminalWindow.cs`, input encoding in
  `TerminalWindow.Input.cs`, font/metric resolution in `TerminalWindow.Fonts.cs`, the perf HUD
  in `TerminalWindow.PerfHud.cs`, the lock-mode hot zone in `TerminalWindow.HotZone.cs`
  (owns a `SessionManager` whose sessions are tabs —
  tab bar hidden with one tab; per-window theme/font/opacity **plus cursor style/blink, the
  focus/hover border, and lock mode + focus hot zone (gotcha 22)** via `TerminalWindowSettings`
  (own file; its `ApplyThemeOverrides` is the single clamp-and-apply implementation behind both
  theme application and new-window defaults); AltGr text (Ctrl+Alt + queued chars on Windows
  international layouts) is delivered as text, not a spurious Ctrl+Alt chord; chrome hiding:
  transparent WindowBg/MenuBarBg + zero border + `Alpha=0` menu/tab strips when the mouse is not over the
  window, even while focused; menu-bar strip is the drag handle (no title bar) and an `InvisibleButton`
  over the grid keeps drag-selection from moving the window; Ctrl+Shift+C/V copy/paste; Ctrl/Alt
  modified keys (letters, digits, Ctrl punctuation) encode via the engine key encoder while plain
  text flows through the ImGui character queue with surrogate pairing; a failed session start is
  shown in the window (`NotifySessionStartFailed`); saved/cascaded geometry is clamped to the
  viewport work area; grid snap:
  when an interactive resize ends, `TrackResizeSnap` shrinks the window by the fractional-cell
  remainder so the grid exactly fills the content region — chrome is *measured* as
  `windowSize - avail`, not computed from style metrics, and a resize is recognized only as
  size-change-while-LMB-held with the snap firing on release, so the snap's own
  `SetNextWindowSize` can never re-trigger detection (no timers needed))
- Theming: `Theming/` (`ThemeDefinition`/`ThemeColors` + `ToEngineTheme()`, `ThemeTomlFormat` (Tomlyn DOM
  read/write), `ThemeCatalog` — code-built "Default" + bundled `TerminalThemes/*.toml` beside the
  assemblies + user themes in `<config>/.purrTTY/themes/`). Theme TOML = alacritty-style `[colors.*]`
  sections; user-saved themes also carry `[font]` (family/size), `[window]` (3 opacities),
  `[cursor]` (style `block|bar|underline` + blink), `[focus]` (border_on_focus / border_on_hover /
  border_opacity), `[lock]` (enabled + hot_zone / hot_zone_placement `top-left`..`bottom-right` /
  hot_zone_width/height/color/opacity/hover_opacity), and
  `[meta] name` (the display name — filenames are sanitized on save, so the name cannot be derived
  from the filename) — those fields are optional and "keep current"/fall back when absent (bundled
  themes are colors-only). Config and theme writes go through `Configuration/AtomicFile`
  (temp + rename) so an interrupted write never leaves a truncated file.
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
- Two real-PTY backends behind `IProcessManager.cs`, selected per-OS by `TerminalSessionFactory`:
  ConPTY on Windows (`ProcessManager.cs` + `Process/ConPty*` etc.) and POSIX pty on Linux/macOS
  (`UnixProcessManager.cs` + `Process/UnixPtyNative.cs` / `UnixPtySpawner.cs` /
  `UnixPtyOutputPump.cs`) — see gotcha 16. Both share one start semantic: start failure =
  resolve/spawn/attach failure; a shell that spawns and dies promptly reports **once**, via
  `ProcessExited` with its real exit code (ConPTY's former 100 ms post-spawn validation raced
  the Exited callback and double-reported — removed). The shared teardown disciplines
  (cancel-outside-lock CTS handling, bounded never-throwing pump waits, join-before-dispose
  writer shutdown) live in `Process/PtyTeardown.cs`; the resource-close policies stay per-OS.
- Shell resolution: `Process/ShellCommandResolver.cs` — `ResolveShellCommandLine` (quoted
  CreateProcess command line, ConPTY) vs `ResolveShellCommandArgv` (discrete argv, Unix exec);
  never join-then-split — Windows args are quoted per CommandLineToArgvW rules
  (`AppendQuotedArgument`), Unix args stay discrete. `Custom` takes a path or a bare executable
  name — a bare name (no directory component) resolves via PATH like every other shell type
  (a user-configured `nu`/`cmd.exe` must launch the way it does in any other terminal, not be
  checked against the game's working directory). `Auto` on Unix = `$SHELL`, then zsh/bash/sh
  from PATH; `Auto` on Windows offers WSL only when `WslDistributionDetector` finds ≥1 distro,
  then PowerShell, then cmd. `Auto` is also the config **default and unparsable-value fallback**
  (`ThemeConfiguration`) — the only shell type valid on every platform.
- Input queue: `Process/PtyInputQueue.cs` — bounded queue + dedicated writer thread used by both
  managers (gotcha 20).
- Shell detection for menus: `ShellAvailabilityChecker.cs` (`IsShellAvailable` cached per shell
  type; availability is defined as **resolvability** — it delegates to `ShellCommandResolver`,
  so the menus can never offer a shell the launch would reject), `WslDistributionDetector.cs`
  (Windows; `wsl --list --quiet` with a bounded 15s wait + kill, output read bounded too),
  `UnixShellDetector.cs` (`/etc/shells` + `$SHELL`, deduped by executable name, default marked)
  — all cached for the **process lifetime** (no expiry; shell installs do not change mid-game)
  and deliberately free of game-logging dependencies so they work from the test host; caching
  contract pinned by `ShellDetectionCachingTests`
- Custom-shell adapter: `CustomShellPtyBridge.cs` (its `StartAsync` triggers the shell's
  `SendInitialOutput` — banner/prompt parity with a real shell's spawn output — and swaps in a
  fresh per-run exit-code source, since `GameConsoleShell` raises `Terminated` from its stop
  hook and a reused completed source made a stop→restart cycle look already-terminated);
  launch options: `ProcessLaunchOptions.cs`
- Session/process event args + `SessionState`: `SessionEventArgs.cs`, `ProcessEventArgs.cs`

Game/integration (`purrTTY.GameMod/`):
- Lifecycle + toggle + hotkey/theme modals: `TerminalMod.cs`. The full menu content lives in
  `TerminalMenus.cs` (`TerminalMenus.DrawMenuContent()` plus the static hook fields —
  `Toggle`/`MenuController`/`Open*Dialog` — that the mod instance wires at init and clears on
  dispose) and is registered two ways with identical content: via the
  `[ModMenuEntry("purrTTY")]` attribute on `TerminalMod.DrawMenu` when the ModMenu companion
  mod is present, and via a
  `Patcher.cs` postfix on KSA's empty public `Program.DrawProgramMenusHook()` menu-bar extension
  point otherwise (replaced the former fragile `DrawMenuBar` IL transpiler — gotcha 21). Menus: Toggle Terminal / Toggle Hotkey,
  New Tab / New Window (items read from `ShellMenuCache` — an immutable snapshot of shell entries +
  WSL distros + Unix shells built **once on a background thread at init**; the menu draw path must
  never run detection itself, because a slow probe — wsl.exe service spin-up, a dead network share
  in PATH — hangs the render thread; a detection *failure* falls back to Default Shell (`Auto`,
  valid everywhere, needs no detection) + Game Console, never Game Console alone. The menu draw
  path is also allocation-free per frame — cached hotkey-shortcut string, static launch delegates
  reading `MenuController`, static accessor lambdas for the sliders, indexed loops — except the
  custom-shell enumeration below, a small LINQ read that runs only while a New Tab/New Window
  menu is open. WSL2 is
  offered only when ≥1 distribution was detected:
  wsl.exe ships with stock Windows even when WSL was never set up, so executable presence is not
  evidence of a working WSL, and bare `wsl` with no distro yields a dead session. Per-shell items
  via `UnixShellDetector` on Linux/macOS replace the generic "Default Shell" entry; Game Console
  always offered. Custom shells registered by **other mods** over the exported contract (e.g.
  gatOS) are appended by `DrawRegisteredCustomShellItems`: it enumerates
  `CustomShellRegistry.GetAvailableShells()` **live on every draw** (skipping the built-in
  `GameConsoleShell` id) and launches via `ProcessLaunchOptions.CreateCustomGame(id)`. Live
  reading instead of a `ShellMenuCache` snapshot solves cross-mod registration timing — load
  order is undefined, so another mod may register after this mod's init — without a refresh
  hook, and it honors the never-detect-on-the-draw-path rule: the read is a plain
  ConcurrentDictionary enumeration, and registry discovery already ran synchronously in
  `InitializeTerminal` before `MenuController` (the gate on drawing these menus) was
  published), Theme (built-in +
  saved lists, Save Current As... modal with name input, Refresh), Font (size slider + family list),
  Focus (cursor style/blink, focus+hover border + opacity, lock mode + hot zone
  placement/size/color/opacities — gotcha 22),
  Window (hide-chrome + performance-HUD checkboxes + 3 opacity sliders). Menu actions target `controller.FocusTarget`.
- Bundled assets: `TerminalThemes/*.toml` (18 color schemes; parse-tested by
  `purrTTY.Display.Tests`) + `TerminalFonts/*` (Nerd Fonts, `.iamttf` + one `.otf`), both copied
  to the build output and the deployed mod dir by the csproj.
- Harmony patches: `Patcher.cs` applies each patch **independently** via
  `CreateClassProcessor(type).Patch()` with a per-class try/catch (gotcha 21) — required
  (`Patch01` input gate, `Patch03_HotkeyGuard`) vs optional (`Patch02` menu fallback,
  `ConsoleWindowPrintPatch`); the harmony instance is recreated lazily (`??=`) so a reload after
  `unload()` nulled it still patches. `Patch01` gates `KSA.Program.OnKey` via
  `GhosttyTerminalController.IsAnyTerminalActive` **but always forwards key *releases*** (suppressing
  a Release at the focus boundary strands the game's held-key state). `Patch03_HotkeyGuard` blocks
  `GameSettings.OnKeyAll` while `ImGui.GetIO().WantTextInput` is set (null-guards the
  `Program.ConsoleWindow` static) so typing in mod text fields never fires game hotkeys. The toggle
  hotkey (`ToggleHotkeyBinding.MatchesPress`) uses `IsKeyPressed(repeat:false)` and is suppressed
  while a text field has focus. `Patches/ConsoleWindowPrintPatch.cs` (captures game-console output; targets the Brutal sink `ConsoleWindow.Print(ReadOnlySpan<char>, ImColor8, int)` → `GameConsoleShell.OnConsolePrint` — **Brutal-version-sensitive**: an older API used `Print(string, uint, int, ConsoleLineType)`)

Custom shells:
- Contract: `purrTTY.CustomShellContract/` (`ICustomShell`, `CustomShellRegistry`,
  `BaseLineBufferedShell`, `WellKnownShellEnvironment` — well-known env-var names; shell
  configuration like the Game Console prompt rides launch-option environment variables so
  shells never reference display-layer config types)
- The contract is a **published inter-mod ABI**: `mod.toml` exports
  `purrTTY.CustomShellContract` + `purrTTY.Logging` over the StarMap ALC
  (`[StarMap] ExportedAssemblies`), so a mod that lists them in its purrTTY
  `[[StarMap.ModDependencies]]` `ImportedAssemblies` resolves purrTTY's loaded copies —
  one type identity, one shared `CustomShellRegistry.Instance` (the consumer is gatOS).
  `purrTTY.Logging` is exported because the contract assembly references it; an importer
  needs both from the same ALC. Treat both assemblies' public surfaces as versioned API.
- Built-in game shell: `purrTTY.CustomShells/GameConsoleShell.cs` (emits VT bytes → `Surface.Write`;
  prompt from `WellKnownShellEnvironment.GameShellPrompt`, stamped by
  `ThemeConfiguration.CreateGameShellLaunchOptions` / the menu launch path)

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
   and **synthesized in integer cell metrics**: the engine's encoder maps pixels→cells by dividing
   by the *integer* cell size pushed via `SetMouseGeometry` (`cols * (int)cellWidth`), so
   `HandleAppMouse` computes the cell with the real (fractional) metrics and reports that cell's
   center in the integer metrics — raw float pixels drift columns off as x grows whenever the
   cell width is fractional. Holding **Shift bypasses app mouse tracking** (xterm behavior), so
   selection and the context menu work inside tmux/nvim; a Release is only forwarded when its
   Press was (presses are hover-gated, releases are not).

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
   redraws across many render ticks — the primary cause of visible tearing.) Teardown closes the
   **pseudoconsole first** — conhost flushes its remaining output and breaks the pipes
   (ERROR_BROKEN_PIPE/ERROR_INVALID_HANDLE exit the loop quietly) — then waits ≤2 s for the pump
   to drain the tail before closing the pipe handles, so a short command's final output is not
   truncated and the read handle is never yanked from under a blocked `ReadFile`. Closing the
   pseudoconsole is the **only** flush/unblock path — conhost never breaks the pipes on its own
   after the client exits (verified empirically; an exit-then-wait "natural drain" just stalls) —
   and the pump's read-cancellation CTS is cancelled only **after** the drain: the token is
   checked between reads, so cancelling earlier raced the pump out of its loop with the flushed
   tail still unread in the pipe. A handle whose
   pump thread fails to stop is **leaked, never closed** (Win32 handle recycling means a write
   through a recycled handle value lands in an unrelated kernel object). ConPTY children are
   created with `STARTF_USESTDHANDLES` and **null** std handles (`StartupInfoBuilder`, same as
   node-pty/Windows Terminal): without the flag, CreateProcess clones the parent's non-console
   std handles into the child (Win 8.1+ behavior, even with `bInheritHandles=false`), so under
   any parent with redirected stdio — test host, CI runner, the game launched with pipes — the
   shell's text output and stdin silently bypass the pseudoconsole (title/console-API calls still
   work, which makes it look "attached") and the terminal renders empty.

15. **Perf HUD for render diagnostics.** Window menu → "Show performance HUD"
   (`TerminalWindow.ShowPerfHud`, all windows) overlays per-tick numbers: engine write / update /
   populate ms (`GhosttyTerminalSurface.LastFrameStats`), ImGui submit ms, dirty state
   (clean/partial/full/sync-hold), PTY MB/s, and the renderer's draw-call breakdown
   (`GridRenderStats`). Use it before optimizing anything further.

16. **The Unix PTY backend is deliberately fork-free.** `UnixPtySpawner` uses
   `posix_openpt` + `posix_spawnp` (SETSID + addopen of the slave onto stdin → the child acquires
   the pty as controlling terminal on both Linux and macOS; SETSIGDEF/SETSIGMASK reset signal
   state because .NET ignores SIGPIPE process-wide and SIG_IGN survives exec, which would break
   pipelines in the shell). Never switch this to `fork()`/`forkpty()`: forking the multi-GB,
   heavily-threaded game process risks allocator-lock deadlocks in the child and ENOMEM under
   strict overcommit. Hard-won specifics encoded in the implementation:
   - **winsize must be set on a slave fd** (parent opens the slave `O_NOCTTY`, ioctls, closes it
     after spawn) — set on the master before the slave ever opens, it does not stick.
   - **`ioctl` is variadic and Apple arm64 passes variadic args on the stack** — a plain 3-arg
     P/Invoke sends the `winsize*` in x2 and the kernel reads garbage. `UnixPtyNative.WinSizeIoctl`
     pads with 8 named args so the pointer lands at sp on macOS/arm64; Linux uses the plain form.
   - **`DllImport("libc")` does not reliably resolve on glibc** (libc.so is a linker script); a
     ModuleInitializer DllImportResolver maps it to `libc.so.6` / `libSystem.B.dylib`.
   - The output pump (`UnixPtyOutputPump`) mirrors gotcha 14 — no sleeping — but blocks in
     `poll(100ms)` instead of `read` so teardown can cancel without closing the fd under a blocked
     read (`POLLNVAL` also exits the loop); exit is detected by EOF (macOS) / EIO (Linux) after
     the child dies, and a waiter thread `waitpid`s for the real exit code. `CleanupProcess`
     closes the master fd only after **both** the reader and the input-writer threads have joined
     (deliberately leaking the fd if either fails to stop — fd-reuse race), and resets `_pid` so
     `SessionManager.RestartSession` can reuse the manager.
   - Working directory uses `posix_spawn_file_actions_addchdir_np` (glibc ≥ 2.29 / macOS ≥ 10.15)
     with a graceful skip when the entry point is missing.

17. **The drag-selection anchor is a tracked grid ref, never an untracked one.** An untracked
   `GridRef` is a raw page-node pointer valid only until the next mutating engine call — PTY bytes
   flow between Begin and Extend, so scrollback pruning/reflow mid-drag would leave it dangling
   (the original use-after-free class). `GhosttyTerminalSurface` anchors with
   `Terminal.TrackGridRef` → `TrackedGridRef` (binding addition wrapping
   `ghostty_terminal_grid_ref_track` / `ghostty_tracked_grid_ref_*`): the engine moves the pin
   across mutations and relocates it to the oldest surviving content when its page is pruned;
   `ExtendSelectCells` snapshots it per call and ends the drag gracefully if the value is gone.
   The one tracked object is reused across drags via `Set()` (each tracked ref adds bookkeeping
   to every terminal mutation — don't create them per tick).

18. **Every session is ticked every frame, and the inbox is bounded.** The PTY pumps never sleep
   (gotcha 14) and a surface inbox drains only inside `BuildFrame`, so any unticked session grows
   its inbox without bound. `TerminalWindow.Render` ticks **all tabs** (dirty tracking makes quiet
   ones nearly free) and `GhosttyTerminalController.Update` — which `TerminalMod.OnAfterUi` calls
   even while the terminal is hidden — drains hidden windows' sessions at ~4 Hz on the same tick
   thread. Safety nets in `GhosttyTerminalSurface`: the inbox hard-caps at 8 MiB (overflow drops
   incoming bytes and heals the gap with CAN+ST so the VT parser cannot desync; logged once per
   episode), and backlog catch-up feeds the engine at most 1 MiB per tick so re-showing a busy
   terminal cannot stall the render thread on one giant `VTWrite`.

19. **Session create/close side effects stay off the published surface.** Creation: configure new
   sessions (theme, surface events) via `SessionManager.SessionConfigurator`, which runs *before*
   the session is initialized and published — after publication the tick thread may already be in
   `BuildFrame`, and the engine is single-threaded; `CreateSessionAsync` also clones launch
   options before stamping dimensions and re-checks disposal before publishing (a window closed
   during a slow spawn disposes the orphan instead of leaking the PTY). Close:
   `TerminalSession.CloseAsync` completes synchronously on the calling thread — it detaches events
   and disposes the native surface before returning (callers must be on the tick thread;
   `TerminalWindow.CloseSession` deliberately blocks on it) and backgrounds only the PTY/process
   teardown. `Activate`/`Deactivate`/session events are raised outside `SessionManager._lock`.

20. **PTY input is queued, never written on the tick thread.** `IProcessManager.Write` on both
   real backends enqueues into a bounded `PtyInputQueue` (`Pty/Process/PtyInputQueue.cs`; 1 MiB
   cap, overflow drops the chunk and reports once per episode) drained by a dedicated writer
   thread: a PTY write blocks indefinitely while the child is not reading its input (SIGSTOPped/
   XOFF'd child + a large paste), and Write is called on the render tick thread (user input and
   engine `PtyReply` during `BuildFrame`). Write *failures* therefore surface via `ProcessError`,
   not exceptions (only "no process running" still throws synchronously). The fd/handle the
   writer uses is closed only **after the writer thread joins** — join-before-close replaces
   holding a lock across the blocking write, and a stuck pump means the fd/handle is leaked, not
   closed (gotchas 14/16). Each `StartAsync` carries a **generation token**; `CleanupProcess` and
   the exit/waiter callbacks no-op when stale, so a timed-out teardown from a previous run can
   never destroy a freshly restarted session. On Windows the command line is built by
   `ShellCommandResolver.ResolveShellCommandLine` with per-argument quoting
   (`AppendQuotedArgument`, CommandLineToArgvW rules) — never bare-space-join argv; it is the
   same join-then-split corruption the Unix side documents. Contracts pinned by
   `PtyInputContractTests`. The error channel terminates at `TerminalSession`, which subscribes
   `ProcessError` and logs at Warning — without that subscription dropped input would be
   silent, defeating the queue's whole reporting design.

21. **Game integration is failure-isolated; the input gate must never get stuck.**
   `TerminalMod.OnAfterUi` calls `controller.Render()` **unconditionally** (not only while visible):
   `Render()` early-outs when hidden and that early-out is the *only* thing that clears
   `_anyTerminalActive` (the `KSA.Program.OnKey` gate). Calling it only while visible stranded the
   flag `true` after hiding a focused terminal — a total game-keyboard black hole until the terminal
   was shown again. `Patcher.patch()` applies each Harmony patch with its own try/catch
   (`CreateClassProcessor(type).Patch()`) and classifies them required vs optional, so one drifted
   target can never abort the mod or block `InitializeTerminal()`; the menu fallback is a **postfix
   on `Program.DrawProgramMenusHook()`** (KSA's empty public menu-bar extension point — called once
   per frame for `MainViewport` right after the View menu) rather than an IL transpiler. The
   `Harmony` instance is created lazily so a StarMap reload after `unload()` re-patches, and
   `unload()` also resets `Patch02`'s cached ModMenu-presence probe and `Patch01`'s held-key model
   (statics survive a reload without an ALC unload — any cached environment probe must be
   re-evaluated). `Patch01` gates `Program.OnKey` while a terminal is active but **must forward a
   key release only for a key whose press it forwarded** — it tracks the game's held keys in a
   `HashSet<GlfwKey>` (`s_gameHeldKeys`, the keyboard analogue of `TerminalWindow.Input`'s
   `_appMousePressSent`). The reason both halves matter: swallowing a release of a genuinely-held
   movement key strands the game's camera/vehicle controls down (so a key the game saw pressed must
   get its release), **but** unconditionally forwarding *every* release leaks KSA's
   release-triggered toggle hotkeys — `ToggleFps`/`ToggleUi`/`ToggleThreadProfiler`/etc. fire in
   `Program.OnKey`'s `case GlfwKeyAction.Release` arm (F1–F12, Shift+E), so releasing an F-key typed
   into a focused terminal toggled game UI even though the press was correctly swallowed. The
   press-gated rule does both: a key pressed while a terminal was active is never tracked, so its
   release is swallowed too (no toggle leak), while a key pressed game-side then released after focus
   moved to the terminal *is* tracked and released (no stuck controls). NB: in current KSA the game
   only receives input at all when `ImGuiBackend.InputFallthrough` is set — the backend
   (`ImGuiBackendGlfwImpl`) forwards GLFW key/mouse to `Program.OnKey`/`OnMouseButton` **only** while
   the 3D-viewport ImGui window is hovered (`Viewport.DrawImGui` sets it + `SetNextFrameWantCaptureKeyboard(false)`);
   `Patch01` gates `Program.OnKey` itself, so it works regardless of how the game routes input to it.
   `Patch03_HotkeyGuard` null-guards the `Program.ConsoleWindow` static; the toggle hotkey is
   `repeat:false` and skipped while any ImGui text field has focus.

22. **Lock mode = `NoMouseInputs` + a separate hot-zone window; focus visuals are overlays.**
   With `TerminalWindowSettings.LockMode` on, an unfocused window is submitted with
   `ImGuiWindowFlags.NoMouseInputs`: ImGui's hover resolution skips it entirely, so clicks (and
   `WantCaptureMouse`) fall through to the game/UI beneath — and drag-move/resize are off too.
   Specifics that must not regress:
   - **Refocus needs a second window.** A `NoMouseInputs` window cannot capture its own refocus
     click, so `RenderHotZone` submits a tiny decoration-less window (anchored to a corner/side of
     the terminal rect) whose `InvisibleButton` calls `RequestFocus()` on *press*
     (`IsItemActivated`). The click-through check includes `!_wantFocus`, so the same frame the
     focus request is applied the window already accepts mouse input — no dead frame. The zone
     window pushes `WindowMinSize=(1,1)` (a small zone must not leave invisible click-eating
     window area) and its fill is drawn on the **foreground draw list** (visible above the
     terminal background regardless of ImGui z-order).
   - **Re-locking is just focus loss.** Clicking the game world or any other ImGui window
     unfocuses the terminal → next frame it is click-through again; the toggle hotkey / menus /
     hot zone refocus it. The key gate (gotcha 21) follows focus, so a locked unfocused terminal
     never eats game keys. The open grid context menu counts as focus for click-through too
     (the popup steals ImGui window focus — same reasoning as the controller's key gate);
     without it, right-clicking a locked terminal turned it click-through under its own popup.
   - **While click-through, hover must not reveal chrome** (`showChrome` gates on `!clickThrough`)
     — chrome the mouse cannot interact with reads as a bug.
   - **Focus visuals never touch layout** (gotcha 8): the focus/hover border is an
     `AddRect` on the foreground draw list; the unfocused cursor is rendered by
     `FrameGridRenderer` as a steady hollow box (shape forced when `windowFocused` is false).
   - **Cursor style/blink is the engine default, not a frontend override.**
     `Surface.SetCursorStyle` (pushed in `WireSession` and on menu change) sets libghostty's
     default style/blink: it applies immediately while the app has not issued DECSCUSR, an app's
     explicit DECSCUSR still wins, and `CSI 0 q` returns to the configured default (pinned by
     `SetCursorStyle_AppliesAsDefaultAndYieldsToDecscusr`). Blink animation is the controller's
     shared 0.53 s phase, reset to solid on every keystroke (the window's payload-free
     `InputSent` event — deliberately carries no bytes; copying them per keystroke for a
     consumer that ignores them was avoidable garbage).

### Changing terminal/rendering behavior
- Frame production / cell mapping: `purrTTY.Terminal/Ghostty/GhosttyTerminalSurface.cs`
- Drawing: `purrTTY.Display/Ghostty/FrameGridRenderer.cs`
- Window/tab/chrome behavior: `purrTTY.Display/Ghostty/TerminalWindow.cs`; keyboard/mouse/
  clipboard input: `TerminalWindow.Input.cs` (same class — partials)
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

From **another mod**: import the exported contract assemblies (see the Custom shells section),
call `CustomShellRegistry.Instance.RegisterShell(id, factory)` at mod init — registration
probe-instantiates and disposes one instance, so the shell ctor must be trivial and Dispose safe
on a never-started instance — and the shell appears in the New Tab / New Window menus
automatically (live registry enumeration, no purrTTY change needed).

### Deploying the game mod
1. `dotnet build purrTTY.GameMod` — copies the mod DLLs **and the native libghostty-vt** to the mods dir.
   `CopyCustomContent` **wipes the destination `purrTTY/` folder first** (stale DLLs from removed
   projects must never linger in the game's mod ALC) and copies the managed payload by glob
   (`purrTTY.*.dll`) plus the explicit deps (`Ghostty.Vt`, `Tomlyn`, `ModMenu.Attributes`,
   `Microsoft.Extensions.Logging.Abstractions`). `0Harmony.dll`/`StarMap.API.dll` are deliberately
   **not** shipped — the StarMap loader supplies them. `THIRD-PARTY-NOTICES.md` +
   `third-party-licenses/` ship in the mod folder; keep both in sync with what actually ships.
2. Launch KSA; toggle the terminal with the configured hotkey (default F12).

## Code Standards (from Directory.Build.props)
- .NET 10 / C# 13; nullable enabled; warnings-as-errors (except CS1591).
- `purrTTY.Terminal` additionally NoWarns CS1591; the vendored `Ghostty.Vt` relaxes both
  warnings-as-errors and XML-doc generation in its csproj (third-party-derived sources).

## Instruction Maintenance Mandate (MUST)

Whenever you make meaningful repository changes, you MUST evaluate and update this file in the same
work item if it affects: project structure/dependencies, the backend/frontend seam, the engine
binding surface, build/test/deploy commands, or feature status. Remove defunct guidance immediately;
prefer verified code paths over plans when documenting behavior; keep navigation pointers current.
Do not document the deleted bespoke emulator as if it still exists.
