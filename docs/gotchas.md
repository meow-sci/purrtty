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
    - **The pty master must be close-on-exec** (`UnixPtyNative.SetCloseOnExec`, the same Apple
      arm64 variadic padding as `ioctl`, applied right after `posix_openpt`).
      - Without it, a copy of the master leaks into every later child: the shell itself, sibling
        shells, and .NET `Process.Start` children such as the relaunched game when KSA 5949
        restarts itself (`Process.Start` + `Environment.Exit`).
      - Any exit that skips `StarMapUnload`, such as that restart, a crash or a kill, then never
        hangs up the pty, and the shells are orphaned.
      - `UnixProcessManagerTests.ClosingMaster_WithoutStop_HangsUpShell` guards this.
      - `posix_openpt` portably accepts only `O_RDWR | O_NOCTTY`, so `O_CLOEXEC` cannot be
        passed there.
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

18. **Every session is ticked every frame, the inbox is bounded, and a full inbox applies
    BACKPRESSURE before it ever drops.** The PTY pumps never sleep (gotcha 14) and a surface inbox
    drains only inside `BuildFrame` (the whole inbox per tick — `MaxBytesPerTick == MaxInboxBytes`),
    so any unticked session grows its inbox without bound. `TerminalWindow.Render` ticks **all
    tabs** (dirty tracking makes quiet ones nearly free) and `GhosttyTerminalController.Update` —
    which `TerminalMod.OnAfterUi` calls even while the terminal is hidden — drains hidden windows'
    sessions at ~4 Hz on the same tick thread. When a pump-thread `Write` would overflow the 8 MiB
    cap it now **waits for the drain** (`Monitor.Wait` on the inbox lock, 500 ms budget — two
    hidden-drain cycles): this propagates flow control end to end (SSH window → guest PTY → the
    gatOS 9p stream, which drops whole *frames* at the source) instead of shearing the byte stream
    — a mid-stream drop severs a kitty APC start and the remaining base64 prints into cells (seen
    live with 1440×900 screen-stream frames, ~6.9 MB per unit). The **tick thread itself never
    waits** (a custom shell — the game console — can emit output on it; waiting would self-deadlock
    until timeout), a write that could never fit (> the whole inbox) skips straight to the drop
    path, and `Dispose` pulses parked writers out. The drop+CAN/ST heal (logged once per episode,
    `drop:N` in the perf HUD) remains as the last resort when the tick stalls past the budget.
    Pinned by `InboxBackpressureTests`.

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
    `unload()` also resets `Patch02`'s cached ModMenu-presence probe, `Patch01`'s held-key model and
    `Patch04_MouseBindingGate`'s swallowed-button model
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
    the 3D-viewport ImGui window is hovered (`GameViewport.DrawImGui` — the `IGameViewport` implementation since KSA 5402 — sets it + `SetNextFrameWantCaptureKeyboard(false)`);
    `Patch01` gates `Program.OnKey` itself, so it works regardless of how the game routes input to it.
    **Mouse-bound game actions take a second path (KSA 5482+):** rev 5449 added mouse-button
    bindings, and `Program.OnMouseButton` now feeds a bound button straight into the private
    `Program.DispatchKeyEvent` (the same action switch `OnKey` dispatches into after its gate chain),
    bypassing both `Patch01` and `Patch03`. `Patch04_MouseBindingGate` prefixes `DispatchKeyEvent`,
    passes keyboard events through untouched (`GlfwKeyEvent.IsMouse == false` — `Patch01` already gated
    them), and applies the same "never deliver the release of a press the game didn't see" rule to
    mouse buttons — but tracked from the other side: it only sees presses that survived
    `OnMouseButton`'s own filters (popups, ImGui mouse capture), so it records the presses it
    **swallowed** (`s_swallowedButtons`) and swallows exactly their releases; unknown releases pass
    (vanilla traffic), and a press the game does see clears any stale entry. Swallowing reports
    "not handled", so `OnMouseButton` falls through to its ordinary click/camera handling. It is its
    own patch class (required list) so a drift of the private target can't take `Patch01` down with it.
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
    postfix (`RenderTranslucencyPassPatch`) records each live instance's quad into the game's scene
    command buffer **on the same main thread** that drives the coordinator. So `InWorldTerminalManager.Remove`
    must, in order: remove the instance from `_instances` (the postfix stops drawing it at once),
    unregister it from the target registry, clear focus if it was focused, then **defer** the GPU free
    onto `_pendingTeardown` (`TeardownDelayFrames = 2`). Freeing immediately is a use-after-free
    (`VK_ERROR_DEVICE_LOST`) — the scene command buffer recorded that frame still references the quad's
    descriptor set + sampled image. `ProcessPendingTeardown` (start of `OnAfterGui`) counts the delay
    down, then does a device `WaitIdle()` (the recording frame has completed) before
    `instance.Dispose()`. `Active` is re-derived (cleared only when the list empties), so an in-flight
    postfix never touches freed handles. A **grid resize is live and in-place** (`TrySetGridSize`) and
    follows the same drain discipline: it commits the new cols×rows to the record, stops recording the
    quad immediately, waits out the same `TeardownDelayFrames` window, then under a device `WaitIdle()`
    rebuilds the `OffscreenRenderTarget` (`Resize` — new colour image/view/sampler),
    resizes the secondary context, and rewrites the quad's descriptor set (`InWorldQuad.RebindTarget`).
    The shell **survives**: the next off-screen frame sees the new extent and `InWorldTerminalRenderer`
    resizes the engine grid + PTY (ConPTY resize / `TIOCSWINSZ`→SIGWINCH), so the running app reflows
    like a 2D-window drag. The off-screen ImGui backend is deliberately **not** rebuilt — since KSA
    5901 its dynamic-rendering pipeline depends only on the colour format and sample count, which the
    recreated target keeps (verified: KSA's `ImGuiBackendVulkanImpl` creates its main pipeline once in
    the ctor from `CreateInfo.ColorFormat`; see gotcha 39). A font/shell change
    still routes through **`Recreate`** (Remove + Create preserving name + focus); the shell restarts.

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
    don't silently allow dozens. The off-screen target is **`R8G8B8A8Unorm`, not SRGB**: the quad's
    fragment shader applies `gammaToLinear()` (`pow(2.2)`) to the sampled texel and expects
    gamma-encoded bytes — an SRGB target double-decodes and renders the in-world terminal noticeably
    dark.

