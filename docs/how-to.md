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

## Unit-test a kFS program outside the game

The game's real kC compiler, VM and host run in the test process with purrTTY's extension
installed (`purrTTY.Kfs.Testing`; rules in [build-and-test.md](build-and-test.md#kfs-test-projects-purrttykfstesting-purrttykfstests)).
Add a fixture to `purrTTY.Kfs.Tests/Runtime/` deriving from `RuntimeFixture`, keep game types
inside method bodies, then: create a vehicle, load the program, script what the game publishes,
run in simulated time, assert on parts, log, globals and stock requests. This is
`StockProgramTests.Mixed_Program_Uses_Telemetry_And_Parts`:

```csharp
[Test]
public void Mixed_Program_Uses_Telemetry_And_Parts()
{
    using SimVehicle v = Runtime.Vehicle(deployables: 1).LoadKc("""
        #include <kfs.h>
        #include <vehicle.h>
        #include <purrtty.h>
        #pragma kfs tick_hz 10
        #pragma kfs subscribe MSG_FLIGHT_DATA flight
        #pragma kfs task run rate_hz=10
        kfs_msg_flight_data_t flight;
        uint32_t want;
        void run(void)
        {
            uint32_t up = flight.altitude_m > 10000.0f ? 1u : 0u;
            if (up != want) {
                want = up;
                x_deploy_set(0u, up);
                kfs_logf("solar %u at %.0f m", up, flight.altitude_m);
            }
        }
        """);
    // Up at 5 km/s to 20 km over 4 s, then back down.
    v.TelemetryScript = t => new SimTelemetry { Flight = { AltitudeM = t <= 4 ? 5000 * t : Math.Max(0, 20000 - 5000 * (t - 4)) } };
    v.Run(9.0, 0.1);

    Assert.That(v.LogText.Where(l => l.StartsWith("solar ", StringComparison.Ordinal)),
        Is.EqualTo(new[] { "solar 1 at 10500 m", "solar 0 at 10000 m" }), v.Dump);
    var history = v.Parts.DeployHistory;
    Assert.That(history.Select(h => (h.Index, h.State)), Is.EqualTo(new[]
    {
        (0, DeployState.Deploying), (0, DeployState.Deployed), (0, DeployState.Retracting), (0, DeployState.Retracted),
    }));
    Assert.That(history.Select(h => h.TimeS), Is.EqualTo(new[] { 2.2, 4.1, 6.1, 8.0 }).Within(1e-6));
}
```

(The test in the suite also asserts the applied commands.) Other tools: `v.RunTicks(n)`,
`v.RunUntil(() => …, timeoutSeconds)`, `v.Pause()`/`Resume()`, `v.ReadGlobal<float>("err.x")`,
`v.RequestText` (`"set_throttle(1.0)"`, `"plan_circularize(apoapsis)"`), `v.State`/`Trap`/`Overruns`.
A stock program needs no `x_` call at all — `StockProgramTests` runs the game's own
`kC_ascent_simple` example. The harness records stock steering, staging and burns but does not fly
them; script their effects (`SimTelemetry.Flight`, `Props.Engines`, `BurnStatus`) yourself.

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
