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

## Launching a program as a terminal that closes on exit
A terminal can run one program instead of an interactive shell and close itself when the program
ends — the kFS Neovim launcher (`purrTTY.GameMod/Kfs/KfsNeovimLauncher.cs`) is the model.

```csharp
ProcessLaunchOptions options = ProcessLaunchOptions.CreateCustom("/usr/bin/htop");   // executable + discrete arguments
options.WorkingDirectory = "/";
options.EnvironmentVariables["MY_VARIABLE"] = "value";      // added to the child's environment
options.RemovedEnvironmentVariables.Add("FORCE_COLOR");     // removed from it, inherited or default alike
options.CloseOnSuccessfulExit = true;                       // or CloseOnExit = true
TerminalWindow window = controller.OpenWindow(options, "htop");   // GhosttyTerminalController, main thread
controller.IsVisible = true;                                // after OpenWindow: set first, it opens a default shell
window.RequestFocus();
```

- `CloseOnExit` closes the terminal whatever the exit code. `CloseOnSuccessfulExit` closes it only
  on exit code 0, so a program that failed to run keeps its window, its last screen and
  " (exit N)" in its tab — use it when the player must be able to read an error (gotcha 43).
- What closes: the tab; the window when that was its last tab; an in-world terminal is retired.
  The final output is applied first (gotcha 42). A process that fails to *start* is never closed
  this way: its window stays and shows the start error.
- For an in-world terminal put the same options in `InWorldTerminalRecord.Launch` and call
  `InWorldTerminalManager.Create(record)` from inside the ImGui frame (`TerminalMod.OnAfterUi`).
- In a saved layout the options are the keys `close_on_exit = true` /
  `close_on_successful_exit = true` in a terminal's `[terminal.shell]` table.
- On Windows the executable must be a real `.exe`: the terminal does not honour `PATHEXT` and
  cannot start a `.cmd` or `.bat` script.