28. **In-world quad transparency is premultiplied-alpha, and faithful to the theme's three opacities.**
    The quad honors `BackgroundOpacity` / `ForegroundOpacity` / `CellBackgroundOpacity` so the 3D world
    shows through, exactly like a 2D window. The mechanism reuses the existing `FrameGridRenderer`
    output rather than a second path: (a) `PerFrameRenderer` clears the off-screen color attachment to
    **`(0,0,0,0)`** (fully transparent); (b) `InWorldTerminalRenderer.DrawContent` draws the terminal
    background rect at `BackgroundOpacity` (not forced opaque) — `Foreground`/`CellBackground` opacities
    are already multiplied per-cell by `FrameGridRenderer.ToU32(color, opacity)`; (c) KSA's ImGui
    backend blend (`SrcAlpha`/`OneMinusSrcAlpha` color, `One`/`OneMinusSrcAlpha` alpha, writes A)
    accumulates coverage into the texture alpha — leaving the texture **premultiplied-alpha**; (d) the
    quad uses purrtty's **own** runtime-compiled fragment shader (`SharedQuadResource.QuadFragGlsl`,
    compiled via `RenderCore.ShaderModuleUtils.FromString` — our first owned shader module; the stock
    `UnlitMesh.frag` discards alpha and forces `1.0`) which **un-premultiplies before the gamma decode,
    then re-premultiplies**, and a **premultiplied blend** (`One`/`OneMinusSrcAlpha`, built inline — no
    KSA preset matches) composites it over the scene. Don't "simplify" to straight-alpha blending or
    drop the un-premultiply: straight alpha gives dark fringes at glyph edges, and skipping the
    un-premultiply makes translucent backgrounds render too dark (≈`0.21` instead of `0.5`). With all
    opacities at `1.0` the texture alpha is `1` everywhere and the result is pixel-identical to the old
    opaque `BlendNone` quad. The blended part-mode pipeline depth-**tests** but does **not write**
    (`ReverseZDepthStencil.DepthTestNoWrite`) so translucent fragments don't occlude each other via the
    depth buffer. Because it never writes depth, the quad's draw must happen **after** every KSA pass
    that reads the depth buffer to repaint color from scratch (atmosphere/clouds/ocean) — see gotcha 32.

29. **Saved layouts are an opt-in set on top of session-only terminals.** A *layout* is a named set of
    terminals (2D windows **and** in-world instances) persisted as one TOML file in
    `<config>/.purrTTY/layouts/` (`LayoutCatalog`/`LayoutTomlFormat` in `purrTTY.Display/Layouts/`);
    the orchestrator `LayoutManager` + the "Layouts…" dialog live in `purrTTY.GameMod/Layouts/`. Rules
    that must not regress: **apply is always user-initiated** — no auto-apply on game start, no default
    layout, no env gate; on apply a terminal whose name collides with a live one is **logged and
    skipped** (`TerminalTargetRegistry.IsNameAvailable` pre-check) and the rest still load;
    `LayoutManager.Apply` is pumped from `TerminalMod.OnAfterUi` because in-world `Create` needs an
    active ImGui frame; **2D windows persist position+size only** (cols/rows + font are derived live),
    while in-world entries persist cols/rows (texture extent) + anchor; and in-world anchor identity is
    **best-effort** — only `Vehicle.Id` is reliably stable across restart, `Part.Id`/`SubPart.Id`
    resolve "first match" (plans/TERM_MANAGER_PLAN.md §3.5). Teardown of a set uses the same safe paths
    (`controller.CloseWindow` / the in-world deferred GPU free).

30. **The layout persistence layer stays dependency-light: no Tomlyn POCO serializer, no `ModLog`.**
    `LayoutTomlFormat` parses to the Tomlyn **DOM** on load and **hand-emits** the TOML text on save,
    and `LayoutCatalog`/`LayoutTomlFormat` do **not** call `ModLog`. Both Tomlyn's reflection-based
    POCO (de)serializer **and** Brutal.Logging's interpolated `LogString` handler pull
    `Microsoft.Extensions.ObjectPool` (assembly v11) from the host runtime — present in the running
    game but **absent from the reference-assembly set used by tests/CI**, where either throws
    `FileNotFoundException` at first use. So the Display-side leaf layer is kept free of both (DOM +
    hand-emit, silent graceful failure); user-facing logging lives in the GameMod `LayoutManager`
    (in-game only). Don't reintroduce `TomlSerializer.Serialize(poco)` or `ModLog.Log.*` into
    `purrTTY.Display/Layouts/` — the layout round-trip tests in `purrTTY.Display.Tests` would then fail
    to load ObjectPool.

31. **In-world part anchors follow a decoupled/docked part by object identity (within a session).**
    A part-mode anchor persists `(vehicleId, partId, subPartId)` (best-effort — plans/TERM_MANAGER_PLAN.md
    §3.5), but at runtime `InWorldQuad` resolves that **once** to a live `Part` and then **follows the
    Part object** instead of re-resolving "part-id within vehicle-id" every frame. KSA decoupling/docking
    **moves the same `Part` instance** into the new vehicle (`Decoupler.Decouple` → `Vehicle.Split` →
    `PartTree.TransferPart` re-points `part.Tree`; `Part.InstanceId` is never reassigned), so
    `VehicleLookup.FindContaining(part)` — a reference-identity search across `Universe.CurrentSystem`
    vehicles (and sub-parts) — relocates the part in whatever vessel now owns it, and the transform flows
    through that current vehicle. Caveats baked into the code: identity is **session-only** (the `Part`
    object / `InstanceId` are per-run and NOT persisted — a new run re-resolves from the persisted ids);
    the cache is **invalidated when the target ids change** (a live Configure-panel edit); and only a
    **specifically-targeted** part is followed — the empty-target "first part" default keeps re-resolving
    so it stays with the controlled/target vehicle rather than chasing a decoupled chunk. A tracked part
    that vanishes from every vehicle (destroyed) drops the cache and re-resolves.

