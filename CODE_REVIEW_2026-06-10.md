# purrTTY wide-scale code review — 2026-06-10

Six parallel deep reviews covering: backend (`purrTTY.Terminal` minus `Pty/`), PTY/process layer,
vendored binding (`vendor/Ghostty.Vt`), ImGui frontend (`purrTTY.Display`), game integration
(`purrTTY.GameMod` + custom shells), and build/CI/tests/docs. Every finding below cites file:line
and was verified by reading the actual code paths (and, where relevant, the pinned ghostty source
at `~/repos/github/ghostty` and the decompiled KSA build). Findings marked **[2x]** were found
independently by two reviewers.

Test suite was run during the review: **287 tests, 0 failures, 2 skipped.**

---

## Priority list (what to fix first)

| # | Finding | Severity | Effort |
|---|---------|----------|--------|
| 1 | ~~Game keyboard black hole: `IsAnyTerminalActive` stuck `true` after hiding a focused terminal~~ **FIXED** (D1) | CRITICAL | 1 line |
| 2 | ~~Native use-after-free: surface disposed on pool thread during tab close while tick thread may be in `BuildFrame`~~ **FIXED** (A1) | CRITICAL | small |
| 3 | ~~Double `FreeHGlobal` of env block on `CreateProcess` failure → Windows heap corruption~~ **FIXED** (B1) | CRITICAL | 1 line |
| 4 | ~~Unbounded `_inbox` growth for hidden terminal / inactive tabs~~ **FIXED** (A3) **[2x]** | MAJOR | medium |
| 5 | ~~Cross-thread `SetTheme` on session create races tick-thread `BuildFrame`~~ **FIXED** (A2) **[2x]** | MAJOR | small |
| 6 | ~~Drag-selection anchor is an untracked native `GridRef` — dangles if scrollback prunes mid-drag~~ **FIXED** (A4) **[2x]** | MAJOR | medium |
| 7 | ~~Linux/macOS first run: default shell is PowerShell → dead empty window~~ **FIXED** (C1) | MAJOR | tiny |
| 8 | ~~All-or-nothing Harmony `PatchAll`: one drifted target kills the whole mod~~ **FIXED** (D2) | MAJOR | small |
| 9 | ~~Deployed mod omits `Microsoft.Extensions.Logging.Abstractions.dll`; deploy never cleans stale DLLs~~ **FIXED** (F1/F2) | MAJOR | tiny |
| 10 | ~~ConPTY: handle-close races, exit-path output loss, blocking writes on render thread, unquoted command line~~ **FIXED** (B3–B7) | MAJOR | medium |

---

## A. Threading / lifecycle (the dominant theme)

> **STATUS 2026-06-11: A1–A6 all FIXED** (branch `feature/fable-review`). A1: session close now
> disposes the surface synchronously on the tick thread (`TerminalSession.CloseAsync` completes
> synchronously; `TerminalWindow.CloseSession` no longer uses `Task.Run`). A2:
> `SessionManager.SessionConfigurator` runs theme push + surface wiring pre-publication. A3:
> every tab is ticked each frame, hidden windows drain at ~4 Hz from `Update()` (now called
> unconditionally by `TerminalMod`), inbox hard-caps at 8 MiB with CAN+ST heal on drop, doubling
> overflow fixed, catch-up chunked at 1 MiB/tick. A4: anchor is a tracked grid ref
> (`TrackedGridRef` binding addition wrapping `ghostty_tracked_grid_ref_*`). A5: post-init
> disposal re-check disposes the orphan session. A6: factory disposes surface on construction
> failure, launch options cloned before stamping, Activate/Deactivate + events raised outside
> `_lock` (previous session now Deactivated on create), `GraphemeCache` capped at 64K entries.
> New tests: anchor-pruning + inbox-cap in `GhosttyTerminalSurfaceTests`. See CLAUDE.md gotchas 17–19.

The codebase has a strong single-tick-thread invariant on paper (CLAUDE.md gotcha 1) but several
code paths break it. These share a root cause: session create/close side effects run on
thread-pool threads.

### A1. CRITICAL — Surface disposed off the tick thread during tab close (native use-after-free)
- `purrTTY.Terminal/Sessions/TerminalSession.cs:121-144` (`CloseAsync` → `finally { Dispose(); }` after an `await`), `:202-241`; `Sessions/SessionManager.cs:238`; caller `purrTTY.Display/Ghostty/TerminalWindow.cs:516-529` (`CloseSession` wraps everything in `Task.Run`).
- Closing a tab runs the whole teardown on a pool thread; even without `Task.Run`, the `finally { Dispose(); }` after `await ProcessManager.StopAsync` executes on a continuation thread. `Surface.Dispose()` frees the native terminal/render-state handles while the same render tick that initiated the close can still call `BuildFrame()` on that session (`TerminalWindow.Render` fires `CloseSession` in `RenderTabBar` *before* reading `Sessions.ActiveSession` at :354 and calling `BuildFrame()` at :371). `ThrowIfDisposed` is an entry-only non-volatile bool; nothing prevents free-during-use → AV/heap corruption in the game process on a routine tab close. The comment at `TerminalSession.cs:221` claims tick-thread disposal; callers don't uphold it.
- **Fix:** make surface disposal tick-thread-only and synchronous: detach events + `Surface.Dispose()` before any `await` (or via a synchronous `DetachAndDisposeSurface()`), background only the process teardown. Drop the `Task.Run` in `TerminalWindow.CloseSession`.
- Note: the *mod-unload* path (TerminalMod → controller → windows → SessionManager.Dispose) is synchronous on the main thread and fine — it's the tab-close path that's broken.

### A2. MAJOR **[2x]** — `SessionCreated` fires on a pool thread; `WireSession` calls `Surface.SetTheme` concurrently with tick-thread `BuildFrame`
- `Sessions/SessionManager.cs:146-158` (session published as active *before* events fire, events raised on the creating thread), caller `GhosttyTerminalController.cs:127-140` (`StartSession` = fire-and-forget `Task.Run`), subscriber `TerminalWindow.cs:171, 240-247` → `SetTheme` → native `SetColorPalette`/etc. (`GhosttyTerminalSurface.cs:303-320`).
- The tick thread can already be in `BuildFrame` (`vt_write`/`render_state_update`) on the new surface while the pool thread is inside the native theme setters. Rolls the dice on every New Tab / New Window. Side effects: first frames render default colors; an early clipboard event is lost.
- **Fix:** make session construction theme-complete before publication (pass theme/configure callback into `CreateSessionAsync`), or queue created sessions and wire/theme them at the top of `Render()`, or stash a pending theme applied inside `BuildFrame`.

