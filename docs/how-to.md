# How-to Recipes

## Changing terminal/rendering behavior
- Frame production / cell mapping: `purrTTY.Terminal/Ghostty/GhosttyTerminalSurface.cs`
- Drawing: `purrTTY.Display/Ghostty/FrameGridRenderer.cs`
- Window/tab/chrome behavior: `purrTTY.Display/Ghostty/TerminalWindow.cs`; keyboard/mouse/
  clipboard input: `TerminalWindow.Input.cs` (same class — partials)
- Add a backend integration test in `purrTTY.Terminal.Tests` and run it.

## Adding or changing themes
- Bundled color schemes: drop an alacritty-style TOML into `purrTTY.GameMod/TerminalThemes/`.
- Format/parsing: `purrTTY.Display/Theming/ThemeTomlFormat.cs`; discovery: `ThemeCatalog.cs`.
- User themes are written to `<config>/.purrTTY/themes/` by the Save Current As... menu action.

## Extending the engine binding
1. Add the native P/Invoke in `vendor/Ghostty.Vt/src/Native/` (verify the symbol exists in the dylib with `nm`).
2. Wrap it in the relevant `Ghostty.Vt` class (mark purrtty additions clearly).
3. Surface it through `ITerminalSurface`/`TerminalFrame` if the frontend needs it.

## Adding a custom shell
1. Implement `ICustomShell` (or inherit `BaseLineBufferedShell`) in `purrTTY.CustomShells`.
2. Ensure metadata + a stable shell ID; verify discovery via `CustomShellRegistry`.

From **another mod**: import the exported contract assemblies (see the Custom shells section in
[code-navigation.md](code-navigation.md)), call `CustomShellRegistry.Instance.RegisterShell(id, factory)`
at mod init — registration probe-instantiates and disposes one instance, so the shell ctor must
be trivial and Dispose safe on a never-started instance — and the shell appears in the New Tab /
New Window menus automatically (live registry enumeration, no purrTTY change needed).

## Theming a specific terminal (the Theme dialog)
- Open via the **"Theme…"** menu item (`ThemeDialog`, `purrTTY.GameMod/UI/ThemeDialog.cs`).
- The **target picker** (a `FilterCombo` over `TerminalTargetRegistry.All`, defaulting to the focused
  terminal) chooses which terminal — 2D window **or** in-world instance — every edit below applies to.
- A theme is the whole appearance **bundle** (`ThemeDefinition`): palette + font family/size + the
  three opacities + cursor/border/lock/hot-zone. "Save Current As…" snapshots the full bundle into a
  user theme TOML; applying a palette/font/opacity edit calls `target.ApplyTheme(...)`.
- 2D edits also persist as the new-window defaults (via the controller); in-world edits are live-only
  (session). To add appearance state to the bundle, extend `ThemeDefinition` + `ThemeTomlFormat` and
  set it in `TerminalWindow.SnapshotAsTheme`/`ApplyThemeOverrides`.

## Creating an in-world (render-to-texture) terminal
- Open via **"In-World Terminals…"** (`InWorldManagerUI`, `purrTTY.GameMod/InWorld/UI/InWorldManagerUI.cs`).
- The manager window has two **collapsible** sections: the instance **list** and a **New Terminal**
  create form.
- Create form (fixed-width label gutter): a unique **name**, a **shell** (`FilterCombo` over
  `ShellMenuCache.Current` + `CustomShellRegistry` — every registered shell appears automatically),
  fixed **cols×rows**, an **anchor mode** (radio, in the widget column): *Part* (tiered
  Vehicle→Part→optional SubPart pickers, resolved by `VehicleLookup`) or *Screen* (camera billboard),
  and a **theme**. Create → `InWorldTerminalManager.Create(record)` builds the GPU graph + shell and
  registers the instance in the target registry.
- Instances are **session-only** (never persisted). The list is an ImGui **table** (name with a
  trailing `*` when focused / `cols x rows` / **Focus** / **Config** / red **Destroy**). **Config**
  opens a **separate per-terminal window** (several can be open at once), independent of the manager's
  visibility: a **Configure** section (live rename + theme + the three **opacity** sliders, plus a
  **live grid resize** — the texture rebuilds in place and the shell reflows via a PTY resize, no
  restart) and a **Placement** section, both collapsible, with a footer of
  half-width **Done** / red **Destroy** buttons. Set `PURRTTY_INWORLD=1` to auto-create one default
  instance on load (dev convenience).