32. **The in-world quad postfixes `SuperMeshRenderSystem.RenderTranslucencyPass`, not `RenderMainPass`
    — atmosphere/clouds/ocean draw between the two and don't know about non-depth-writing quads.**
    KSA's frame (`Program.RenderGame`) draws all *opaque* geometry — vehicle parts, the planet's solid
    terrain/distant-sphere body — inside `SuperMeshRenderSystem.RenderMainPass`'s render pass, all of
    which correctly depth-test/write and so are correctly occluded by/occlude the quad. But **after**
    that render pass ends, `OceanRenderer.Render` (its own `VkRenderPass`, same depth/color images) and
    `PlanetTransparenciesRenderer.Render` → `AtmosphereRenderer.RenderWithCompute` (a **compute** shader
    that `imageLoad`/`imageStore`s the resolved offscreen color image directly, gated by a ray-sphere
    intersection against the atmosphere shell) both run, reading the **existing depth buffer** to decide
    how to repaint color at every pixel — with no notion of a translucent quad that drew color there
    without writing depth. The result: atmosphere/clouds/ocean unconditionally overwrite the quad
    wherever it overlaps a planet, producing a hard cutout following the planet's silhouette (looks like
    normal depth occlusion but isn't). Postfixing `SuperMeshRenderSystem.RenderTranslucencyPass` instead
    — the pass KSA itself uses for translucent scene geometry (glass, particles) — runs after both
    offenders, so the quad survives. **Since KSA 5482 (rev 5408, "bloom renders after translucency")**
    the main flight pass runs the MSAA colour resolve, the underwater tint, sun + global bloom (global
    Kawase bloom is on by default and touches every pixel) and the selected-part outline *after* this
    pass, so the quad is now post-processed like KSA's own glass/particles — slightly softened and
    dimmed by bloom. That is accepted as intended (the quad reads as world geometry); the VAB editor
    pass already bloomed translucency before 5482, and secondary viewports (kitten cams) run their only
    bloom — sun bloom — before it. **KSA 5554 (revs 5530/5534)** adds atmosphere and terrain to
    secondary viewports; their `RenderAtmosphereOnly` call also precedes translucency, so the
    same injection point remains valid. The postfix still draws with the **main camera** in every
    viewport; secondary-camera placement is an existing limitation, not validated by the offline
    upgrade checks. None of those later passes re-derive colour from depth, so gotcha 32's cutout can't
    return through them. Three consequences baked into the fix:
    - **The pipelines are built for dynamic rendering, not a classic `VkRenderPass`.**
      `RenderTranslucencyPass(useCustomRenderPass:true)` uses `vkCmdBeginRendering`/`EndRendering`, not
      a classic `VkRenderPass` handle — a pipeline bound to one is not valid inside a
      dynamic-rendering scope. `SharedQuadResource`'s pipelines instead chain a
      `VkPipelineRenderingCreateInfo` (color/depth formats + `RasterizationSamples` all sourced from
      `Program.OffscreenTarget`'s `ColorAttachment`/`DepthAttachment`/`Samples`, no `RenderPass`/`Subpass`),
      mirroring KSA's own `PartModelGlass` pipeline (which draws in the same post-atmosphere slot,
      immediately before the translucency pass).
      `Program.OffscreenTarget` is the same object as `Program.MainViewport.OffscreenTarget`, which is what
      the patch binds — so the pipeline always matches the attachments. (KSA 2026.8.5.5168 / rev 5154
      deleted `Program.OffScreenPass`, `KSA.OffscreenTarget`, `KSA.RenderTarget` and `KSA.Framebuffer`;
      `ColorAttachment`/`DepthAttachment` on the replacement `KSA.Rendering.RenderTarget` already resolve
      to the MSAA images when multisampled, so no separate `msaa ? … : …` probe is needed.)
    - **The postfix reopens its own dynamic-rendering scope.** Unlike `RenderMainPass` (which leaves its
      render pass open for the caller to `EndRenderPass`), `RenderTranslucencyPass` calls its own
      `BeginRendering`/`EndRendering` internally and returns with the scope already closed — a postfix
      can't simply append draws into it. `RenderTranslucencyPassPatch` instead issues the same entry
      barriers plus a second `BeginRendering` (`LoadOp.Load` for both color and depth) that KSA's own
      `RenderTranslucencyPass` / `PartModelGlass.WriteCommandsColor` use for their identical scopes,
      draws every live instance's quad, then `EndRendering`s — a self-contained second dynamic-rendering
      scope layered on top, not a modification of KSA's own pass.
    - **The entry barriers must declare the write access the quad performs.** Since rev 5154 every
      `RenderImage` tracks its own layout/access state: `PipelineBarrier2(image, state)` takes its
      *source* from the image's recorded state and records `state` as the source of the **next**
      barrier (so no hard-coded source state can drift). The quad blends (reads + writes) colour and
      depth-tests against depth the translucency pass just stored, so the patch declares
      `ColorAttachmentReadWrite` + `DepthStencilAttachmentReadWrite` — exactly what
      `RenderTranslucencyPass` declares. It used to declare the read-only `ColorAttachmentRead` (and no
      depth barrier), which left the quad's writes out of the next barrier's source scope; harmless-ish
      while the next consumers were other colour-attachment passes, but since 5482 the next consumer is
      the MSAA resolve / bloom **compute** read. A write-access state also defeats
      `RenderImage.CreateBarrier`'s "same state and no writes → skip" shortcut, so the barrier is
      always emitted.

33. **Kitty video re-decode is content-hash-driven; the payload LENGTH is not a change signal.**
    Video-style producers (terminal-doom, the gatOS `/sim/display` screen stream) delete and
    re-transmit the *same* image id every frame. `GhosttyTerminalSurface.PopulateImages` used to
    detect the re-transmit by comparing the stored payload's byte length — but raw-format (`f=32`)
    frames of fixed dims **all have identical length**, and consecutive zlib frames often collide
    too, so the frontend texture froze on frame 1 forever. It now compares an FNV-1a-64 content
    hash computed straight over the engine's stored payload (`KittyPlacementCursor.HashImageData`,
    a purrtty binding addition — 8-byte lanes, no copy, no allocation), and evicts hash entries
    whose image lost its last visible placement (an id deleted and later re-created always
    re-decodes). Pinned by `KittyScreenStreamAssetTests` using real gatOS frames vendored under
    `purrTTY.Terminal.Tests/Assets/` (equal-length A→B re-transmit must re-emit; a still image
    must NOT re-decode per tick).