### A3. MAJOR **[2x]** — Unbounded `_inbox` growth for any session not being ticked
- `GhosttyTerminalSurface.cs:180-193` (`Write`), `:907-921` (`EnsureInboxCapacity`); frontend: `GhosttyTerminalController.cs:293-299` (hidden ⇒ no windows render), `TerminalWindow.cs:354-371` (only the *active* tab gets `BuildFrame`).
- PTY pumps never sleep (by design); the inbox drains only in `BuildFrame`. Toggling the terminal hidden while `htop`/a build runs, or leaving a chatty background tab, grows `_inbox` without bound (hundreds of MB/min possible). Secondary bugs: `newSize *= 2` overflows `int` past 1 GB; on re-show the whole backlog is fed as a single `VTWrite` → multi-second render stall.
- **Fix:** tick `BuildFrame()` for every session of every open window (dirty tracking makes clean sessions nearly free) and drain hidden-controller sessions on a low cadence; add a hard inbox cap as a safety net (drop must be coupled with a parser-state reset); fix the doubling overflow regardless; chunk catch-up writes.

### A4. MAJOR **[2x]** — Drag-selection anchor is an untracked native grid ref held across ticks
- `GhosttyTerminalSurface.cs:105, 243-264` (`_selectionAnchor` from `BeginSelectCells`, reused by `ExtendSelectCells`); binding `Terminal.Selection.cs:60-71`, `GridRef.cs`.
- Verified against pinned ghostty (`src/terminal/c/grid_ref.zig:20-41`): `GhosttyGridRef` is a raw `?*PageList.List.Node` — explicitly the *untracked* variant, valid only until the next mutating op. PTY bytes flow between begin and extend (tick order: `BuildFrame` at `TerminalWindow.cs:371` runs *before* `HandleInput` → extend at :405-408). If scrollback pruning (10 MiB default — seconds of `yes` while dragging) or resize-reflow frees the anchored page, the next extend hands native a dangling node → wrong selection at best, corrupted tracked-pin bookkeeping/crash at worst. Live risk, not latent. The existing test only scrolls the viewport; it never prunes the anchor's page.
- **Fix:** wrap ghostty's tracked-grid-ref API (`ghostty_tracked_grid_ref_*`, present in the pinned lib per `include/ghostty/vt/grid_ref_tracked.h`) and use it for the anchor (`has_value` detects invalidation → end drag gracefully). Stopgap: re-derive the anchor each tick from the engine's installed (natively tracked) selection.

### A5. MAJOR — Closing a window during session spawn leaks a live PTY process
- `SessionManager.CreateSessionAsync` checks disposal only at entry; after `await session.InitializeAsync()` it adds to the (possibly already-disposed/cleared) dictionary with no re-check. WSL spin-up takes seconds — the window can be closed in that gap; the orphan session (shell process, pump threads, native terminal) is never disposed.
- **Fix:** re-check `_disposed` inside the post-init lock; if disposed, dispose the session and throw.

### A6. Minor lifecycle/threading items
- `TerminalSessionFactory.cs:27-35`: surface leaks if `CreateProcessManager` throws (unknown custom-shell ID) — wrap in try/catch + dispose.
- `SessionManager.cs:120-122, 269-271`: `CreateSessionAsync`/`RestartSessionAsync` mutate the *shared* `_defaultLaunchOptions` instance when no options passed (dimension cross-contamination across windows) — clone before stamping.
- `SessionManager.cs:146-158`: creating a session never `Deactivate()`s the previous one; `Activate`/`Deactivate` (which raise `StateChanged`) are invoked under `_lock` — re-entrancy hazard. Raise events outside the lock.
- `GraphemeCache` (`GhosttyTerminalSurface.cs:805-851`) is unbounded for the surface lifetime — cap and clear (~64K entries).

---

## B. PTY / process layer

> **STATUS 2026-06-11: B1–B9 all FIXED** (branch `feature/fable-review`). B1/B2: each native
> allocation now has exactly one free path. B3+B5: all PTY input goes through a new bounded
> `PtyInputQueue` (1 MiB cap, drop+report on overflow) drained by a dedicated writer thread on
> both backends — the render tick thread never issues the blocking write; the fd/handle is closed
> only after the writer thread joins (join-before-close replaces lock-across-write), and is
> deliberately **leaked** if a pump thread fails to stop (fd/handle-reuse hazard). B4: exit
> handler snapshots `Id`/`ExitCode` defensively. B6: ConPTY teardown closes the pseudoconsole
> first (conhost flushes + breaks the pipes), waits for the output pump to drain (≤2 s), then
> closes the pipe handles. B7: `ResolveShellCommandLine` quotes the shell path and every argument
> per Windows argv rules (`AppendQuotedArgument`); the bare-space `ResolveShellCommand` is gone.
> B8: both managers carry a per-start generation token; stale teardown (timed-out waiter, late
> `Exited` callback) no-ops after a restart. B9: `WrapProcessHandle` handles an already-exited
> child without leaking the raw handles; read-token-under-lock + `_starting` double-start guard
> in both managers; env block merged case-insensitively and sorted (CreateProcessW contract),
> creation failures logged not swallowed; `Auto` on Windows offers WSL only when ≥1 distribution
> is detected; `CustomShellPtyBridge.Write` observes faults of the fire-and-forget write;
> `POLLNVAL` exits the Unix pump loop; initial `TIOCSWINSZ` failure is logged; dead
> `CleanupHandles`/`CleanupPseudoConsole`/`ValidateProcessRunning` helpers deleted. New
> cross-platform tests: `PtyInputContractTests` (argv quoting incl. the space-containing-arg
> case from F7.3, queue ordering/overflow/failure semantics). See CLAUDE.md gotcha 20.