- **Focusing:** a *Part* terminal is focused (and forwards app-mouse) by **clicking its quad**
  (nearest ego-space ray hit wins). A *Screen* billboard is **focus-from-menu** (the list's **Focus**
  button) by default; tick **Click to focus (ray-pick)** in its **Placement** section to also let it be
  clicked like a part. The toggle round-trips through saved layouts (`BillboardClickToFocus`).
- Lifecycle rules when extending this: respect the deferred GPU teardown (gotcha 25), the
  no-device-touch-at-shutdown rule (gotcha 26), and the per-instance cost (gotcha 27).

## Making an in-world terminal see-through
- The quad honors the theme's **three opacities** so the 3D world shows through, like a 2D window.
  Lower any of **Background** / **Foreground** / **Cell background** opacity below 100%:
  - **Live**, per instance: the **Configure** section of a terminal's **Config** window (opened from
    "In-World Terminals…") has three opacity % sliders. Edits apply instantly and are **session-only**
    (not persisted).
  - **Via a theme**: apply (or save) a theme whose `[window]` table sets `background_opacity` etc.;
    applying it to the in-world target carries the values in. `BackgroundOpacity` is the dominant
    "how see-through is the whole panel" knob.
- It works by reusing the existing per-cell opacity render (no second path): the off-screen target
  clears transparent, the background rect carries `BackgroundOpacity`, and the quad's custom
  premultiplied-alpha frag composites the result over the scene. To change/extend the compositing,
  read **gotcha 28** first — straight-alpha or skipping the un-premultiply both regress it.

## Saving, loading, and editing a terminal layout (a set)
- Open via the **"Layouts…"** menu item (`LayoutManagerUI`, `purrTTY.GameMod/Layouts/UI/LayoutManagerUI.cs`).
- **Save current as…** names the live terminals (2D windows + in-world instances) and writes one TOML
  file to `<config>/.purrTTY/layouts/<name>.toml`. Appearance is captured **by theme name** — custom
  colours/opacities persist only if first saved as a named theme (the Theme dialog).
- **Load** re-creates the whole set; a terminal whose name collides with a live one is **logged and
  skipped** (the rest still load — the banner shows "loaded N / skipped M"). **Tear down** removes a
  loaded set as a unit; **Delete** removes the file.
- **Edit** a saved layout: rename the layout, and per terminal rename / retheme / set the **startup
  command** / grid + anchor ids (in-world) or window geometry, or remove it. Editing changes only the
  saved file — live terminals are untouched until the layout is loaded again. Fine in-world placement
  (offset/rotation/size) is best tuned live in "In-World Terminals…", then re-saved.
- Data model + catalog: `purrTTY.Display/Layouts/` (`TerminalLayout`/`TerminalEntry`/`ShellSpec`,
  `LayoutCatalog`, `LayoutTomlFormat`). Orchestrator: `purrTTY.GameMod/Layouts/LayoutManager.cs`
  (`Apply`/`CaptureCurrentAs`/`TeardownSet`). There is **no auto-apply** — applying is always a user
  action. See gotchas 29–30 and `plans/TERM_MANAGER_PLAN.md`.

## Auto-running a command when a terminal starts
- Set a per-terminal **startup command** (`ProcessLaunchOptions.StartupCommand`). It is written to the
  shell as stdin (newline-terminated) right after the shell starts (`TerminalSession.InitializeAsync`).
  Works for every shell type, including the gatOS SSH custom shell, because the interactive login PTY
  is already up with its env (no SSH exec channel needed; no fixed sleep — the PTY line discipline
  buffers it until the shell reads it).
- Set it in the **In-World** create form ("Startup command" field) or per terminal in a saved layout
  (the Layouts editor / `startup_command` in the TOML). gatOS recipes:
  `cd /root/land-o-matic && cargo run --release` (landing-guidance TUI),
  `watch -n 0.2 cat /sim/vessels/active/telemetry` (zero-build telemetry).