34. **The pinned libghostty-vt native memory-corrupts on kitty `o=z` (zlib) payloads of
    compressible data — do not send it compressed streams.** Committing a chunked `a=T,o=z`
    transmission whose deflate stream contains real back-references (any real image; minimal
    repro: zlib of 230 KB of zeros → 2 239 B payload) corrupts the heap inside `VTWrite` —
    usually a segfault, sometimes silent (the crash point is layout-dependent), which also
    explains historic in-game misrendering. Payloads that barely compress (noise, ratio ≈ 1)
    happen to survive, which is why small synthetic tests never caught it. Raw `f=32` is fully
    correct end-to-end (pixel-exact vs ground truth) and is what the gatOS stream now sends;
    PNG (`f=100`) transmits are silently ignored by this native build (no placement — treat as
    unsupported). The repro is preserved as the `[Explicit]`
    `KittyScreenStreamAssetTests.ZlibRealFrame_CrashesPinnedNative_KnownBug` — run it after any
    native pin bump; when it passes, zlib may be re-enabled stream-side and gotcha updated.

    **Root cause (isolated 2026-07-02): a `zig 0.15.2 std` bug, NOT ghostty's code — a pin bump
    alone will not fix it.** Ghostty's `LoadingImage.decompressZlib`
    (`src/terminal/kitty/graphics_image.zig`, byte-identical from our pin through ghostty main)
    drives `std.compress.flate.Decompress` through the `std.Io.Reader` plumbing; std's
    `streamIndirectInner` builds its 64 KiB-window `Writer` with `rebase = unreachableRebase`,
    but `writeMatch` calls `writableSlicePreserve(history_len=32 KiB, len)` which *does* rebase
    when a back-reference write straddles the window end — guaranteed once decompressed output
    exceeds ~64 KiB with matches (i.e. any real image). Debug builds panic at the `unreachable`;
    the shipped `ReleaseFast` native turns it into silent heap corruption. Reproduced standalone
    in ~15 lines of zig (fixed Reader over the payload + `appendRemaining`, ghostty's exact
    code). Upstream: zig issues #25032/#25035 (same `flate.Decompress` streaming family,
    milestone "urgent") are **open**, code unchanged on zig master. Paths to re-enable zlib:
    (a) a future zig release fixes flate and the native is rebuilt (zig 0.15, cross-compile
    commands in the vendored README), or (b) patch ghostty's `decompressZlib` to bypass the
    broken Reader plumbing (e.g. drive `streamRemaining` into a `Writer.Allocating`, whose
    rebase is real) and rebuild. Either way, the `[Explicit]` repro test is the gate.

    **RESOLVED 2026-07-02 via path (b):** the native was rebuilt from ghostty main `c22df09da`
    + branch `purrtty/vt-video-fixes` (`bb9c398bf`): `decompressZlib` now uses the windowless
    direct mode into a `std.Io.Writer.Allocating` (whose rebase really reallocates), with an
    effectively unbounded per-call limit — the finite-limit path has its own
    `remaining -= length` underflow (a second std bug in the same family) — and the 400 MB
    output cap enforced per step. Verified standalone in Debug + ReleaseFast (zeros + 5.2 MB
    match-heavy payloads, exact output), by `zig build test-lib-vt`, and by the un-quarantined
    `ZlibRealFrame_DecodesToGroundTruth` (real o=z frame, pixel-exact decode). `o=z` is safe for
    purrTTY again; the gatOS stream default flipped back to `rgba-zlib` (gatOS perf plan P6).
    The test stays as the standing pin-bump gate. The patch itself is vendored as
    `vendor/Ghostty.Vt/native-patches/0002-*.patch` (the ghostty checkout is disposable — see the
    fork-free upgrade workflow in the vendored README).

35. **Kitty images draw in the in-world (off-screen) terminals too — via main-backend textures.**
    `InWorldTerminalRenderer` mirrors `TerminalWindow`'s kitty integration (upload `NewImages`,
    draw z<0 placements under the grid, z>=0 over it) with its own per-instance `ImageTextureCache`.
    The cache registers textures with the MAIN ImGui backend (`ImGuiBackend.Vulkan.AddTexture`),
    and that is fine in the off-screen pass because KSA's `ImGuiBackendVulkanImpl` treats
    `TextureId` as the raw `VkDescriptorSet` handle and binds whatever set a draw command carries
    with its own pipeline layout — the two backends' descriptor-set layouts are identical, hence
    Vulkan-compatible, so a set allocated from the main backend's pool binds cleanly in the
    off-screen command buffer. Teardown ordering matters: the instance frees the renderer (and its
    kitty textures) only AFTER `PerFrameRenderer.Dispose()` drains the off-screen fences (no
    in-flight command buffer may still sample them), and the shutdown path (`Dispose(freeGpu:false)`,
    device already destroyed) skips freeing them entirely — the OS reclaims the VRAM.

36. **libghostty-vt lib builds default the kitty image-storage limit to 10 MB — set it.**
    Ghostty's `.lib` artifact defaults `kitty_image_storage_limit` to 10 * 1000 * 1000
    (`Terminal.zig` / `Screen.zig`, "more conservative with memory by default"), vs 320 MB in
    the full app. A large kitty video frame (gatOS screen stream at 1440x900 raw = ~5.2 MB)
    means TWO frames exceed 10 MB — and ghostty's `addImage` limit check runs BEFORE the
    same-id replace frees the old copy, so at the default every re-transmit of the fixed video
    id triggered a full evict+realloc of the whole image, and the eviction path removes
    placements without `untrackPin` (a tracked-pin leak per frame, unbounded; q=2 hides every
    resulting error until placements silently stop being created and the video freezes while
    text keeps working). `GhosttyTerminalSurface` now sets 256 MiB at construction via the
    binding's `Terminal.KittyImageStorageLimit` (C option 15; the get key is `TerminalData` 26),
    which restores the true in-place replace. The overwrite-path pin leak
    (`graphics_storage.zig addPlacement` never deinits the replaced placement) still exists in
    the native and is patched at the next native rebuild (gatOS PERF_IMPROVEMENT_PLAN.md P0.4);
    until then the gatOS stream sends steady-state frames as `a=t` (transmit-only, no new
    placement) with an `a=T` keyframe ~1/s, so pin churn is bounded either way.
    (The patch is vendored as `vendor/Ghostty.Vt/native-patches/0001-*.patch`.)
    **Patched 2026-07-02:** the rebuilt native (`purrtty/vt-video-fixes` `ac3fee170`) now
    untracks the replaced/evicted placement's pin in `addPlacement`/`evictImage`, so even
    per-frame `a=T` re-displays no longer leak; the keyframe cadence stays as belt-and-braces
    (and still matters for external terminals running unpatched ghostty).

37. **`ImageEx` is a `readonly struct` whose `Dispose()` NREs on `default` — never dispose one
    unguarded.** `Brutal.VulkanApi.Abstractions.ImageEx.Dispose()` is a bare
    `Allocator.FreeAllocation(in AllocationInfo)`, and `Allocator` is a reference: on
    `default(ImageEx)` it is `null`, so calling `Dispose()` throws a bare
    `NullReferenceException` with no useful message. This bites in the recreate-in-place idiom, where
    the constructor calls `Resize()` and `Resize()` starts by tearing down the previous allocation —
    on the *first* call there is no previous allocation. It killed **every** in-world terminal
    creation during the KSA 2026.8.5.5168 upgrade (`OffscreenRenderTarget` had just taken over the
    attachment allocation from the deleted `KSA.RenderTarget`/`KSA.Framebuffer`, whose `Target != null`
    class check the struct rewrite silently dropped). The fix is to hold the allocations as
    `ImageEx?` so `null` unambiguously means "nothing allocated" and teardown is `?.Dispose()`; a
    handle check (`.VkImage.VkHandle != 0`) happens to work too but relies on `ImageAllocationInfo`
    staying a struct. Both in-world catch sites log the full exception (`{ex}`, not `ex.Message`) so
    the next such failure names its frame instead of just "Object reference not set to an instance of
    an object."