### B1. CRITICAL — Double `Marshal.FreeHGlobal` of the environment block on CreateProcess failure
- `purrTTY.Terminal/Pty/ProcessManager.cs:149-175`: catch frees `envBlock` (:160-163), then the `finally` frees it again (:171-174) before the rethrow. `envBlock` is non-null in every real launch (`ProcessLaunchOptions.CreateDefault()` always sets TERM etc.). Any `CreateProcessW` failure (bad shell path, broken WSL, AV interference) double-frees → silent native heap corruption that can surface far away.
- **Fix:** delete the free in the catch (finally covers it) or null the pointer after freeing.

### B2. MAJOR — Attribute-list double-free on failure paths
- `Pty/Process/AttributeListBuilder.cs:29-66`: failure branches free `attributeList` (:32, :48) and throw; the enclosing catch (:54) frees the same pointer again (:59). Same fix pattern as B1.

### B3. MAJOR — ConPTY `Write`/`Resize` race handle close (use-after-close on recycled Win32 handles)
- `ProcessManager.cs:247-259, 294-320` read `_inputWriteHandle`/`_pseudoConsole` *outside* `_processLock` while `CleanupProcess` (:384-413, called from the threadpool `Exited` handler / `StopAsync` / `Dispose`) closes them under it. A recycled handle value means `WriteFile` can write terminal input into an unrelated kernel object. Trigger: typing or a DSR reply landing while the shell exits.
- **Fix:** mirror the Unix manager's `_writeLock` discipline (re-validate the field under the lock — `UnixProcessManager.cs:223-233` is the correct pattern), or move to `SafeHandle`s.

### B4. MAJOR — `HandleProcessExited` can throw on a disposed `Process` → unhandled threadpool exception → game crash
- `Pty/Process/ProcessEvents.cs:26-36` reads `process.ExitCode`/`Id` with no try/catch; `CleanupProcess` (`ProcessCleanup.cs:16-26`) can dispose the `Process` while the `Exited` callback is already in flight. Races on every tab close on Win11-era conhost.
- **Fix:** snapshot PID at start; read exit code defensively (catch `InvalidOperationException`/`ObjectDisposedException`) or via the process handle before disposal.

