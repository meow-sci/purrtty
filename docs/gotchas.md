# Key behaviors & gotchas

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
   alone; `Text` is only for printable input. **For letter/digit/punctuation keys the event MUST
   also carry the unshifted codepoint** (`KeyEvent.UnshiftedCodepoint`, set in
   `GhosttyTerminalSurface.EncodeKeyOnce` from a `TerminalKey → codepoint` map mirroring
   libghostty's `Key.codepoint()` table). The legacy encoder has a `key.codepoint()` fallback so it
   works without it, but the **kitty keyboard protocol** encoder does **not** — it builds
   `CSI <code>;<mods>u` from the unshifted codepoint (or `utf8`) and returns **zero bytes** if
   neither is set, silently dropping every Ctrl/Alt+letter while an app (e.g. atuin) has the
   protocol enabled. Do not set `utf8` instead: under a held modifier the kitty path writes it
   verbatim (a literal `r`). Pinned by `EncodeKey_CtrlLetter_{Legacy,Kitty}Mode_*`. NB the binding
   exposes only the called key-event setters (`set_key`/`set_action`/`set_mods`/`set_utf8`/
   `set_unshifted_codepoint`); `set_consumed_mods`/`set_composing` exist natively but are not bound.

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
    tail still unread in the pipe. A handle whose pump thread fails to stop is **leaked, never closed**
    (Win32 handle recycling means a write through a recycled handle value lands in an unrelated
    kernel object). ConPTY children are created with `STARTF_USESTDHANDLES` and **null** std handles
    (`StartupInfoBuilder`, same as node-pty/Windows Terminal): without the flag, CreateProcess clones
    the parent's non-console std handles into the child (Win 8.1+ behavior, even with
    `bInheritHandles=false`), so under any parent with redirected stdio — test host, CI runner, the
    game launched with pipes — the shell's text output and stdin silently bypass the pseudoconsole
    (title/console-API calls still work, which makes it look "attached") and the terminal renders empty.

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

23. **Kitty graphics: the engine owns the protocol; we read + composite.** `Terminal.VTWrite`
    parses and stores images + placements from APC graphics commands; purrtty never parses them.
    Each tick `GhosttyTerminalSurface.PopulateImages` reads the engine's *visible* placements
    (`KittyPlacementCursor`, tick-thread-only — the storage/image handles are transient pointers
    into engine-owned maps, valid only for that tick, so image bytes are **copied + decoded
    immediately**) and the engine resolves placement geometry against the live viewport, so scroll
    and resize track for free (no client-side tracked refs). Decoding (zlib/PNG → RGBA) is
    renderer-neutral CPU work in the backend; only ids + geometry + packed RGBA cross the seam.
    **GPU upload and ImGui texture registration are render-thread-only** (`ImageTextureCache`,
    driven from `TerminalWindow.Render` inside the UI pass): a `SimpleVkTexture` is created +
    uploaded (staging pool + one-shot command buffer; `VkUtils` does the layout transitions) and
    registered via `ImGuiBackend.Vulkan.AddTexture` → an `ImTextureRef` drawn with
    `drawList.AddImage`. Never create/upload textures or call `AddTexture`/`RemoveTexture` off the
    render thread. Evict with `RemoveTexture` **before** disposing the `SimpleVkTexture`, and stay
    under the shared 1000-slot descriptor pool. Decode failures / oversize images are dropped
    (logged once), so a missing texture just means that image doesn't draw — never a crash. Known
    gaps (`KITTY_PLAN.md`): virtual Unicode-placeholder placements (Phase 2) and same-id
    re-transmit / animation + pixel-exact non-cell sizing + source-rect crop (Phase 3).

24. **`FilterCombo`/required-name fields must `EvaluateLength()` every frame.** The BRUTAL
    `ImInputString` only refreshes its `.Length` on a **true** `InputText` return, so a live read of
    the typed text reads empty while the user types unless you (a) do **not** pass `EnterReturnsTrue`
    **and** (b) call `filter.EvaluateLength()` each frame before `.ToString()`. `ImGuiWidgets.FilterCombo`
    (`purrTTY.GameMod/UI/ImGuiWidgets.cs`) does both; the in-world manager's name field and the theme
    dialog's rename/save fields follow the same rule. Skip it and the filter never narrows / the name
    validation never sees the typed text. Memory `iminputstring-enterreturns-true`. Selectable rows are
    id-disambiguated with `##{index}` so duplicate labels (parts sharing a template id) never collide.

25. **Closing an in-world terminal: detach synchronously, free the GPU two frames later.** The render
    postfix (`RenderMainPassPatch`) records each live instance's quad into the game's scene command
    buffer **on the same main thread** that drives the coordinator. So `InWorldTerminalManager.Remove`
    must, in order: remove the instance from `_instances` (the postfix stops drawing it at once),
    unregister it from the target registry, clear focus if it was focused, then **defer** the GPU free
    onto `_pendingTeardown` (`TeardownDelayFrames = 2`). Freeing immediately is a use-after-free
    (`VK_ERROR_DEVICE_LOST`) — the scene command buffer recorded that frame still references the quad's
    descriptor set + sampled image. `ProcessPendingTeardown` (start of `OnAfterGui`) counts the delay
    down, then does a device `WaitIdle()` (the recording frame has completed) before
    `instance.Dispose()`. `Active` is re-derived (cleared only when the list empties), so an in-flight
    postfix never touches freed handles. A grid/font/shell change is a **`Recreate`** (Remove + Create
    preserving name + focus) — there is no in-place texture resize (`TrySetGridSize` returns false), so
    a resize routes through this same deferred-free path; the shell restarts.

26. **Shutdown is the opposite rule: never touch the Vulkan device.** The mod only unloads at game
    shutdown, by which point the game has already destroyed the Vulkan device. Calling `WaitIdle` /
    `Destroy*` on it faults with an `AccessViolationException` — a **corrupted-state exception that
    managed try/catch cannot trap**. So the coordinator's `Dispose` and `InWorldTerminalInstance.Dispose(freeGpu: false)`
    skip every GPU free (the process is exiting; the driver/OS reclaims the VRAM) and only close each
    shell session (device-free) to avoid orphaned child processes. The mid-session Close/resize path
    (gotcha 25) keeps its `WaitIdle` — the device is alive there. Do **not** "fix" the shutdown by
    wrapping the device calls in try/catch; an AVE is uncatchable and the only correct move is not to
    make the call.

27. **Each in-world instance is one extra off-screen pass + queue submit per frame.** Every
    `InWorldTerminalInstance` owns its own `OffscreenRenderTarget` (~4 MB color at 1024²),
    `PerFrameRenderer` (its own command pool/buffers/fences and **one extra queue submit** on the
    shared graphics queue), and shell session. Only the identical quad state (pipelines/layout/vertex
    input/unit-quad VB-IB) is shared via `SharedQuadResource`. Bound N sensibly in the manager UI;
    don't silently allow dozens. The off-screen target is **`R8G8B8A8Unorm`, not SRGB**: `UnlitMesh.frag`
    applies `gammaToLinear()` to the sampled texel and expects gamma-encoded bytes — an SRGB target
    double-decodes and renders the in-world terminal noticeably dark.