38. **Dispose the whole `VertexInput` on KSA 5801 / Brutal 2026.11.** It now implements
    `IDisposable` and owns `Bindings`, `Attributes` **and** a private
    `PtrOwner<VkPipelineVertexInputStateCreateInfo>`. Manually disposing only the lists
    leaves the create-info allocation to its owner's finalizer. `SharedQuadResource.DestroyGpu`
    calls `_vertexInput?.Dispose()` once, releasing all three allocations at teardown. The
    new Brutal native collections have no finalizer, so every owned collection still needs
    deterministic disposal; do not assume garbage collection releases those buffers.

39. **The off-screen ImGui pass is colour-only dynamic rendering with explicit barriers (KSA 5901).**
    Rev 5816 moved `ImGuiBackendVulkanImpl` to dynamic rendering: `CreateInfo.RenderPass`/`SubPass`
    were replaced by a required `ColorFormat`, baked into its pipeline through
    `VkPipelineRenderingCreateInfo` with **one colour format and no depth format**. Two consequences:
    (a) the pipeline is invalid inside any `VkRenderPass`, so `PerFrameRenderer` records a
    `BeginRendering`/`EndRendering` scope; (b) that scope must carry **no depth attachment** — a depth
    view whose format differs from the pipeline's (`Undefined`) is a validation error unless
    `dynamicRenderingUnusedAttachments` is enabled. `OffscreenRenderTarget` therefore owns only a colour
    image (ImGui never depth-tests; nothing sampled the old depth image). Dynamic rendering performs no
    implicit layout transitions, so `PerFrameRenderer` issues the two the old render pass did via
    `CommandBuffer.TransitionImages2`. The pre-scope transition goes from `Undefined`, which is safe
    because the scope clears. Its source stages are `ColorAttachmentOutput | FragmentShader`. That
    covers the scene pass's fragment-shader sampling of the quad from an earlier submission, a
    write-after-read hazard the old external subpass dependency handled implicitly. It also covers our
    own previous writes. The post-scope transition goes to `SampledReadF` (`ShaderReadOnlyOptimal`), the
    layout `InWorldQuad`'s descriptor set declares. Skipped (non-dirty) frames leave the image in that
    layout.