### B5. MAJOR — Synchronous PTY writes on the game render thread can freeze the game
- `UnixProcessManager.cs:203-234, 347-372`; `Pty/Process/ConPtyInputWriter.cs:21-48`. `Write` is called on the tick thread (`TerminalWindow.Send` → `SendInput`; also `PtyReply` during `BuildFrame`). `write(2)` on a pty master blocks when the slave input queue is full (child busy/SIGSTOPped/XOFF'd + a large paste) — render thread blocks indefinitely; on Unix it also holds `_writeLock` so resize/teardown queue behind it. Nothing throws, so the render-loop try/catch can't help.
- **Fix:** bounded per-manager input queue drained by a dedicated writer thread (mirror of the output pump), drop/error on overflow.

### B6. MAJOR — ConPTY exit path doesn't drain output before closing (tail output lost)
- `ProcessManager.cs:384-399`: on exit, cancels CTS, `ClosePseudoConsole`, closes the read handle — without waiting for `_outputReadTask`. The final prompt / a short command's entire output can be truncated; also closes the handle under a possibly-blocked `ReadFile` (the same recycling-race class the Unix side documents and avoids).
- **Fix:** `ClosePseudoConsole` → `outputReadTask.Wait(~2000)` (broken pipe ends the pump loop) → then close the read handle.

### B7. MAJOR — `ResolveShellCommand` builds an unquoted CreateProcess command line
- `Pty/Process/ShellCommandResolver.cs:16-21` + `ProcessManager.cs:145-146`: bare-space join, no quoting, `lpApplicationName = null`. Breaks: `pwsh` → `C:\Program Files\...` (unquoted-path hazard); any arg with spaces (`--cd C:\Users\John Smith`) is re-tokenized by the child — the exact join-then-split bug the Unix-side docs warn about, live on Windows.
- **Fix:** quote the shell path; apply standard Windows argv quoting per argument.

### B8. MAJOR (latent) — Stale teardown can destroy a freshly restarted session (both backends)
- `UnixProcessManager.cs:303-337, 379-435`; `ProcessManager.cs:360-399`. `CleanupProcess` acts on *current* fields with no spawn-generation notion: a timed-out old waiter can cancel the new CTS / close the new fd after `RestartSessionAsync` installs them. No production caller of restart today, but it's shipped public API.
- **Fix:** per-start generation token; `CleanupProcess(generation)` no-ops if superseded; `StopAsync` waits for the waiter task before allowing restart.

### B9. Minor PTY items
- `ProcessLifecycleManager.cs:200-211`: `WrapProcessHandle` re-attaches by PID (`GetProcessById` throws if child already exited → leaks `hProcess`/`hThread`; PID-reuse mis-attach theoretically possible). Construct around the existing handle.
- `ProcessManager.cs:186-190`: `_readCancellationSource.Token` read outside the lock → NRE/ODE on start/exit race. Both managers also have a check-then-act double-start TOCTOU.
- `UnixProcessManager.cs:402-429`: after a timed-out reader wait, the master fd is closed anyway — exactly the fd-reuse hazard the code documents. Prefer leaking one fd or flag the pump's fd invalid.
- `UnixPtySpawner.cs:77`: initial `TIOCSWINSZ` result discarded — log on failure.
- `ProcessLifecycleManager.cs:145-192`: env block not sorted (CreateProcessW docs require it); case-sensitive Hashtable merge can yield `Path` vs `PATH` dupes; blanket catch silently drops TERM/COLORTERM.
- `ShellCommandResolver.cs:53-71`: `Auto` on Windows tries WSL first on executable-existence alone — contradicts the project's own "wsl.exe presence ≠ working WSL" rule. Gate on `WslDistributionDetector` or demote below PowerShell.
- `CustomShellPtyBridge.cs:158-167`: `Write` is fire-and-forget (`_ = WriteInputAsync`) — fine for `BaseLineBufferedShell` (synchronous), but third-party async shells get swallowed faults and no ordering guarantee. Attach a fault-logging continuation; document the expectation.
- `UnixPtyOutputPump.cs:66-69`: `POLLNVAL` not in the exit mask → 10 Hz spin until cancel. `ProcessManager.CleanupHandles` (:418-426) is dead code.

### Verified solid
`UnixPtyNative.cs` is excellent (constants independently verified: TIOCSWINSZ/O_NOCTTY/POSIX_SPAWN_SETSID per-OS; variadic-ioctl workaround correctly gated; blittable structs; EINTR retried on read/write/poll/waitpid; correct errno vs return-code handling; libc resolver). `UnixPtySpawner` failure paths close fds on every branch; no zombie risk. Both output pumps honor gotcha 14. Detection layer (ShellAvailabilityChecker / WslDistributionDetector / UnixShellDetector) is thread-safe, bounded, and robust.

---

## C. Frontend (purrTTY.Display)

> **STATUS 2026-06-11: C1–C4 all FIXED** (branch `feature/fable-review`). C1: default shell (and
> unparsable-string fallback) is now `ShellType.Auto` in both config sections; a failed session
> start is reported via `TerminalWindow.NotifySessionStartFailed` and rendered in the otherwise
> blank window (with the error logged at Error, not Debug). C2: `SetMouseGeometry` now receives
> `cols * (int)cellWidth` and `HandleAppMouse` synthesizes event positions from the
> frontend-computed cell center in integer metrics, so frontend and engine agree by construction.
> C3: the modified-key encode loop runs for Ctrl **or** Alt and now covers digits and
> Ctrl+Space/[/\/] (`DigitKeys`/`ControlPunctuationKeys`). C4: `SessionManager.Sessions` is a
> cached snapshot invalidated on add/remove; tab labels are cached per session;
> `ITerminalSurface.HasSelection` (cheap native `GHOSTTY_TERMINAL_DATA_SELECTION` probe, binding
> addition `Terminal.HasSelection`) replaces full-text extraction in the context menu; the
> redundant per-frame `ComputeCellMetrics` is gone; app-mouse releases are sent only when the
> matching press was forwarded; astral-plane input pairs surrogates; Shift overrides app mouse
> tracking (xterm behavior) so selection/copy works inside tmux/nvim; wheel magnitude sends one
> scroll report per notch; an open grid context menu keeps the game-key gate engaged
> (`TerminalWindow.IsContextMenuOpen`); block-cursor glyph redraw honors `foregroundOpacity` and
> the block-element rect path; decorations skip `Invisible` cells; curly/dotted/dashed underlines
> render distinctly; `IsAsciiMonospace` validates ' '; selection bg composites *over* cell
> backgrounds (own alpha, unaffected by `CellBackgroundOpacity`); user themes store their display
> name in `[meta] name` (round-trips through filename sanitization); config/theme writes are
> atomic (`AtomicFile`: temp + rename); restored/cascaded geometry is clamped to the viewport
> work area; the dead legacy files listed below (~1,900 lines incl. the phantom
> `ITerminalController` selection/resize APIs, `TextSelection`, `LayoutConstants` trimmed to the
> font-size bounds) are deleted and `PurrTTYFontManager` was rewritten (read-only `LoadedFonts`,
> cached family list, dead locals/"TestApp" strings gone). Deliberately not done: alt-screen
> wheel→arrow-key translation (nicety; needs an alt-screen probe on the seam). New test:
> `HasSelection_TracksSelectionLifecycle`.

### C1. MAJOR — Fresh install on Linux/macOS produces a dead, empty terminal window
- `Configuration/ThemeConfiguration.cs:657` defaults `DefaultShellType = ShellType.PowerShell`; the unparsable-string fallback (:665-673, :752-759) is `ShellType.Wsl`. With no config: F12 → `OpenWindow` → `ResolvePowerShell` throws on Unix → fire-and-forget `StartSession` logs at Debug and gives up; the window stays open and permanently empty (auto-close requires `_hadSessions`). Directly undermines the pending in-game Linux validation.
- **Fix:** default to `ShellType.Auto` (resolves `$SHELL`/zsh/bash/sh on Unix; sensible on Windows), make the parse fallback `Auto` too, and surface session-start failure in the window instead of a Debug log line.

### C2. MAJOR — App-mouse reports land on the wrong cell whenever the cell width is fractional
- `TerminalWindow.cs:367-368` pushes truncated *integer* cell metrics to the engine; `HandleAppMouse` (:679-746) sends raw float pixels. Verified in pinned ghostty (`src/renderer/size.zig:140-145`) that the encoder divides by the integer cell size. `_cellWidth` from `CalcTextSize("M")` is routinely fractional (e.g. 9.6px) → with cw 9.6→9, a click at column 40 reports column 42; error grows with x. nvim/tmux/htop clicks hit the wrong cell on wide grids.
- **Fix:** synthesize event positions from the frontend-computed cell (`col * (int)cw + (int)cw/2`, same for y) so frontend and engine agree by construction; use `cols * (int)_cellWidth` in `SetMouseGeometry`.

### C3. MAJOR — Alt-modified keys are swallowed entirely
- `TerminalWindow.cs:568-581`: the letter-key encode loop runs only under `io.KeyCtrl`; queued characters are skipped when `io.KeyAlt` is held. Alt+B/F/D (readline), Emacs Meta, MC shortcuts produce *no bytes at all*.
- **Fix:** run the loop for `io.KeyCtrl || io.KeyAlt`; consider adding digits and Ctrl+Space/`[`/`\`/`]`.

### C4. Minor frontend items
- Per-frame allocations on the render path: `SessionManager.Sessions` LINQ-allocates per access (hit every frame by `RenderTabBar`); tab labels + `Guid` formats allocate per tab per frame; `DrawContextMenu` extracts the *full selection text* every popup frame just for an enable-bool (add a cheap `HasSelection` to `ITerminalSurface` — native no-value probe is free); redundant per-frame `ComputeCellMetrics` (`TerminalWindow.cs:347`).
- Spurious app-mouse Release events (press is hover-gated, release isn't — `TerminalWindow.cs:697-720`): track "press forwarded" per button.
- Astral-plane input corrupted: each UTF-16 unit encoded independently (`:581-591`) → surrogate halves become U+FFFD. Pair surrogates.
- No Shift-override selection / context menu while an app tracks the mouse (`:596-599`) — the only copy path requires a selection you cannot make inside tmux/nvim. Add the xterm/ghostty Shift bypass.
- Wheel magnitude ignored in app-mouse mode (`:741-745`); alt-screen non-tracking wheel could translate to arrow keys (`less` nicety).
- Game hotkeys re-enable while the grid context menu is open (popup steals focus → `_anyTerminalActive` drops): treat `IsPopupOpen(GridContextMenuId)` as focus-equivalent.
- Renderer nits: block-cursor glyph redraw ignores `foregroundOpacity` and bypasses the block-element rect path (`FrameGridRenderer.cs:543-549`); decoration pass draws underlines for `Invisible` cells; curly/dotted/dashed underlines render as plain lines (`:349-359`); `IsAsciiMonospace` doesn't validate ' ' though run-bridging inserts spaces; selection bg *replaces* the cell bg (composite instead) and becomes invisible at `CellBackgroundOpacity = 0`.
- Theme save name/file round-trip mismatch (`ThemeCatalog.cs:64-72,138-143`): sanitized filename vs name-from-filename → saved theme doesn't resolve next launch. Validate names or store the display name in the TOML.
- Non-atomic config/theme writes (`ThemeConfiguration.cs:539-560`, `ThemeTomlFormat.cs:159-160`): temp file + `File.Replace`.
- Restored/cascaded window geometry never clamped to the screen — can restore fully off-screen with chrome hidden (nothing visible to grab). Clamp to viewport work rect.
- ~1,900 lines of verified-dead legacy code: `Utils/ClipboardManager.cs`, `Utils/CoordinateConverter.cs`, `Performance/PerformanceStopwatch.cs`, `Configuration/MouseWheelScrollConfig.cs`, `FontContextDetector.cs`, `DpiContextDetector.cs` (+`TerminalRenderingConfig.cs`), `Controllers/TerminalSettings.cs`, most of `LayoutConstants`; on `ITerminalController`, `GetCurrentSelection`/`SetSelection` are a phantom selection API and `ResizeTerminal`/`GetTerminalDimensions` have no callers. Per the CLAUDE.md maintenance mandate, delete.
- `PurrTTYFontManager` hygiene: dead locals, "TestApp" log strings, public mutable static `LoadedFonts`, per-call list allocation in a menu-draw path.

### Verified solid
Seam integrity (no `Ghostty.Vt` types in Display — grep-verified). Chrome hiding is genuinely layout-stable (verified against upstream imgui source that `WindowBorderSize` doesn't move the content region). Mouse-button remap and motion gating correct. Resize-snap has no feedback loop. Renderer batching correctness (run merges, wide cells, block-element rect math, ABGR packing) checks out. Clipboard edge cases and OSC 52 thread placement fine. Multi-window ImGui IDs collision-free; gotcha-6 single-config-instance honored everywhere traced. Theme TOML loading has per-file failure isolation.

---

## D. Game integration (purrTTY.GameMod + custom shells)

> **STATUS 2026-06-11: D1–D5 all FIXED** (branch `feature/fable-review`). D1:
> `TerminalMod.OnAfterUi` now calls `controller.Render()` unconditionally — its hidden early-out
> is what clears `_anyTerminalActive`, so the game-key gate can no longer stick `true` after
> hiding a focused terminal. D2: `Patcher.patch()` applies each patch independently via
> `CreateClassProcessor(type).Patch()` with a per-class try/catch, classified required (`Patch01`,
> `Patch03_HotkeyGuard`) vs optional (`Patch02`, `ConsoleWindowPrintPatch`); failures are logged at
> Error and never abort the mod or block `InitializeTerminal()` (which now also logs init failure at
> Error). D3: the fragile `DrawMenuBar` IL transpiler is replaced by a `[HarmonyPostfix]` on KSA's
> empty public `Program.DrawProgramMenusHook()` menu-bar extension point (verified present in the
> pinned KSA build, called once per frame for `MainViewport` right after the View menu; sets
> `MainViewport.MenuBarInUse`). D4: the Harmony instance is recreated lazily (`m_harmony ??= new
> Harmony(...)`) so a reload after `unload()` nulled it still patches. D5: `Patch01` forwards key
> *releases* even while gated (held-key state no longer sticks); `Patch03_HotkeyGuard` null-guards
> the `Program.ConsoleWindow` static; the toggle hotkey uses `IsKeyPressed(repeat:false)` and is
> suppressed while a text field has focus; the inherent game-console `\r\n`/capture-extent limits
> are documented on `GameConsoleShell.OnConsolePrint`. See CLAUDE.md gotcha 21.

### D1. CRITICAL — `IsAnyTerminalActive` gets stuck `true` when the terminal is hidden while focused → game keyboard black hole
- `GhosttyTerminalController.cs:293-338` (flag written only inside `Render()`), `TerminalMod.cs:436-440` (`Render()` called only while visible), `Patcher.cs:113-121` (consumer).
- F12-open → type → F12-hide: `Render()` never runs again, flag stays `true`, `Patch01` returns false for every `KSA.Program.OnKey` — verified against the decompiled KSA build that this is the game's *entire* keyboard pipeline (GameSettings.OnKeyAll, popups, console hotkey, camera, vehicle controller). Game keyboard is dead until the terminal is shown again.
- **Fix (one line):** clear the flag in the `IsVisible` hide path — or call `_controller.Render()` unconditionally from `OnAfterUi` and rely on its early-out (which handles invisibility correctly).

### D2. MAJOR — All-or-nothing patching: one drifted patch target silently kills the entire mod
- `TerminalMod.cs:463-477` runs `Patcher.patch()` (→ `PatchAll`) *before* `InitializeTerminal()` in one try/catch. `ConsoleWindowPrintPatch` targets the Brutal-version-sensitive `ConsoleWindow.Print(ReadOnlySpan<char>, ImColor8, int)`; if it drifts again, the `HarmonyException` aborts `OnFullyLoaded` before init — no terminal at all, Debug-level log only. The mod also relies on the StarMap loader supplying `0Harmony.dll` (compiled against 2.4.2, `PrivateAssets=all`) — a loader-side Harmony bump lands in this same failure mode.
- **Fix:** patch per-class with per-class try/catch (`harmony.CreateClassProcessor(type).Patch()`), classify Patch01/Patch03 required vs ConsoleWindowPrintPatch/Patch02 optional, run `InitializeTerminal()` regardless of optional failures, log visibly.

### D3. MAJOR — `Patch02` transpiler fragility (menu fallback)
- `Patcher.cs:37-62`: injects at "4 instructions after the last `EndMenu`" with no validation — IL reshuffle can throw (whole-mod death via D2) or corrupt the eval stack (`InvalidProgramException` → game menu bar dies); a new top-level game menu silently misplaces it; the no-match path logs nothing.
- **Better fix:** the current KSA build ships an empty public `Program.DrawProgramMenusHook()` called right after the View menu (decomp `KSA/Program.cs:3312/3323`) — an intentional extension point. A plain `[HarmonyPostfix]` there replaces the transpiler entirely. At minimum add not-found logging + pattern validation.

### D4. MAJOR — Patch idempotency on mod reload: `m_harmony` nulled forever
- `Patcher.cs:17, 24-28`: `unload()` sets `m_harmony = null`; on a StarMap reload without ALC unload, `patch()` becomes `null?.PatchAll(...)` — silent no-op (no input gating, no menu, no capture). **Fix:** `m_harmony ??= new Harmony("purrTTY")`.

### D5. Minor integration items
- `Patch01` drops key-*release* events at activation boundaries — held-key state (camera/vehicle) can stick. Keep forwarding Releases while gated.
- Toggle hotkey: `ImGui.IsKeyPressed(Key)` defaults `repeat: true` (verified in Brutal decomp) → holding the hotkey rapid-toggles; printable-key bindings fire while typing in *any* foreign ImGui text field. Pass `repeat: false`; skip when `WantTextInput` (outside the capture modal).
- `Patch03` NREs if `Program.ConsoleWindow` is unassigned when `OnKeyAll` fires (add null guard); note the prefix suppresses the original body game-wide whenever any ImGui text field has focus — broader than the comment implies.
- Game-console capture: every captured `Print` gets `\r\n` (multi-segment colored lines get spurious breaks); capture window is the synchronous extent of `Execute` — async command output is lost and unrelated cross-thread prints during `Execute` are mis-attributed. Inherent; document it. Patch-absent behavior is graceful.

### Verified solid
`ShellMenuCache`: volatile immutable snapshot, `Interlocked.Exchange` start guard, background exceptions observed with a usable fallback, WSL gated on ≥1 distro, draw path never detects. `GameConsoleShell` output threading is correct end-to-end (postfix → lock → Channel → single-reader pump → `Surface.Write`, the one thread-safe member). `CustomShellRegistry` is robust to dupes/throwing types. All four patch target signatures verified to match the current pinned KSA build. Unload path detaches events before disposal; no event leaks into game objects found.

---

## E. Vendored binding (vendor/Ghostty.Vt)

All verified against the pinned upstream commit (`7092b394` — the local ghostty checkout matches the pin).

> **STATUS 2026-06-11: E1–E6 all FIXED** (branch `feature/fable-review`). E1: `Paste.Encode`
> copies its input into a mutable scratch (`data.ToArray()`) before handing it to
> `ghostty_paste_encode`, which mutates the buffer in place — a read-only span (e.g. a `u8`
> literal) can no longer fault. E2: the Enquiry/Xtversion return-value callbacks keep a persistent
> per-terminal `GCHandle` pin (`PinReply`), replaced on the next call and freed in `Dispose`, so
> the native trampoline never reads a buffer that was unpinned before it returned. E3:
> `Sys.SetLog` now uses the correct ordinal (`LOG = 2`; added `USERDATA = 0`) so engine logs reach
> the sink instead of being silently dropped. E4: `RawCellLayout.Validate` now also writes a
> 256-color palette background, cross-checks `StyleId` against `CELL_DATA_STYLE_ID` and
> `BgPaletteIndex` against `CELL_DATA_COLOR_PALETTE`, and fails loudly if the styled / bg-rgb /
> bg-palette branches did not each run (no more blind-pass on drifted content-tag bits). E5:
> `Formatter.ToSpan` copies the engine-allocated buffer into a managed array and `ghostty_free`s
> the native one (no leak); `Formatter.ToString` heap-allocates above a 1 KiB stack threshold (no
> unbounded `stackalloc` → `StackOverflowException`). E6: kitty `SetLayer` uses option ordinal `0`
> (was `1` → INVALID_VALUE); the placement-iterator ctor frees the iterator if the bind call
> fails; `KeyEvent`/`MouseEvent` null their handle after free (double-Dispose no longer
> double-frees); the dead-and-wrong `Enums/TerminalOption.cs` is deleted; `Terminal.Resize`
> surfaces its `GhosttyResult` via `ThrowIfFailure` instead of swallowing it; and
> `ghostty_mouse_event_set_mods` is declared `ushort` to match the native `uint16_t`.
> Full suite after the changes: **295 passed, 2 skipped, 0 failed** (incl.
> `RawCellLayout_MatchesNativeAccessors` exercising the strengthened E4 validation against the
> real native lib).

### E1. MAJOR — `Paste.Encode` passes a `ReadOnlySpan` to a native API that mutates it in place
- `src/Paste.cs:15-32`. `ghostty_paste_encode`'s first param is documented "modified in place" (`include/ghostty/vt/paste.h:77`) and `src/input/paste.zig` really writes into it (control bytes → spaces; `\n`→`\r`). The binding pins the caller's read-only span and hands it over mutable. Latent (purrtty always passes a fresh array today), but one `"…"u8` literal away from an AV on a read-only page.
- **Fix:** copy into a mutable scratch inside `Encode`.

### E2. MAJOR — Enquiry/Xtversion callbacks unpin the buffer before native reads it
- `src/Terminal.cs:70-77, 92-99`: `gc.Free()` runs before the callback returns, but the native trampoline (`src/terminal/c/terminal.zig:177-185`) reads the memory *after* return. GC in that window → dangling read into the PTY reply. Latent (callbacks unused) but the exact use-after-scope class of gotcha 3.
- **Fix:** persistent pinned (or POH) buffer per terminal, freed in `Dispose`.

### E3. MAJOR — `Sys.SetLog` uses the wrong option ordinal; engine logs are silently discarded
- `src/Sys.cs:31-32`: defines `SYS_OPT_LOG = 0`, but the header (`include/ghostty/vt/sys.h:136-167`) says `USERDATA = 0`, `LOG = 2`. `SetLog` actually sets the global *userdata* to a function pointer; no log sink is ever installed. **Fix:** `LOG = 2`, add `USERDATA = 0`.

### E4. MAJOR — `RawCellLayout.Validate` doesn't fully enforce its tripwire role
- `src/RenderState.FrameReader.cs:345-347`: the bg-rgb branch is claimed mandatory but no flag enforces it ran — drifted content-tag bits would skip the check silently. `StyleId` never compared numerically against native data 6; the `TagBgColorPalette` path (`BgPaletteIndex`, used by `FillCell`) never exercised.
- **Fix:** track `sawBgRgb` and fail if absent; compare `StyleId` against `CELL_DATA_STYLE_ID`; add a palette-bg case (`\x1b[42m\x1b[K`) vs `CELL_DATA_COLOR_PALETTE`.

### E5. MAJOR (latent, unused) — `Formatter.ToSpan` leaks every native buffer; `ToString` stackallocs unbounded
- `src/Formatter.cs:85, 93-105`: alloc'd buffer must be `ghostty_free`d — `ToSpan` never frees and hides the pointer; `ToString` does `stackalloc byte[(int)written]` where `written` can be the whole scrollback → uncatchable `StackOverflowException`. Unused by purrtty (selection copy uses the correct `GetSelectionText` path). Fix before first use: heap buffer; make `ToSpan` copy-and-free.

### E6. Minor binding items
- `KittyGraphics.cs:199-201`: `SetLayer` passes option `1`; header says `LAYER = 0` — native returns INVALID_VALUE, discarded → layer filter never applies. Iterator ctor also leaks if the bind call throws (:177-185).
- `KeyEvent.cs:63-67` / `MouseEvent.cs:61-65`: double-`Dispose` double-frees (handle never cleared, no SafeHandle/finalizer). Latent under current usage.
- `src/Enums/TerminalOption.cs` is dead *and wrong* (bears no relation to the real native ordinals) — a public footgun; delete.
- `Terminal.cs:395-402`: `Resize`'s `GhosttyResult` swallowed — silent grid/PTY desync on failure. Worth checking; other ignored results are benign (zero-init out-buffers).
- `NativeMethods.cs:238`: `set_mods` declared `int` vs native `uint16_t` — benign on supported ABIs, only width mismatch in the file.
- `NativeLibraryResolver` selects by OS only — osx-x64/linux-arm64 get `DllNotFoundException` (consistent with the declared 3-RID matrix; documented).
- Upstream doc note: the `max_scrollback` header comment says "lines" but `Screen.zig:282` confirms **bytes** — the binding's doc is right, upstream's header is wrong.

### Verified solid
RawCell bit decode is exactly right vs `page.zig`'s `packed struct(u64)` (content_tag/content/style_id@26/wide@42, RGB r-in-LSB). The reused RowCells handle is the *documented* native pattern (`render.h` blesses it; native repopulates in place). ClearDirty/ClearRowDirty ordinals and types match `render.zig`; symbols confirmed in the vendored dylib via `nm`. Every struct layout checked byte-for-byte against the headers (16 of them, incl. the 792-byte colors struct). Every enum ordinal in use checks out. Owned-bytes contract honored in both encoders and `GetSelectionText`; callback delegates rooted for the terminal lifetime.

---

## F. Build / CI / tests / docs

> **STATUS 2026-06-11: F1–F6 all FIXED** (branch `feature/fable-review`); **F7 (test coverage
> gaps) deliberately left open.** F1+F2: `CopyCustomContent` now wipes the destination `purrTTY/`
> folder before copying and copies the managed payload by glob (`purrTTY.*.dll` + explicit deps
> incl. `Microsoft.Extensions.Logging.Abstractions.dll`); release notes tell players to delete
> the old folder before unzipping. F3: branch-derived values are validated against
> `^[0-9A-Za-z._-]+$` (with a non-empty BASE guard) and passed to run scripts via `env:` — no raw
> `${{ }}` interpolation remains in any run script. F4: tests now run as a matrix on
> ubuntu-latest / windows-latest / macos-14 in **Release** config (each runner loads its own
> vendored native lib — win-x64 and osx-arm64 are no longer ship-blind), and the publish job
> `needs` the matrix. F5: `THIRD-PARTY-NOTICES.md` now indexes everything that ships (binding,
> native engine + statically-linked highway/simdutf, Tomlyn, M.E.L.A, Harmony note, 7 font
> families, iTerm2-Color-Schemes) and `third-party-licenses/` gained `LICENSE.highway`,
> `LICENSE.highway-BSD3`, `LICENSE.simdutf`, `LICENSE.iterm2-color-schemes`; the notices file
> ships in the mod folder. F6: the unused `Lib.Harmony` ref was **removed** from CustomShells
> (GameMod's 2.4.2 is now the only reference — usage was comments only); `dorny/test-reporter`
> pinned to the v3 commit SHA; CI triggers added for `fix/**`, `chore/**`, and `pull_request`
> into main; `cancel-in-progress` disabled on `release/*` refs; `timeout-minutes: 30` on both
> jobs; KSA DLL misresolution now fails with one actionable error (`ValidateKSAAssemblies` target
> + `RequiresKSAAssemblies` in the four KSA-referencing csprojs); stale `InternalsVisibleTo`
> (Display.Tests) and the copy-pasted Logging csproj description fixed; `REPO_INDEX.md` deleted;
> `LIBGHOSTTY_ANALYSIS.md` status updated to implemented; `README.md` rewritten for the
> libghostty-vt reality; local bin/obj husks of deleted projects and the stale local `dist/`
> removed. Full suite after the changes: 295 passed, 2 skipped, 0 failed.

### F1. MAJOR — Deployed mod omits `Microsoft.Extensions.Logging.Abstractions.dll`
- `purrTTY.Logging.csproj:17` references M.E.L.A 10.0.0; `ILogger` is used in shipped hot code. The hard-coded copy list in `purrTTY.GameMod.csproj:24-35` doesn't copy it — confirmed absent from the real deployed mod folder while `deps.json` declares it. Works only because the game/host happens to supply a compatible copy; a KSA update that drops/bumps it breaks the mod with `FileNotFoundException`. **Fix:** add it to `CopyCustomContent` (and prefer a glob over the hand-maintained list — this omission is exactly what the hand list produces).

### F2. MAJOR — `CopyCustomContent` never cleans the destination
- Proof in-repo: stale local `dist/purrTTY/` still contains pre-migration `purrTTY.Core.dll` and `purrTTY.TermSequenceRpc.dll` beside current DLLs. A player unzipping a new release over an old install keeps deleted DLLs in the game's mod ALC forever. **Fix:** wipe/clean known-obsolete files in the target; mention "remove the old folder" in release notes.

### F3. MAJOR — release.yml interpolates branch-derived values raw into run scripts
- `release.yml:89` (`modversion` into a `sed` program) and `:126` (`version` into `zip`). A `release/1.0/rc1` branch breaks the sed delimiter; git refs may contain `$`/backticks → shell-injectable in a `contents: write` job. **Fix:** pass outputs via `env:`; validate `NAME` against `^[0-9A-Za-z._-]+$` in the meta step.

### F4. MAJOR — Windows/ConPTY and the non-linux native binaries have zero automated coverage
- No Windows CI job; `ProcessManager`/`ConPty*` are play-tested only; `win-x64`/`osx-arm64` natives are never loaded in CI — `RawCellLayout.Validate()` runs only against the linux `.so` there. After a pin bump, a broken win-x64 binary ships undetected. **Fix:** add `windows-latest` (and optionally `macos-14`) test jobs — the KSA-assemblies/`KSA_DLL_DIR` tiers are already host-agnostic, so it's mostly a matrix change.

### F5. MAJOR — `THIRD-PARTY-NOTICES.md` materially incomplete vs what ships
- Lists only the binding + libghostty-vt. The zip also ships `Tomlyn.dll` (BSD-2), seven Nerd-Font families, 18 themes derived from `mbadolato/iTerm2-Color-Schemes` (MIT — credited in README but no license text ships), and the native lib statically links **highway (Apache-2.0 — NOTICE obligation)** and **simdutf**. **Fix:** index everything in `third-party-licenses/`, add the iTerm2-schemes license, add native static-dep notices.

### F6. Minor build/CI items
- Lib.Harmony version skew: CustomShells pins 2.3.3, GameMod 2.4.2 — align.
- `dorny/test-reporter@v3` pinned by mutable tag with `checks: write` — pin to SHA.
- No CI on `fix/*`/`chore/*` branches and no `pull_request` trigger; merge results tested only post-merge.
- `cancel-in-progress: true` applies to `release/*`: a re-push during Publish can cancel between `gh release delete` and `create`, leaving the release missing until the next push.
- No `timeout-minutes` on the job (hung pty test burns 6 h). `BASE` extraction (:63) needs a non-empty guard. Tests run Debug; dist is Release (compile-only Release coverage).
- KSA DLL resolution failure produces a CS0246 avalanche instead of one actionable `<Error>` message — add a guard.
- Stale: `InternalsVisibleTo` to deleted `purrTTY.Display.Tests` (`purrTTY.Display.csproj:31-34`); `purrTTY.Logging.csproj:7` description is copy-paste from the deleted emulator; bin/obj husks of deleted projects linger locally; `LIBGHOSTTY_ANALYSIS.md:22` still says "proposal"; `REPO_INDEX.md` is empty; root `README.md` still describes the bespoke "(mostly) VT100" emulator with no mention of libghostty-vt.
- Release gating itself verified correct (test failure blocks publish; reporter can't mask failures; tip pruning sound; PAT handling good).

### F7. Test coverage gaps (untested documented invariants)
1. Sync-output 1s safety timeout (gotcha 13, second half) — only the hold/release happy path is tested. Make `SyncOutputTimeout` settable and assert frames go live.
2. `OscSidecar` split-chunk framing: no test splits an OSC 52 across two `Write()` calls, uses the `ESC \` terminator, exercises the clipboard-*query* path, or the 64 KB truncation. (Related sidecar nits: truncation can paste a silent *prefix* if it lands on a base64 boundary — prefer discard-on-overflow; `Reset()` exists but is never wired.)
3. `ShellCommandResolver` argv contract — CLAUDE.md calls "never join-then-split" hard-won, yet nothing tests the resolver. One space-containing-arg test would pin it (and would have caught F-B7).
4. No Display test project: `ThemeTomlFormat` round-trip (incl. optional `[font]`/`[window]` = keep-current) and parsing all 18 bundled TOMLs are pure-logic and cheap.
5. Selection-anchor pruning (the A4 scenario) — existing test only scrolls the viewport.
6. Session resize wiring, `RestartSession` reuse, `CustomShellPtyBridge` — untested.
7. `IsShellAvailable_IsStableAcrossCalls` is a tautology (can't catch cache removal); `Scrollback_AccumulatesBeyondViewport` asserts only `> 0`.

---

## Suggested fix batches

1. **Hotfix batch (hours):** D1 (stuck input gate, 1 line), B1/B2 (double-frees, 2 lines), C1 (Unix default shell → `Auto`), E3 (log ordinal), D4 (`m_harmony ??=`), F1 (copy M.E.L.A).
2. **Threading batch:** A1 (tick-thread surface dispose), A2 (theme-before-publish), A5 (dispose re-check), SessionManager minors (A6).
3. **Buffering/selection batch:** A3 (tick all sessions + inbox cap + overflow fix), A4/E-tracked-grid-ref (wrap tracked refs, switch the anchor).
4. **Windows robustness batch:** B3–B7 (write lock, exit drain, async input writer, command-line quoting, exit-handler hardening) + a `windows-latest` CI job (F4) to hold the line.
5. **Integration hardening:** D2 (per-class patching), D3 (DrawProgramMenusHook postfix), D5 minors.
6. **Input/UX polish:** C2 (app-mouse rounding), C3 (Alt keys), Shift-override selection, surrogate pairing, wheel magnitude.
7. **Hygiene:** F2/F3/F5/F6 (deploy clean, workflow injection, notices, stale docs), dead-code deletion (frontend + binding `TerminalOption`), test gaps (F7).