## kFS: status of everything below
The following recipes cover purrTTY's kFS work: the `x_` services, the `purr` command, `purr-kfs`,
the language server and Neovim. **It is new and has not run in the game.** What was verified:
the `purr-kfs` commands shown below were run by hand; the unit-test recipe and the Neovim paths
(finding the server through its info file, the `purr-kfs lsp` fallback, the `:Kfs*` commands
against the server stack the mod runs, over a fake game side) are covered by the automated tests,
which passed with Neovim 0.12.6 on macOS. Recipes that need the game describe what the code does;
nobody has performed them in the game yet, so expect to find differences when you first try them,
and check them against the plan's
[in-game validation checklist](../plans/KFS_EXTENSION_FOR_PARTS_PLAN.md#10-in-game-validation-checklist).

Two folders recur. `<config>` is `<Documents>/My Games/Kitten Space Agency`, where purrTTY keeps
its `.purrTTY/` settings folder. The **kFS workspace** is the game's folder for kFS programs,
`<Documents>/My Games/Kitten Space Agency/kfs` by default (`kfs ws` shows or changes it); each
subfolder of it is one program ("project").

## Using the `purr` command in the game
`purr` is purrTTY's own command in the game console, registered the way the game registers its
`kfs` command. It is registered whenever the mod is loaded; `purr` or `purr help` lists the verbs.
Its output goes where `kfs` output goes (`KfsIntegration.Print`).

- **Which index is which part:** `purr parts` lists the controlled vehicle's lights, deployables
  and actuators with the index a program addresses each by; `purr parts lights`,
  `purr parts deploy` and `purr parts actuators` list one kind.
- **Write the example programs:** `purr examples` writes seven example folders (`x_blink`,
  `x_light_show`, `x_deploy`, `x_actuator`, `x_parts`, `x_party_time`, `x_blink_kasm`) into the
  kFS workspace. A file that already exists is kept; `purr examples --force` overwrites.
- **Run one** on the controlled vehicle, with the game's own `kfs` command:

  ```
  kfs on
  kfs load x_blink/blink.c --vehicle
  kfs start
  ```

  `kfs on` turns on the game's kFS prototype, which every other `kfs` verb needs. The light show
  (`x_light_show`) ends when its global `stop` is set: `kfs poke stop 1`. So does `x_party_time`,
  the light show plus the light parts' own actuators moved now and then at random; its other
  knobs are `all_actuators` (1: every actuator, not only those on light parts) and `seed`.
  `kfs start` puts every global back to its initial value, so poke a knob after the start.
- **Reset light overrides:** a program's light colour, cone, intensity and range outlive it — they
  are **not** undone when the program stops, traps or is replaced. `purr lights reset` restores the
  stock look of the controlled vehicle's lights, `purr lights reset all` of every vehicle's.
- **Status:** `purr status` prints the extension's state, hooks, tables, console registration and
  counters. After a program has run, non-zero `pump frames`, answered and accepted counters are the
  evidence that the hooks fire.
- **Reference text:** `purr header` prints `<purrtty.h>`, `purr kinc` prints `purrtty.kinc`,
  `purr services` prints one line per service.
- `purr nvim [file]` and `purr lsp [token | restart]` are described under the Neovim recipes below.

`parts`, `lights` and `examples` need the extension to be active and say so when it is not (for
example with `enabled = false` in `<config>/.purrTTY/kfs.toml`); the other verbs always work. The
purrTTY menu's **kFS** submenu runs the same verbs ("List parts in console", "Reset light
overrides", "Write example programs", "Extension status in console"), with their output in the
console.

## Writing a kFS program that uses the `x_` services
1. Start from an example: `purr examples` in the game, or read them in the repository under
   `purrTTY.Kfs/Examples/`. `x_blink/blink.c` is the smallest; its comment explains indices, switch
   groups and when a command takes effect.
2. In kC, include the header after the game's own: `#include <kfs.h>` then `#include <purrtty.h>`.
   The header declares all 22 services and the `X_*` constants and documents each; read it with
   `purr header` (in the game) or `purr-kfs header` (outside). `purr-kfs services --markdown`
   prints the same reference as Markdown.
3. In kASM, the service ids `SVC_X_*` come from the game's `<kfs.kinc>`, like the stock ones; only
   the `X_*` constants come from `purrtty.kinc`, a file that must be **in the project's folder**
   (`.include "purrtty.kinc"`). `purr examples` writes one next to `x_blink_kasm/blink.kasm`;
   `purr kinc` or `purr-kfs kinc` prints its text.
4. Parts are addressed by index (0 to `x_light_count() - 1` and so on) in lists frozen when the
   program starts; find out which index is which with `purr parts`. A getter never rejects an
   index — a missing or departed part reads 0 — so test the `PRESENT` property first.
5. A setter takes effect at the next frame, and what a getter reads back comes in two steps: the
   switch, the appearance and an animation's target from the next frame; the position and
   `X_DEPLOY_STATE` one frame after that; `X_LIGHT_LIT` one frame after that when switching on,
   but at once when switching off (gotcha 46).
6. Check it without the game with `purr-kfs check` / `run` (below), or in a unit test (the recipe
   after that).

## Check or run a kFS program outside the game with `purr-kfs`
`purr-kfs` builds and runs kFS programs with the game's own compiler, assembler and VM plus
purrTTY's `x_` services, without the game. It needs the folder that holds `KSA.dll`: set
`KSA_DLL_DIR` or pass `--ksa <dir>`. Run it from the build output (see
[build-and-test.md](build-and-test.md)); the commands below write `purr-kfs` for
`dotnet purrTTY.Kfs.Cli/bin/Debug/net10.0/purr-kfs.dll`.

```bash
purr-kfs check x_blink/blink.c --workspace /path/to/kfs        # diagnostics + a summary line; exit 1 on errors
purr-kfs check x_blink/blink.c --workspace /path/to/kfs --json # one JSON document: diagnostics, sizes, per-task WCET
purr-kfs run x_blink/blink.c --workspace /path/to/kfs --lights 2 --for 2s --log
purr-kfs header            # <purrtty.h>;  purr-kfs kinc: purrtty.kinc;  purr-kfs services [--markdown]
```

- `<file>` is relative to the workspace (`--workspace <dir>`, default: the current directory). The
  project is chosen as the game's workbench chooses it: a file in a folder builds every `.c`, `.h`,
  `.kasm` and `.kinc` file directly in that folder; a root-level `.c` builds with the files its
  `#include "..."` lines reach. Nothing is written to the workspace.
- `run` loads the program onto a **simulated** vehicle and runs it in simulated time (it never
  waits): `--for <duration>` (`10s`, `500ms`, `2m`; default `10s`), `--frame <hz>` (default 60),
  and what to print — `--log` (program log), `--requests` (decoded stock requests), `--state`
  (final state of the machine and of every light and animation); with none of the three, all three.
- The simulated vehicle: `--lights <n>`, `--deployables <n>` (2 s of travel each),
  `--actuators <n>` (1 s each), all default 0; the layout: `--thrusters <n>` (default 1),
  `--engines <n>`, `--gimbals <n>` (default 0). Stock steering, staging and burns are recorded, not
  flown.
- Exit codes: 0 success, 1 build errors or a trapped program, 2 a usage or environment problem.
  `purr-kfs <command> --help` prints each command's full help (`purrTTY.Kfs.Cli/HelpText.cs`).

## Unit-test a kFS program outside the game

The game's real kC compiler, VM and host run in the test process with purrTTY's extension
installed (`purrTTY.Kfs.Testing`; rules in [build-and-test.md](build-and-test.md#kfs-test-projects-purrttykfstesting-purrttykfstests-purrttykfstoolstests)).
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
    Assert.That(history.Select(h => h.TimeS), Is.EqualTo(new[] { 2.3, 4.2, 6.2, 8.1 }).Within(1e-6));
}
```

(The test in the suite also asserts the applied commands.) The times follow the game's frame
order, which the simulator reproduces (step → pump → dispatch): the deploy is issued in the tick at
2.1 s, applied by the pump of the next frame (2.2 s) and moves the part from the step of the frame
after (2.3 s) — see "The simulator's frame order" in [build-and-test.md](build-and-test.md).
Other tools: `v.RunTicks(n)`, `v.RunUntil(() => …, timeoutSeconds)`, `v.Pause()`/`Resume()`,
`v.ReadGlobal<float>("err.x")`, `v.WriteGlobal<uint>("stop", 1u)` (what `kfs poke` does),
`v.RequestText` (`"set_throttle(1.0)"`, `"plan_circularize(apoapsis)"`), `v.State`/`Trap`/`Overruns`.
A light and an animation on one physical part (a lamp head on a hinge) are a `SimLightSpec` and a
`SimAnimationSpec` with the same `PartId`, which both read back as `X_LIGHT_PART_ID` /
`X_ACTUATOR_PART_ID`. To bound what a program costs, count per frame `v.Binding.Answered +
v.Binding.Accepted` (getters and commands; add the new `kfs_logf` lines for the service calls a
tick makes) and read the measured cycles of a task in a helper method,
`v.Computer.Snapshot.Tasks[0].MeasuredMax` — the `x_party_time` tests in
`ExampleProgramTests` do both.
A stock program needs no `x_` call at all — `StockProgramTests` runs the game's own
`kC_ascent_simple` example. The harness records stock steering, staging and burns but does not fly
them; script their effects (`SimTelemetry.Flight`, `Props.Engines`, `BurnStatus`) yourself.
For a quick check without writing a test, use `purr-kfs run` (below).

## Editing kFS in Neovim from inside the game
purrTTY can open the player's **own** Neovim (their configuration loads as usual) as the process
of a purrTTY terminal, with files that give kC and kASM buffers a filetype and attach them to the
language server the mod runs.

- **Open it:** purrTTY menu → **kFS** → **Edit in Neovim...** (a list of the workspace's `.c`,
  `.h`, `.kasm` and `.kinc` files) or **Open workspace in Neovim**; or in the game console
  `purr nvim x_blink/blink.c` (a workspace-relative path) or `purr nvim` (the workspace folder).
  A new name with a kFS extension opens an empty buffer; nothing is written until `:w`. Paths
  outside the workspace are refused. When Neovim cannot be opened the menu entries are greyed with
  the reason, and the reason of a failed launch is shown under them.
- **Finding `nvim`:** `nvim_path` from `kfs.toml` when set (it must exist; there is no fallback);
  else the game process's `PATH`; else, on macOS and Linux, the player's login shell is asked
  (`$SHELL -lc …`), which reads `~/.zprofile` or `~/.profile` but not `~/.zshrc`. When that shell
  cannot find it either, the terminal shows a message and waits for Enter.
- **Settings** in `<config>/.purrTTY/kfs.toml` (written with defaults and comments on first run):

  | Key | Default | Meaning | Read |
  | --- | --- | --- | --- |
  | `enabled` | `true` | install the kFS extension | when the game starts |
  | `nvim_path` | `""` | absolute path of the `nvim` executable; empty searches for it. On Windows point it at `nvim.exe` (not a `.cmd` shim), in a single-quoted TOML string | at each launch |
  | `nvim_open_in` | `"window"` | `"window"` (a purrTTY window) or `"inworld"` (an in-world terminal: a 120×40 billboard in front of the camera) | at each launch |
  | `lsp_enabled` | `true` | run the language server inside the game | when the server starts |
  | `lsp_port` | `0` | its port on `127.0.0.1`; `0` picks a free one each time | when the server starts |
  | `lsp_allow_vehicle_commands` | `false` | let the editor run *any* `kfs` command line (`:KfsVerb`); build, load, start, stop, reset, target, manual and parts work without it | when the server starts |

  The server starts with the game and again after `purr lsp restart`.
- **When Neovim exits:** the window (or tab, or in-world terminal) closes by itself when Neovim
  exits normally. When it exits with an error — it crashed at start, a wrapper could not run it —
  the terminal stays, with the error on screen and " (exit N)" in its tab.
- **What the launch does:** the terminal is named `kFS: <file>`; with the game's kFS prototype on
  and a vehicle under control, `kfs` is pointed at that vehicle, so the editor's diagnostics use
  its layout; the terminal is **not** saved into layouts (reopen it from the menu instead).
- **The language server:** `purr lsp` shows its address, clients, workspace and build target;
  `purr lsp restart` restarts it after a change to `kfs.toml` (always with a new token, and with a
  new port when `lsp_port` is 0);
  `purr lsp token` prints the session token. The menu shows its status and has "Copy server
  address". By default it listens on the loopback interface from game start and writes its address
  and token to `<config>/.purrTTY/kfs-lsp.json` (readable by the owner only on macOS and Linux):
  any process of the same user that can read that file can connect and build, load and start
  programs. Set `lsp_enabled = false` to switch it off.
- Terminal limits a full-screen editor meets, as the plan's §7.3 lists them (rows T5–T8, not built;
  not re-checked for this document): the toggle hotkey (F12 by default) never reaches a 2D
  terminal's program; Ctrl/Alt with the punctuation keys `/ - ; ' , . =` and Alt-composed
  characters on non-US layouts are not encoded; focus-in/out is not reported; the clipboard cannot
  be read over OSC 52.

## Using Neovim outside the game
The same runtime files work in a Neovim started normally. They are in the `nvim` folder of the
installed mod (the `purrTTY` folder in the game's mods directory); their own reference is
`nvim/doc/purrtty-kfs.txt` there. Load them once, in one of these ways:

```bash
nvim --cmd "lua dofile('/path/to/mods/purrTTY/nvim/purrtty_kfs.lua')" /path/to/kfs/x_blink/blink.c
```

or put `dofile('/path/to/mods/purrTTY/nvim/purrtty_kfs.lua')` in `init.lua`, or copy the `nvim`
folder to `~/.local/share/nvim/site/pack/purrtty/start/purrtty_kfs`.

- **Against the running game.** Nothing more to do: when a kC or kASM buffer of the workspace
  opens, the files read the server info file `kfs-lsp.json` that the game wrote (address, token,
  process id, workspace) and connect. It is looked for in `<workspace>/../.purrTTY/` and in the
  default `<Documents>/My Games/Kitten Space Agency/.purrTTY/`; if the game's configuration folder
  is elsewhere, set `PURRTTY_CONFIG_DIR` to the folder that **contains** `.purrTTY`. A file whose
  process is gone is ignored, and Neovim looks again every 3 seconds, so starting the game later,
  or `purr lsp restart`, is picked up by itself.
- **With no game at all.** Put `purr-kfs` on `PATH` and set `KSA_DLL_DIR`; when no game server is
  found the files start `purr-kfs lsp --stdio --workspace <root>` themselves. This server gives
  diagnostics, hover and completion; of the commands only `:KfsManual` works, the others answer
  that they need the game. The build target comes from the workspace's `vehicle.h` (written by the
  game's `kfs build`), else it is a default layout with one thruster. To serve several editors or
  another client, run it yourself:

  ```bash
  purr-kfs lsp --listen 127.0.0.1:0 --workspace /path/to/kfs   # prints "LISTENING <port>" and "TOKEN <t>"
  ```

  A TCP server always requires a token (`initializationOptions.token`); pass your own with
  `--token-file <file>` or the `PURR_KFS_LSP_TOKEN` environment variable (`--token <t>` is visible
  in the process list). `--thrusters`, `--engines` and `--gimbals` fix the layout; `--verbose`
  logs every message to stderr.
- **A `.c` file keeps filetype `c`?** It is not below a known workspace root. A workspace in a
  non-default place is named with `PURRTTY_KFS_ROOT` or the `root` option of `vim.g.purrtty_kfs`;
  then `:KfsRestart`. `:KfsInfo` shows what was found and what was tried.

## The `:Kfs*` commands in Neovim
Each sends a command to the language server, which runs it on the game's main thread in its next
frame and returns what the game printed. `:KfsBuild`, `:KfsLoad` and `:KfsVerb` first write the
modified kC/kASM buffers of the workspace, because the game builds from disk.

| Command | Does |
| --- | --- |
| `:KfsBuild [file]` | builds the current buffer's file, as `kfs build` |
| `:KfsLoad [file]` | builds and loads it into the workbench target (the vehicle, when `kfs` targets one). Refused for a path containing a space: the game's `kfs load` splits its arguments at spaces |
| `:KfsStart`, `:KfsStop`, `:KfsReset` | start or stop the loaded program, reset the computer |
| `:KfsTarget vehicle` / `:KfsTarget bench` | what builds and loads are for |
| `:KfsParts [kind]` | the part index tables (`purr parts`); `kind` is `lights`, `deploy` or `actuators`, or omitted for all |
| `:KfsManual [code]` | the manual page of a diagnostic code (default: the one under the cursor) |
| `:KfsVerb {line}` | any `kfs` console line; needs `lsp_allow_vehicle_commands = true` |
| `:KfsLog` | opens the program log (`kfs://log`): log lines, command output, traps |
| `:KfsInfo` | connection status: what was found, what was tried, the program state |
| `:KfsRestart` | forget everything and look for a server again |

A short answer is shown as a message, a long one in the buffer `kfs://output`. "The game ran it"
is not "it succeeded": a build error is the game's text, shown as it is (and as diagnostics).
`require('purrtty_kfs').status()` returns a string for a status line. Against the standalone
`purr-kfs lsp` (no game) the commands that act on the game are refused by the server; `:KfsManual`,
`:KfsInfo` and `:KfsRestart` still work, and there is no program log.

## Adding a `purr` verb from mod code
Verbs live in a registry any part of purrTTY can add to — `KfsLspService` adds `lsp`,
`KfsNeovimLauncher` adds `nvim`.

```csharp
// At load (main thread). Keep the handle; dispose it at unload.
IDisposable verb = PurrConsole.Verbs.Add(
    "hello",                                   // lower-case letters, digits, '-'; starts with a letter
    "say hello [name]",                        // one line, shown by purr help
    context =>
    {
        string name = context.Words.Count > 0 ? context.Words[0] : "world";
        context.Print($"hello, {name}");       // every line goes where kfs output goes
    },
    needsExtension: false);                    // true: refused with a message while the extension is not active
...
verb.Dispose();                                // removes exactly this verb
```

- The handler runs on the main thread in the UI phase (a console command or a menu click). It gets
  a `PurrContext`: `Args` (everything after the verb), `Words`, `Print`, `Host` (the
  `IKfsConsoleHost`, only while the extension is active) and `ExtensionActive`; `NoArguments()`
  prints the usual complaint for a verb that takes none.
- An exception in a handler is caught and reported as a purrTTY bug; it never reaches the game.
- Output is converted to ASCII by default (`PurrConsole.AsciiOutput`), because the console font
  may lack typographic characters.
- A built-in verb goes in `PurrBuiltinVerbs.Create` (`purrTTY.Kfs/Console/`); test it with
  `PurrConsole.Run(args, host, print)` and a `SimConsoleHost`
  (`purrTTY.Kfs.Tests/Runtime/PurrCommandTests.cs`).
- To offer it in the menu, call `PurrConsole.Execute("hello")` from `KfsMenu.Draw`.

## Adding an `x_` service
The catalogue is the single source: the table rows, the header, the kASM constants and the
language server's hover text are generated from it. The steps, in the order the code depends on
them:

1. **Catalogue** (`purrTTY.Kfs/Catalog/XCatalog.cs`): add the id to `Svc` (inside the block
   `0x5000`–`0x50FF` of `KfsBuildInfo`) and a `ServiceDef` to `XCatalog.Services` (name with the
   `x_` prefix, parameters with their domains, cost class, summary and documentation). A new
   selector value goes in the matching enum of `Catalog/Selectors.cs`. `CatalogSelfCheck` verifies
   the catalogue before every install and refuses a broken one.
2. **Emitter** (`Catalog/CatalogEmitter.cs`): normally nothing to do — it is generic. Change it
   only for a new kind of parameter or a new generated artefact.
3. **Router** (`Runtime/ServiceRouter.cs`): a service **with a result** (a getter) gets a case in
   `Answer`, which runs inside the VM's `SYSCALL` and must return at once from data that is safe to
   read on a solver thread; a service **without** one (a setter) gets a case in `Accept`, which
   validates the slot (`Resolve`) and enqueues a `PartCommand` — it must not touch a part.
4. **Back end**: the shared logic goes in `Runtime/VehiclePartsBase.cs` (a getter in the
   `…GetI`/`…GetF` methods, a setter as a case of `Apply`, which the pump calls on the main thread)
   with clamping in `PartMath`; what must touch the part becomes a new abstract raw read or write,
   implemented twice: `Game/KsaVehicleParts.cs` (the game) and
   `purrTTY.Kfs.Testing/SimVehicleParts.cs` (the simulator). Put any decision about game objects
   into `KsaPartRules` as a pure function so it can be unit-tested.
5. **Tests**: a case in `purrTTY.Kfs.Tests/Runtime/ServiceTests.cs` (every service has one);
   update the count in `CatalogTests`; regenerate the snapshots with `PURRTTY_UPDATE_SNAPSHOTS=1`
   and review the diff of `purrTTY.Kfs.Tests/Snapshots/`; add the service to an example if it helps.
6. **Documents**: the header text is the program author's reference — write the catalogue
   documentation for them. Update the service count in `AGENTS.md` and the docs that cite it.

Remember the rules of the two threads (gotchas 44–46): reads on the solver thread do not allocate,
lock or throw; writes happen only in the pump; and what `KsaVehicleParts` does to a live vehicle
is not covered by any test, so add the check to the plan's in-game checklist.

## Adding a language-server feature
Features are providers registered on each connection's `KfsLanguageServer` before the client's
`initialize`; the capabilities sent to the client are computed from what is registered.

1. Implement the interface in `purrTTY.Kfs.Lsp/LspFeatures.cs` — `IHoverProvider`,
   `ICompletionProvider`, `IDocumentSymbolProvider`, `ICodeLensProvider`, `IInlayHintProvider`
   (the last three exist but have no provider yet) or `IExecuteCommandHandler` — in
   `purrTTY.Kfs.Lsp/Features/`. For any other method use `Features.AddRequest(method, handler,
   advertise)` or `Features.AddNotification(method, handler)`.
2. Register it: in `StandardFeatures.AddTo` when every host should offer it (the game and
   `purr-kfs lsp`), in `HostFeatures.AddTo` when it needs a host that can act on the game.
3. The handler gets an `LspRequestContext`: `Server.Documents` (open buffers), `Server.Builds`
   (`ResultFor(path)`: the last build of a project), `Server.Host` (tables, target),
   `Server.Encoding`. `PositionRequest.From(context)` resolves a position request against the open
   document; `KfsIndexCache.For(host)` is the name index hover and completion share.
4. Handlers run on the connection's read thread until they await, in arrival order: return
   quickly, never block on the game. Throw `JsonRpcException` for a deliberate error; any other
   exception becomes an `InternalError` response. Never start a thread except through
   `LspThreads.Start` (gotcha 53).
5. Test it with `FeatureSession` (`purrTTY.Kfs.Tools.Tests/Runtime/Lsp/`), which gives a server
   with a temporary workspace; `ServerProtocolTests` shows fake providers and the capability
   checks.

## Deploying the game mod
1. `dotnet build purrTTY.GameMod` — copies the mod DLLs **and the native libghostty-vt** to the mods dir
   (or to `PURRTTY_DIST_DIR` when that is set — set it for any build that is not a deployment).
   `CopyCustomContent` **wipes the destination `purrTTY/` folder first** (stale DLLs from removed
   projects must never linger in the game's mod ALC) and copies the managed payload by glob
   (`purrTTY.*.dll` — this is how `purrTTY.Kfs.dll` and `purrTTY.Kfs.Lsp.dll` ship) plus the
   explicit deps (`Ghostty.Vt`, `Tomlyn`, `ModMenu.Attributes`,
   `Microsoft.Extensions.Logging.Abstractions`, `StbImageSharp` — the kitty-graphics image
   decoder), and the Neovim runtime files (`purrTTY.GameMod/nvim/**` → `purrTTY/nvim/`, without a
   `doc/tags` file). `0Harmony.dll`/`StarMap.API.dll` are deliberately
   **not** shipped — the StarMap loader supplies them — and neither are `purrTTY.Kfs.Testing`,
   `purr-kfs` or any game assembly. `THIRD-PARTY-NOTICES.md` +
   `third-party-licenses/` ship in the mod folder; keep both in sync with what actually ships.
2. Launch KSA; toggle the terminal with the configured hotkey (default F12).