40. **A PTY resize is a no-op once the process is gone; the guard lives in each back end, under its
    state lock.** Before this rule `IProcessManager.Resize` threw "no process is running", and the
    frontends call it between `ImGui.Begin` and `End` — resizing a window whose shell had exited
    threw out of the middle of an ImGui frame (and retired an in-world terminal through
    `MarkFailed`). Now `Resize` does nothing before the first start, after the process exited and
    after a stop; a failure of the OS call on a live PTY is reported through `ProcessError`; only
    `ObjectDisposedException` after `Dispose` remains. The decision **must stay inside the lock that
    teardown takes** — a caller-side `if (IsRunning) Resize()` races an exit seen on the pump
    thread: `UnixProcessManager.Resize` checks `_pid` / `_exited` / `_masterFd` under `_stateLock`
    (teardown invalidates the fd under the same lock); `ProcessManager.Resize` (ConPTY) checks the
    **pseudoconsole handle** under `_processLock` and not `Process.HasExited` — a client that has
    exited but whose cleanup has not run still has a valid pseudoconsole, and the old `HasExited`
    check threw in exactly that window; `CustomShellPtyBridge.Resize` checks `_isRunning`. Frontends
    resize only through `TerminalSession.Resize(cols, rows, cellW, cellH)`, which always resizes the
    surface first (a dead tab's last screen still reflows with its window) and then the PTY. The
    same race on the input side is closed in `TerminalSession.WriteIfRunning`: `Write` still throws
    "no process is running" synchronously (gotcha 20), so input for a process that died after the
    `IsRunning` check is dropped there. Pinned by `Resize_After_Exit_Is_Ignored`,
    `Resize_Racing_Exit_Never_Throws` and their neighbours in `UnixProcessManagerTests` /
    `ConPtyProcessManagerTests` (the ConPTY ones have run on CI only).

41. **"The program has finished" is `TerminalSession.HasProcessExited`, never `IsRunning`.**
    `ProcessManager.IsRunning` goes false when the child is reaped — **before** the output pump has
    drained what the dying process last wrote. Both real back ends raise `ProcessExited` only after
    that drain, and `TerminalSession.OnProcessExited` sets the latch there (`_exitCode` first, then
    the volatile `_processExited`, so a reader that sees the flag also sees the code). Anything that
    reacts to an exit by closing or reading the final screen must use the latch: acting on
    `IsRunning` cuts off the tail — the last line of output, a title change, the OSC 52 clipboard
    write an editor sends for a yank just before it quits. `SessionManager.RestartSessionAsync` calls
    `ResetProcessExited()` **before** it starts the new process, so a restart that exits at once
    re-arms the latch. On Windows `ProcessLifecycleManager` subscribes `Exited` before it sets
    `EnableRaisingEvents`: enabling the events registers a wait that fires immediately for a child
    that already exited, and with the handler attached afterwards that exit was lost.

42. **Sessions that end are closed on the main thread, before `ImGui.Begin`, after one last
    frame.** The exit is noticed on a pump thread but acted on only by
    `SessionManager.CloseExitedSessions()`, which the frontends poll on the tick thread — no
    cross-thread marshalling, and closing disposes the native surface synchronously (gotcha 19), so
    it may never run while a surface is being iterated or between `Begin` and `End`. Before each
    close it calls `Surface.BuildFrame()` once, which applies the output the latch guarantees is
    already in the inbox (gotcha 41). The callers: `TerminalWindow.Render` (before `Begin`; when the
    last tab closed, the window is not submitted at all), `TerminalWindow.DrainSessions` (the
    controller's four-times-a-second drain while the terminal is hidden) and
    `InWorldTerminalRenderer.TickAndCheckDirty`. Whether the host goes too is
    `SessionExitPolicy.ShouldRetireHost(closedThisTick, remaining)`: only when this tick closed
    something **and** nothing remains — an empty window whose session is still starting, or failed
    to start and shows its error, is left alone. A window retires through the same `Close()` as its
    "x" (the controller prunes it next frame, saving its geometry if it was the last one, and the
    game-key gate follows); because a window can close while hidden, `GhosttyTerminalController`
    also prunes on the **show** path (`PruneClosedWindows`), otherwise the next toggle would show a
    dead window or restore stale geometry. An in-world terminal sets `SessionEnded`, keeps its last
    texture, and `InWorldTerminalManager.PruneRetired` removes it through the ordinary two-frame
    GPU teardown (gotcha 25). A session whose process failed to *start* is never published, so none
    of this applies to it.

43. **`CloseOnExit` closes on any exit; `CloseOnSuccessfulExit` only on exit code 0.** Both are
    `ProcessLaunchOptions` flags (default false, copied by `Clone()`, stored in layouts as
    `close_on_exit` / `close_on_successful_exit`, written only when true);
    `TerminalSession.ShouldCloseAfterExit` decides, and `CloseOnExit` wins when both are set. The
    kFS Neovim launcher uses `CloseOnSuccessfulExit` on purpose: with `CloseOnExit`, a Neovim that
    cannot start (a wrapper script whose target is missing, a crash while reading the
    configuration) flashes a window that closes before the player can read why. Use `CloseOnExit`
    only for a program whose failure output nobody needs. Without either flag nothing changes: the
    session stays with its last screen and " (exit N)" in its tab. The flags follow the session's
    **original** launch options across a restart.

44. **kFS service calls arrive on vehicle-solver threads; nothing there may throw, block or touch a
    part.** The game dispatches a vehicle's kFS computer inside its physics job
    (`PhysicsBubble.RunFlightComputer` → `KfsDispatch.TryComputeControl`), so the two host hooks —
    `IssueResultPatch` (inside the VM's `SYSCALL`, mid-tick) and `ApplyRequestPatch` (after the
    tick) — and everything under `ServiceRouter` run on a job thread; write that code as if
    several vehicles were dispatched at the same time. An exception that escapes there is caught
    by the game, which **halts that vehicle's kFS computer** and hands the vehicle to the stock
    flight computer (`KfsVehicleHost.Fault`: "kFS host fault … its computer is halted"). So both
    postfixes catch everything (a getter answers 0, a
    request is rejected with "internal error"), and so does the router. The rules for code reached
    from `ServiceRouter.Answer` / `Accept`: read only (single field reads and bounds-checked span
    reads — `KsaVehicleParts` documents which members qualify); no locks other than the binding's
    queue lock; no allocation per call after warm-up (at time warp a frame runs many ticks; a
    rejection's text is not even formatted when the host's event limiter would drop it —
    `RejectedSilently`); and **never a write to a game object** — a setter only validates its slot
    and enqueues a `PartCommand` on the vehicle's `VehicleBinding`. The queue is a ring that grows
    to `QueueLimit` and then overwrites the oldest command, so a program that floods it loses old
    commands, never blocks the solver and never grows memory. Whether a small patched method is
    inlined by the JIT (and its patch therefore bypassed) cannot be shown offline; `purr status`
    counters after running an example are the in-game evidence.

45. **Part writes happen only in the main-thread pump, in the one window where no solver job
    runs.** `BeforeInputEventsPatch` is a postfix on `KfsIntegration.BeforeInputEvents()`, which
    `Program.PrepareFrame` calls after `JobSystems.VehicleSolver.Wait()` and
    `Universe.ApplyVehicleSolvers()` and before `InputEvents.ApplyInputEvents()` and
    `Universe.ExecuteNextVehicleSolvers(...)` — the same place stock kFS drains its own main-thread
    work. The postfix runs `MainThreadPump.Run()`: discover the live hosts, drain each binding's
    queue through `BindingPump.DrainAndApply` in `Vehicle.Id` order (stock kFS's order), prune the
    bindings whose host is gone. Three things to keep: (a) the stock body does nothing while the
    game's kFS flag is off, and its `ReleaseDead` sweep runs only while it is on, so the pump runs
    regardless and filters hosts by `KfsVehicleHost.IsAlive` itself — otherwise a dead host's
    binding would never be pruned; (b) the postfix must never throw — nothing in kFS catches there,
    the exception would surface in the game's frame preparation — so it catches, logs once and
    counts; a failing host costs only that host that frame; (c) with no hosts a frame must cost
    almost nothing (one lock and an empty copy), because it runs every frame for every player.
    Anything else that must touch a vehicle on the main thread belongs in this pump, not in a new
    hook. The host list itself, `KfsVehicleHost.AllHosts`, is internal: `HostDirectory` reaches it
    by reflection once and reads it only under the list's own lock, as the game does.

46. **A program sees a command take effect one frame later, and committed part state one frame
    after that.** Per frame the game commits the physics job that just finished
    (`ApplyVehicleSolvers`), runs the pump, then starts the next job, which dispatches the kFS
    computer *before* it updates the part modules. So for a command issued in the dispatch of
    frame N: the pump of frame N+1 applies it; the dispatch of frame N+1 already reads the **live**
    fields the pump wrote (a light's switch `X_LIGHT_ON`, a light's appearance, an animation's
    target, and `MOVING`, which compares the committed position with the live target); the
    **committed** values — `POSITION`, `X_DEPLOY_STATE` — are what the physics step
    of frame N+1 produces and are read from frame N+2. `X_LIGHT_LIT` is mixed (live switch **and**
    committed power state): switching on reads lit from frame N+2, switching off reads dark
    already in frame N+1, and a light without a switch always reads lit. At time warp one frame runs several ticks
    and all of them read the same values. `SimVehicle.Frame` reproduces exactly this (step → pump →
    dispatch) and `FrameOrderTests` pins it; an earlier simulator order (pump → step → dispatch)
    showed committed values a frame early and hid the delay from every test. When adding a
    property, decide which group it is in and implement the read accordingly in **both** back ends
    (`KsaVehicleParts` reads the committed state list, `SimVehicleParts` its `CommittedState`). The
    game side of this is read from the decompiled sources, not observed in the game.

47. **The game's `SystemHeaders` must never be changed while a compile is running.**
    `SystemHeaders.Files` and `SystemHeaders.Builtins` are a plain `Dictionary` and `HashSet` that
    the kC compiler reads without a lock — on the stock editor's background build thread and on the
    language server's compile threads. Registering or removing `<purrtty.h>` under a reader can
    corrupt them. Therefore: the header is registered once at install (main thread, at mod load,
    before the language server exists); at uninstall `SystemHeaderRegistration.Release(inUse)`
    **leaves it registered** when `SystemHeaderReaders.InUse(...)` reports one of our compiles
    still running (a compile abandoned by the watchdog counts until it returns) or the game
    services report the
    stock editor building (`StockEditorProbe`, which reads the private `KfsIntegration._workbench`
    because the public `Workbench` getter would create the workbench at unload). One log line says
    so and `KfsExtension.HeaderLeftRegistered` keeps the reason. The next registration of the same
    header **adopts** the leftover — it restores what the leftover would have restored — so the
    state never becomes permanent. This is also why the unload order in `TerminalMod.Unload` is
    fixed: the language server stops first (its compiles end or are counted), then
    `Patcher.unload()` runs `KfsExtension.Uninstall()`. Do not reorder them, and do not add another
    writer of `SystemHeaders`. Pinned by `Installer/HeaderInUseTests.cs`.

48. **The game's statics outlive every load of the mod, and purrTTY's own may too.** StarMap can
    unload and load the mod again without restarting the game, and the code also allows for a
    load that follows an unload which never ran. What the kFS code left in *game* statics is then
    still there, and each piece has its rule: **(a) the tables** — `KfsIntegration._tables`
    is assigned once per process and never taken back (`Uninstall` leaves it; with no hooks the
    extra rows only reject). A later install reuses published tables only when their `Origin`
    equals `TableAugmenter.OriginFor(catalog)`, a hash of the emitted rows; tables of another
    catalogue, or tables another mod built first, disable the extension (`tables already built`)
    instead of running the new header against old rows. **(b) the console command** — see
    gotcha 49. **(c) `KfsIntegration.Output`** — gotcha 52. **(d) the header** — gotcha 47.
    **(e) light templates** — `KsaGameServices.Detach` restores every private template still alive
    (`LightOverrides.RestoreAll`). And when the assembly itself is *not* unloaded, purrTTY's own
    statics survive (gotcha 21 has the terminal's cases): `KfsLspService.Start` first disposes the
    service an earlier load left in `KfsLspService.Current`; `KfsExtension.Install` returns the
    current status when it is already installed; `PurrConsole` remembers a console that could not
    remove the command ("lingering") and does not register it a second time. Any new static cache
    or registration in the kFS code needs an answer for both cases. None of the reload paths has
    been exercised in the game.

49. **One console command name, one owner: the game's `help` throws on a duplicate.**
    `TerminalInterface.RegisterCommand(delegate, owner)` files a command under an owner object;
    the same name under the same owner **replaces** the entry, and `UnregisterObject(owner)` removes
    all of that owner's commands. But the console's `help` collects every command of every owner
    into one dictionary with `Add`, so the same name under **two** owners makes `help` throw for the
    rest of the session. `purr` is therefore filed under exactly one owner object per process,
    kept in `AppDomain` data (`KsaConsoleHost.SharedOwner`, key `purrTTY.Kfs.PurrCommandOwner`) so
    that even a second copy of the assembly in a fresh load context finds the same object and its
    registration replaces the old one. Never register `purr`, or any other purrTTY command, with
    `new object()` or `this` as owner. The command method must carry `[TerminalAction]` (the
    console reads name, description and parsing mode from the attribute, and refuses a delegate
    without one); `purr` uses `ArgParseMode.Unparsed` and parses its own line. The mod registers
    the command itself at load (`PurrConsole.Register`, state `Standing`) so it exists with the
    extension disabled; the installer then uses that registration and its uninstall leaves it.

50. **The game's `kfs load` splits its arguments at spaces; `kfs build` does not.**
    `KfsConsoleCommands.Execute` splits off the verb at the first space and hands the rest to the
    verb. `Workbench.Build` takes the whole rest as the file name, so a workspace path with spaces
    builds (and quotes would become part of the name — none are added). `Workbench.Load` splits the
    rest at **every** space to find `--part <name>` and `--vehicle`, so a path containing a space
    cannot be loaded at all: `KfsCommandLine.Resolve` refuses `kfs.load` for such a path with a
    message that says to rename it, instead of loading the wrong file. The same function adds
    `--vehicle` when the build target is a vehicle — without it a load would silently retarget the
    bench — and strips nothing else: the verb word of every fixed command is a constant, so an
    editor cannot inject another verb through a file name. Re-read these parsers on a game update
    (`upgrade-ksa` skill, section 9).

51. **`KfsIntegration.Workbench` is the player's real workspace — know which calls touch the
    disk, and never reach it from a test.** The getter creates the process's one workbench on first
    use, over `<Documents>/My Games/Kitten Space Agency/kfs`. Creating it touches no file
    (`new Workspace(folder)` only stores the path), but it is not free of effects: it registers the
    kC front end, and **every** read assigns `Out = Output`, so it is main-thread only. The folder
    is created by `Workspace.Ensure()`, `Set()`, `Write()` — and by `Workspace.List()`, which calls
    `Ensure()` first. Hence: the kFS menu lists sources only after `Directory.Exists`
    (`KfsNeovimLauncher.ListSources`), so opening a menu never creates a folder; starting the
    language server reads the folder's path and creates nothing; only an explicit action creates
    it (`purr examples`, a Neovim launch). A test must not call the getter at all: any `kfs` verb
    run through it would read and write the developer's own Documents folder. Tests go through the
    seams instead — `IKfsConsoleHost` (`SimConsoleHost`, an in-memory workspace) and `IKfsGameSide`
    (a fake) — and build with `ProjectResolver` over a `TempWorkspace`. At unload, use
    `StockEditorProbe` (the private field) rather than the getter, which would create a workbench
    as the mod leaves.

52. **The output tee wraps whatever is in `KfsIntegration.Output` and is taken out only if it is
    still there.** The language server captures what `kfs` commands print by installing an
    `OutputTee` in the public static `KfsIntegration.Output` (`OutputTeeInstallation.Install`),
    which forwards every call to the writer it replaced. Rules: **install** wraps the current
    writer, except that a *detached* tee of ours found in the slot — recognised by its full type name and
    its `IsDetached`/`Original` members, so one left by another load of the assembly counts too —
    is unwrapped first, so reloads do not stack tees; right after installing, read `KfsIntegration.Workbench` once, because the
    workbench copies `Output` only when that getter runs; **restore** puts the original back only
    when the slot still holds our tee — if another mod installed its writer on top, removing ours
    would cut that writer off, so the tee is left in place and merely *detached* (a pure
    pass-through) and a warning is logged. Two per-thread scopes change where text goes:
    *clients only* (`EnterClientsOnly`, set around `KfsIntegration.Error` by `KfsErrorEchoPatch`)
    sends a line to the editors but not to the console, because `Error` already logs the line —
    which the console shows — and additionally writes it to `Output` whenever `Output` is not the
    game's own console writer; without the scope every `kfs` error would appear twice once the tee
    is installed. The suppression applies **only** when the writer the tee replaced is the game's
    `KfsIntegration+ConsoleWindowWriter` (found by name with reflection); for any other original
    writer the tee forwards as usual. *Console only* (`EnterConsoleOnly`) is the opposite and exists
    for the token (gotcha 54). If the JIT inlined `Error` into its callers the patch has no effect
    and errors simply show twice — an in-game check.

53. **No exception may leave a language-server thread or timer, and no client may cost the game.**
    The server runs inside the game process: an unhandled exception on any thread ends the
    process — the game. Every thread the library starts therefore goes through
    `LspThreads.Start`, whose body catches everything and logs it; a thread that cannot be started
    (the process is at its limit) costs that one connection, not the server; timer callbacks
    (debounce, watchdog, the live feed) are guarded the same way (`BuildScheduler.Guard`,
    `LiveFeed.OnTimer`). New code must not start a thread with `new Thread`, and a new timer
    callback or `Task.Run` body must catch everything itself. A reviewer's reproduction before this
    was in place: a tokenless connection reset at the wrong moment aborted the process on macOS.
    The limits that keep a local peer from exhausting the process (names are the option
    properties): 8 connections and 2 not yet authenticated
    (`KfsLspListenerLimits.MaxConnections`, `MaxUnauthenticated`; excess is closed at accept), 5 s
    to complete `initialize` (`InitializeTimeout`), a 10 s send timeout (`SendTimeout`), accept
    back-off from 100 ms to 1 s; a message of at most 64 KiB before authentication and 16 MiB
    after (`KfsLanguageServerOptions.MaxPreAuthMessageBytes`, `MaxMessageBytes`), 16 MiB of queued
    output before the connection is cut (`MaxQueuedBytes`), headers of at most 2 KiB / 4 lines /
    1 KiB per line and only `Content-Length` / `Content-Type` (`JsonRpcOptions`); before
    authentication any request but `initialize` is refused and the connection closed, and nothing
    is compiled. On the game's main thread: at most 8 commands wait
    (`InGameHostOptions.MaxPendingVerbs`; more are refused with "the game is busy"), a frame starts
    one command and further ones only within 4 ms (`VerbBudgetPerFrame`), a command not started
    within 10 s fails and never runs later (`VerbTimeout`), and stopping the server waits at most
    500 ms for its threads (`LspServiceOptions.StopTimeout`). A compile that runs past 10 s is
    abandoned and its thread *retired* (it cannot be interrupted); at most 2 retired threads may
    exist in the process (`BuildSchedulerOptions.MaxRetiredThreads`), after which compiles are
    refused rather than threads leaked. Two bounds are missing and known: the number of open
    documents of an authenticated connection, and the size of what one command prints into a
    capture. All of this is pinned by `HardeningTests` / `HardeningUnitTests`; none of it has met a
    real game frame.

54. **The language server's token is a secret: never log it, echo it, or let it into a feed.** The
    in-game server listens on the loopback interface, and the token (`LspToken.New`: 32 random
    bytes, new at every start) is the only thing between another local process and the `kfs`
    commands. It exists in exactly these places: the server info file
    `<config>/.purrTTY/kfs-lsp.json` (written atomically, mode 0600 on Unix from creation, never
    inside the workspace, deleted at stop only when it parses and carries this start's token and
    process id), the `PURRTTY_KFS_TOKEN` environment variable of a Neovim the mod starts (the
    runtime files move it into Lua state and remove it from Neovim's environment so `:terminal` and
    `:!` do not inherit it), and the output of `purr lsp token`. Every status text is written
    without it (`LspServiceCore.Status`, `LspServiceText`, the log line at start), and
    `purr lsp token` prints inside `OutputTee.EnterConsoleOnly()` — otherwise the output tee would
    forward the token to every editor subscribed to command output. When adding a log line, a
    status line, a menu entry, an error message or an LSP notification near this code, check that
    neither the token nor the info file's contents can reach it. Honest limit: any process of the
    same user can read the info file; the token keeps other users and other machines out, not the
    player's own programs. `purr-kfs lsp --listen` follows the same rule (it never runs without a
    token; prefer `--token-file` or `PURR_KFS_LSP_TOKEN`, since `--token` shows in the process
    list).

55. **kFS editor terminals are never saved into a layout.** A layout stores a shell, its arguments
    and a startup command — not the environment. A Neovim terminal opened by `KfsNeovimLauncher`
    depends on session-specific environment variables (workspace root, runtime folder, the
    server's address and token), so a saved copy would come back as a Neovim without a language
    server and with a stale token at best. `LayoutManager.CaptureCurrentAs` therefore skips every
    terminal whose launch options carry `PURRTTY_KFS_ROOT`
    (`NeovimLaunchPlan.IsKfsEditorEnvironment`), 2D and in-world alike. For the check to work while
    a window's first session is still starting, the window remembers what it was opened with
    (`TerminalWindow.StartLaunchOptions`, used by `WindowLayoutRecord.LaunchOf`) — without it a
    window captured in that moment was saved as a default-shell window under the editor's name. A
    new kind of special-purpose terminal with per-launch state needs the same exclusion, or its
    state must become part of `ShellSpec`.

56. **Every ImGui `Begin…` is closed in a `finally`.** The menus and dialogs run inside the game's
    frame — the menu content inside a Harmony postfix of the game's own menu bar. A throw between
    `BeginMenu` and `EndMenu` (or `Begin` and `End`) leaves ImGui's stack unbalanced for the rest
    of the frame, which breaks the *game's* UI, not only ours. So: `TerminalMenus.DrawMenuContent`
    catches everything and logs once; every submenu it opens ends in a `finally` (the `Submenu`
    helper and the New Tab / New Window / Window / WSL2 blocks); `KfsMenu` does the same for its
    "Edit in Neovim..." submenu; the windows of `ThemeDialog`, `LayoutManagerUI` and
    `InWorldManagerUI` end in a `finally`. The kFS menu matters most here because its entries call
    into code that reads the disk and the game (`kfs.toml`, the `PATH`, the workspace) — which is
    also why those reads run once when the menu **opens** (detected by frame number) and are
    cached, never per frame. Not yet converted: the `Begin`/`End` pair of `TerminalWindow.Render`
    and the one in `InWorldTerminalRenderer`; treat them the same way when you next change them.