- The In-World create form's "Startup command" field has a **History** dropdown on the row below it
  (a full-width filterable combo, `InWorldManagerUI.DrawStartupCommandInput`) listing the last 20
  distinct values typed there, most-recent first; picking one fills the field above. History is backed by
  `purrTTY.Display.Configuration.StartupCommandHistory`, persisted to
  `<config>/.purrTTY/startup-command-history.toml` — loaded once and cached in memory (the combo
  never touches disk while drawing), and rewritten only when a terminal is actually created with a
  non-blank startup command.

## Launch a terminal from another mod
Another mod can open and manage purrTTY terminals — a 2D window, a tab, or an in-world quad on a vehicle
part or the camera — running a registered custom shell or a real process, and observe them through
handles and lifecycle events. The ABI is `purrTTY.CustomShellContract` (namespace `purrTTY.Core.Terminal`,
`Launcher/` folder, assembly version 1.1.0.0+); purrTTY does all the main-thread/ImGui work.

1. **Reference the contract.** Compile against `purrTTY.CustomShellContract.dll` and import it from
   purrTTY's ALC so both mods share one `TerminalLauncherRegistry` (same recipe as registering a custom
   shell):
   ```toml
   [[StarMap.ModDependencies]]
   ModId = "purrTTY"
   Optional = true
   ImportedAssemblies = ["purrTTY.CustomShellContract", "purrTTY.Logging"]
   ```
2. **Get the launcher — it may not exist yet.** `TerminalLauncherRegistry.Current` is null until purrTTY has
   finished loading (and again after it unloads). Null-check it and subscribe to `Changed` to attach late
   or detach; the handler runs on the thread that published, so start work with `_ = LaunchAsync(...)`
   instead of blocking inside it.
   ```csharp
   using purrTTY.Core.Terminal;

   static ITerminalLauncher? _launcher;

   [StarMapAllModsLoaded]
   public void OnAllModsLoaded()
   {
       TerminalLauncherRegistry.Changed += OnLauncherChanged;
       OnLauncherChanged(TerminalLauncherRegistry.Current);   // purrTTY may already be up
   }

   static void OnLauncherChanged(ITerminalLauncher? launcher)
   {
       _launcher = launcher;   // null = purrTTY went away: drop handles, stop launching
       if (launcher is { IsAvailable: true }) { /* place the terminals this mod owns */ }
   }
   ```
3. **A 2D window running your custom shell.** Register the shell first
   (`CustomShellRegistry.Instance.RegisterShell(id, factory)`); its factory takes no arguments, so pass the
   instance identity through `Environment` (it arrives in `CustomShellStartOptions.EnvironmentVariables`;
   `Configuration` is forwarded to `CustomShellStartOptions.Configuration`). `Cols`/`Rows` size the window
   from the theme's font cell (approximate), `Title` is the tab title.
   ```csharp
   ITerminalHandle window = await _launcher!.LaunchAsync(new TerminalLaunchRequest
   {
       Placement = TerminalPlacementKind.Window,
       Program = new TerminalProgramSpec
       {
           CustomShellId = "ksa-script",
           Environment = { ["KSA_SCRIPT"] = scriptPath },
       },
       Window = new TerminalWindowOptions { Name = "Door Panel", Title = "Door Panel", Cols = 60, Rows = 16, NoResize = true },
   });
   ```
4. **A tab running a real process** (here `nvim`; give an absolute path — a game started from a desktop
   environment often has a minimal `PATH`, notably on macOS). A tab joins the focused window (or opens a
   window when none is open). Observe the real exit code:
   ```csharp
   ITerminalHandle tab = await _launcher!.LaunchAsync(new TerminalLaunchRequest
   {
       Placement = TerminalPlacementKind.Tab,
       Program = new TerminalProgramSpec
       {
           Program = "/opt/homebrew/bin/nvim",
           Arguments = { "notes.txt" },
           WorkingDirectory = notesDir,
       },
       Window = new TerminalWindowOptions { Title = "notes.txt" },
   });

   var exited = new TaskCompletionSource<int?>(TaskCreationOptions.RunContinuationsAsynchronously);
   tab.Lifecycle += (h, e) => { if (e == TerminalLifecycleEvent.Exited) exited.TrySetResult(h.ExitCode); };
   if (!tab.IsRunning && tab.ExitCode is { } code) exited.TrySetResult(code);   // exited before we subscribed
   int? exitCode = await exited.Task;   // the tab stays open; CloseAsync() or the user closes it
   ```
5. **An in-world terminal on a part**, addressed by the game's `Part.InstanceId` (preferred over `PartId`,
   looked up across all vehicles, session-scoped). The handle can then be moved, resized and re-themed live:
   ```csharp
   ITerminalHandle panel = await _launcher!.LaunchAsync(new TerminalLaunchRequest
   {
       Placement = TerminalPlacementKind.InWorldPart,
       Program = new TerminalProgramSpec { CustomShellId = "ksa-script", Environment = { ["KSA_SCRIPT"] = scriptPath } },
       InWorld = new TerminalInWorldOptions
       {
           Name = "Door Panel",
           Cols = 40, Rows = 12,
           PartInstanceId = part.InstanceId,        // KSA.Part.InstanceId; VehicleId "" = controlled vehicle
           OffsetZ = 0.05f, WidthMeters = 0.6f, HeightMeters = 0.2f,
           BackgroundOpacity = 0.85f,
       },
   });
   await panel.SetPlacementAsync(new TerminalInWorldOptions { PartInstanceId = part.InstanceId, OffsetX = 0.1f, OffsetZ = 0.05f, WidthMeters = 0.6f, HeightMeters = 0.2f });
   await panel.ResizeAsync(48, 14);
   await panel.ApplyThemeAsync("Solarized Dark");
   panel.WriteInput("hello\r"u8.ToArray());
   await panel.CloseAsync();
   ```
Things to know:
- **Threads.** `LaunchAsync` and every handle method may be called from any thread; the work runs at the
  start of the next frame on the main thread and the task completes after it. `Lifecycle` events are raised
  on the **main thread** (one frame after the cause) — marshal yourself if you need another thread, and do not
  block in a handler. Properties (`IsOpen`, `IsRunning`, `ExitCode`, `Cols`, `Rows`, `Name`) are safe from any
  thread. If the game is not running frames, queued tasks wait (gotcha 38); `IsAvailable` + `Changed` are the guards.
- **Events vs. state.** `Created` (and often `Started`) fire before your `await` resumes, so a handle-level
  subscriber should read `IsOpen`/`IsRunning`/`ExitCode` after subscribing; `ITerminalLauncher.Lifecycle` sees
  everything. `Exited` is not `Closed`: the terminal stays open showing the final output until it is closed.
  `Resized` is not raised for the first grid measurement.
- **`ShowTerminal` ordering.** The launcher creates the window first and only then makes the terminal UI
  visible (making it visible first would spawn an unwanted default shell — gotcha 39). Set
  `ShowTerminal = false` to open a window while the UI stays hidden; `Focus = false` is best-effort.
- **Names.** A requested `Name` that is taken becomes `Name 2`, `Name 3`, …; read `handle.Name`
  (`launcher.Find(name)` is case-insensitive). A tab is not a registry entry: its handle name is the
  requested one or `<window>#<8 hex of Id>`.
- **Arguments.** `Program.Arguments` reach a real process (and the default shell); custom shells get no
  argument vector — use `Environment`/`Configuration`.
- **In-world clicks** both focus the quad and arrive as a mouse press when the program enabled mouse
  tracking — act on the release (gotcha 40). In-world focus is cleared while a 2D terminal is focused.
- **Where it lives.** Contract: `purrTTY.CustomShellContract/Launcher/`; implementation:
  `purrTTY.GameMod/Launcher/`; pure mapping + state machine (unit-tested): `purrTTY.Display/Launcher/`.

## Deploying the game mod
1. `dotnet build purrTTY.GameMod` — copies the mod DLLs **and the native libghostty-vt** to the mods dir.
   `CopyCustomContent` **wipes the destination `purrTTY/` folder first** (stale DLLs from removed
   projects must never linger in the game's mod ALC) and copies the managed payload by glob
   (`purrTTY.*.dll`) plus the explicit deps (`Ghostty.Vt`, `Tomlyn`, `ModMenu.Attributes`,
   `Microsoft.Extensions.Logging.Abstractions`, `StbImageSharp` — the kitty-graphics image
   decoder). `0Harmony.dll`/`StarMap.API.dll` are deliberately
   **not** shipped — the StarMap loader supplies them. `THIRD-PARTY-NOTICES.md` +
   `third-party-licenses/` ship in the mod folder; keep both in sync with what actually ships.
2. Launch KSA; toggle the terminal with the configured hotkey (default F12).
