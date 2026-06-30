# Terminal Layout Manager Plan (saved sets of in-world + 2D terminals)

> Status: **IMPLEMENTED** (P1–P7, all phases committed). Author target: a `Layouts/` subsystem + small
> additions across `purrTTY.Terminal`, `purrTTY.Display`, and `purrTTY.GameMod`.
>
> **As-built deviations from this design:** the data model + catalog live in **`purrTTY.Display/Layouts/`**
> (not GameMod) so the pure-logic test project covers them and reuses the in-assembly `AtomicFile` —
> so `AtomicFile` was **not** made public. `LayoutTomlFormat` parses via the Tomlyn DOM and **hand-emits**
> TOML on save, and the leaf layer avoids `ModLog`, because both Tomlyn's POCO serializer and
> Brutal.Logging pull `Microsoft.Extensions.ObjectPool` v11 — absent in the test/CI reference set
> (gotcha 30). The on-disk schema uses **discrete scalar fields** (`pos_x`, `offset_x`, …) rather than
> arrays, and each terminal carries a **single shell** (multi-tab capture deferred). The editor (R8)
> covers rename / theme / startup command / grid / anchor ids / window geometry / remove; fine in-world
> placement is tuned live then re-captured.
>
> This plan adds a way to **save, restore, and tear down named *sets* of terminals** — both
> in-world (3D render-to-texture) **and** 2D ImGui windows — with all of their settings (anchor,
> position, scale, rotation, grid size, theme, shell), plus a per-terminal **auto-run command** that
> fires as that terminal's shell starts (e.g. launch a gatOS flight-computer TUI). Layouts are
> **applied only when the user explicitly loads them** — there is no automatic apply on game start.

---

## 0. TL;DR

- A **Layout** = a named set of terminal **specs**, persisted as one TOML file in
  `…/My Games/Kitten Space Agency/.purrTTY/layouts/<name>.toml` (sibling of `themes/`).
- Each spec fully describes one terminal: name, kind (`window` | `inworld`), theme (by name),
  **shell + startup command**, and kind-specific placement (2D pixel geometry / in-world
  anchor+offset+rotation+size+grid).
- A new **`LayoutManager`** (in `purrTTY.GameMod/Layouts/`) sits *above* the two existing
  coordinators (`GhosttyTerminalController` for 2D, `InWorldTerminalManager` for in-world) and
  orchestrates **capture**, **apply** (create the whole set), and **teardown** (destroy the whole
  set). It is the only layer that sees both render worlds. Every action is user-initiated — nothing
  is ever applied automatically.
- Saved layouts can be **edited in place** — remove a terminal, rename it, or change its attributes
  (theme, shell, startup command, placement) — then re-saved, without applying them.
- **Name collisions on load are logged and skipped** — exactly as requested — using the registry's
  existing `TerminalTargetRegistry.IsNameAvailable(name)` pre-check.
- A per-terminal **auto-run command** is implemented as a single new field
  `ProcessLaunchOptions.StartupCommand`, written to the PTY as stdin right after the shell starts.
  Because `ProcessLaunchOptions` is *already* the shared shell spec for both worlds, **both 2D and
  in-world terminals get this for free, for every shell type** (gatOS-over-SSH, PowerShell, WSL,
  Game Console…).

---

## 1. How this relates to prior plans

This resumes work that `plans/TERMINAL_MANAGEMENT_REFACTOR_PLAN.md` (COMPLETE) explicitly **deferred**
in its §9 "Open items": *"per-2D-window theme persistence across restarts"* and *"in-world
persistence / auto-restore"*.

It also **consciously revises locked decision #3** of that plan ("in-world is session-only; no
per-instance TOML; nothing auto-restores"). The revision is deliberate and narrow:

- **Ad-hoc terminals stay session-only.** Creating a one-off terminal via the In-World manager or
  the New Window/Tab menus persists nothing, exactly as today.
- **Layouts are an opt-in persistence layer on top.** The user explicitly saves a *set*; only then
  is anything written — and a saved set is created in-game only when the user explicitly loads it.
  **Nothing is ever applied automatically** (no auto-apply on game start, no default layout).

So we are not re-introducing the retired per-instance `purrtty-inworld.toml` (single instance +
`Enabled` flag, removed in commit `687b444`). We are introducing a *set*-level catalog that the
user curates, which is a different and additive feature.

---

## 2. Requirements → features

| # | Requirement (from the request) | Feature |
|---|---|---|
| R1 | Manage **sets** of pre-saved terminals | `LayoutCatalog` (CRUD over `layouts/*.toml`) + `Layouts…` dialog |
| R2 | Restore a set with **all settings** (anchor, pos, scale, rotation, theme, grid…) | `TerminalSpec` captures the full surface of both kinds; `LayoutManager.Apply` re-creates |
| R3 | Include **both** in-world **and** 2D ImGui terminals | `TerminalSpec.Kind` ∈ {`window`, `inworld`}; one set may mix both |
| R4 | **Auto-run a command** at session start (e.g. flight-computer TUI) | new `ProcessLaunchOptions.StartupCommand`, injected as stdin post-start |
| R5 | Leverage a **standard, non-bespoke** pattern for gatOS-over-SSH | stdin-injection into the live login PTY (see §6 — *not* `ssh host 'cmd'`, which gatOS doesn't support and which would be worse) |
| R6 | **Duplicate names** on load → log error + skip that one | per-spec `IsNameAvailable` pre-check in `Apply` |
| R7 | **Tear down the whole set** | `LayoutManager.TeardownSet(name)` / `TeardownAllLoaded()` |
| R8 | **Edit a saved layout** — remove / rename / edit a terminal's attributes (theme, shell, startup command, placement) | `Layouts…` editor over the `TerminalLayout` POCO → `LayoutCatalog.Save` |

---

## 3. Codebase findings that shape the design

These are the load-bearing facts (verified against current source) the design is built on.

### 3.1 Two coordinators, one registry

- 2D windows are owned by `GhosttyTerminalController` (`purrTTY.Display/Ghostty/GhosttyTerminalController.cs`):
  `OpenWindow(ProcessLaunchOptions?, string? title)`, `OpenTab(...)`, `Windows` (`IReadOnlyList<TerminalWindow>`).
- In-world instances are owned by `InWorldTerminalManager`
  (`purrTTY.GameMod/InWorld/InWorldTerminalManager.cs`): `Create(InWorldTerminalRecord)`,
  `Remove(instance)`, `Recreate(old, newRecord)`, `Instances`, `OnAfterGui(dt)`, `Dispose()`.
- **Both** self-register in the process-wide `TerminalTargetRegistry`
  (`purrTTY.Display/Ghostty/TerminalTargetRegistry.cs`) as `INamedTerminal` (`Name`, `Kind`,
  `HasFocus`, `ApplyTheme`, `TrySetGridSize`, `TryRename`). The registry is **main-thread-only,
  lock-free**.
- Collisions are already handled the way R7 wants: `Register` returns `false` (never throws, never
  auto-renames) when a name is taken; `IsNameAvailable(name, excluding=null)` and `Resolve(name)`
  let a caller pre-check; `SuggestUniqueName(base)` makes a free name.

> ⇒ The layout manager belongs **above both coordinators**, in `purrTTY.GameMod` — the only assembly
> that references both the controller and the in-world manager.

### 3.2 `ProcessLaunchOptions` is the shared shell spec for *both* worlds

`ProcessLaunchOptions` (`purrTTY.Terminal/Pty/ProcessLaunchOptions.cs`, namespace
`purrTTY.Core.Terminal`) is a flat, serialization-friendly class: `ShellType` (enum), `CustomShellPath`,
`CustomShellId`, `Arguments` (`List<string>`), `WorkingDirectory`, `EnvironmentVariables`
(`Dictionary<string,string>`), `InitialWidth/Height`, `Clone()`.

- 2D: passed to `controller.OpenWindow(options)`.
- In-world: stored verbatim as `InWorldTerminalRecord.Launch`.

> ⇒ Adding **one field** here (`StartupCommand`, §6) gives the auto-run feature to **both** worlds
> at once, and it persists as part of the (already-flat) shell spec.

### 3.3 There is no SSH default-command hook — stdin injection is the right mechanism

gatOS does **not** reach purrTTY as an `ssh.exe` subprocess. `gatOS.Ssh` implements purrTTY's
`ICustomShell` (registered id `"gatOS"`, launched via `ProcessLaunchOptions.CreateCustomGame("gatOS")`)
and brokers an SSH.NET connection into a QEMU/Alpine guest, opening **only an interactive PTY shell
channel** (`CreateShellStream`) — there is no exec channel and no `ForceCommand`.

Consequences (confirmed in the gatOS repo):

- The classic `ssh host '<cmd>'` "default command" form is **not available**, and even if it were
  added it would be **worse**: a bare remote command runs a *non-login, non-interactive* shell that
  skips `/etc/profile.d`, so `$GATOS_HTTP`/`$GATOS_MQTT` are unset and no PTY is allocated — exactly
  what the gatOS flight-computer TUIs need.
- The only way to push bytes into any running shell is `IProcessManager.Write` / `TerminalSession.SendInput`.
  Nothing calls these automatically today.

> ⇒ The correct, **least-bespoke** mechanism for "auto-run a command on open" is to let the
> interactive login PTY come up and then **type the command as stdin** (`<cmd>\n`). For gatOS this
> preserves the login env + PTY the TUIs require; for every other shell it's just as if the user
> typed it. No SSH-aware code, no contract change, no exec channel. See §6.

### 3.4 Appearance is already persisted as named themes — layouts reference them

A `ThemeDefinition` (`purrTTY.Display/Theming/ThemeDefinition.cs`) is the **complete appearance
bundle**: 16-color palette + fg/bg/cursor/selection, font family/size, the three opacities, and
cursor/border/lock/hot-zone. User themes are TOML files in `<config>/themes/` managed by
`ThemeCatalog` (`Find(name)`, `SaveUserTheme`, `DeleteUserTheme`, `GetUserThemesDirectory()`), and a
terminal applies one at runtime via `INamedTerminal.ApplyTheme(ThemeDefinition)`.

> ⇒ A layout spec stores a **theme name**, not an embedded palette. Apply = `catalog.Find(name)` →
> `terminal.ApplyTheme(def)`. This is consistent with how in-world opacities already behave today
> ("session-only unless captured into a named theme"). See §18 for the "embed inline appearance"
> alternative.

### 3.5 In-world part anchors — accepted limitation (relies on vehicle + part/sub-part id)

In-world part anchors persist as the three id strings already on `InWorldTerminalRecord` —
`TargetVehicleId`, `TargetPartId`, `TargetSubPartId` — and are resolved every frame by
`InWorldQuad.TryComputePartModel` → `VehicleLookup.Resolve` + first-`Id`-match part/sub-part walks.
**We persist and restore exactly those three ids.** That is the most stable identity the game
currently exposes, and it is deliberately accepted as imperfect for now:

- **`Vehicle.Id`** is the vehicle's system-unique name (saved with the game) — stable and unique
  while the vehicle exists and isn't renamed. Empty string means *"the controlled vehicle"* (follows
  the player).
- **`Part.Id` / `SubPart.Id`** are the author-assigned serialized id when present, otherwise the
  part *type* id (`Template.Id`), which several parts can share. Resolution is therefore "first
  match", so a restored anchor can bind to a different part of the same type. (`Part.InstanceId` is
  unique but resets to 1 every run, so it cannot be persisted.)

**This is fine for now — no extra engineering.** The game does not yet expose stable, embeddable
per-part metadata; until it does, vehicle id + part/sub-part id is the best available handle. When
KSA adds per-part metadata we can embed, we will store it and resolve against it — the on-disk
schema can gain fields without breaking existing layouts (a versioned, additive change).

Two factual notes for the implementer/UI (behavior, not workarounds to push on the user): an
in-world instance can be **created before any vehicle exists** — its quad simply doesn't draw until
the anchor resolves, then appears; and billboard mode carries **no anchor at all** (camera-relative),
so those terminals always restore exactly. We also store the part's display name (`part_name`)
alongside the id, purely so the user can recognize and re-pick it if a restore lands on the wrong
part.

### 3.6 2D vs in-world asymmetry

- **In-world**: `Cols × Rows` are **authoritative** (they size the off-screen texture);
  `TrySetGridSize` works via `Recreate`. Placement is the anchor + offset/rotation/meters (or
  billboard params). All of this already lives on `InWorldTerminalRecord` (a flat POCO with
  `Clone()`).
- **2D window**: cols/rows are **derived** from pixel size + font every frame (`TrySetGridSize`
  returns `false`). So a 2D spec persists **window position + size (px) only** — never cols/rows,
  and never a font (font comes from the resolved theme). The emulator recomputes the grid live from
  the window's pixel size ÷ the current cell, so it re-flows correctly if the theme's font
  family/size changes. A 2D window may also hold **multiple shell tabs** under one registry name →
  its spec needs an ordered tab list. (In-world is the opposite: cols/rows are authoritative and
  *are* stored — they size the off-screen texture.)
- **Capture gap**: a live 2D window does **not** retain the `ProcessLaunchOptions` it was launched
  with (in-world keeps it in `.Launch`). Capturing an existing window therefore requires the
  session to remember its launch options — a small addition (§7).

### 3.7 Persistence idiom + config dir

- Idiomatic pattern is a **Tomlyn POCO** like `ThemeConfiguration`: `[TomlPropertyName(...)]`
  sections, a `[TomlIgnore]` typed prop + `[TomlPropertyName]` **string-shim** for enums/colors
  (graceful fallback on bad values), `TomlSerializer.Deserialize<T>` / `Serialize`, options
  `{ WriteIndented = true, IndentSize = 2, DefaultIgnoreCondition = WhenWritingNull }`, written via
  `AtomicFile.WriteAllText` (temp + rename).
- Config root: `Path.GetDirectoryName(ThemeConfiguration.GetConfigFilePath())` = `<override>/.purrTTY/`,
  where `<override>` is set once at startup by `TerminalMod.SetProductionConfigPath()` to
  `…/My Games/Kitten Space Agency`. The themes dir is derived the same way
  (`ThemeCatalog.GetUserThemesDirectory()`).
- **One blocker**: `AtomicFile` is `internal` to `purrTTY.Display`. Make it `public` (one keyword;
  consistent with `ThemeConfiguration.GetConfigFilePath()` already being public) so the GameMod
  layout catalog can reuse the crash-safe writer instead of re-implementing it.

---

## 4. Architecture & layering

```
purrTTY.GameMod/Layouts/                       ← NEW: the whole feature lives here
  LayoutDefinition.cs     — TOML POCO: TerminalLayout { Name, Description, Terminals: TerminalEntry[] }
  ShellSpec.cs            — TOML mirror of ProcessLaunchOptions (+ StartupCommand) with To/FromLaunchOptions
  LayoutCatalog.cs        — CRUD over <config>/layouts/*.toml (mirrors ThemeCatalog)
  LayoutManager.cs        — orchestrator: Capture / Apply / TeardownSet / TeardownAllLoaded + saved-layout editing (all user-initiated)
  UI/LayoutManagerUI.cs   — non-modal "Layouts…" dialog (mirrors InWorldManagerUI/ThemeDialog)

purrTTY.Display/                               ← small additions
  Ghostty/GhosttyTerminalController.cs : + CaptureWindows(), CreateConfiguredWindow(...), CloseWindow(window)
  Ghostty/TerminalWindow.cs            : + desired-name ctor path, + RequestPlacement(pos,size)
  Ghostty/WindowLayoutRecord.cs (NEW)  : Display-side 2D spec (Name, pos+size px, ThemeName, Tabs:ProcessLaunchOptions[]) — no cols/rows, no font
  Configuration/AtomicFile.cs          : internal → public

purrTTY.Terminal/                              ← the auto-run mechanism
  Pty/ProcessLaunchOptions.cs          : + string? StartupCommand (mirror in Clone())
  Sessions/TerminalSession.cs          : inject StartupCommand as stdin after StartAsync; + LaunchOptions getter
  Sessions/SessionManager.cs           : + Sessions enumeration (if not already exposed)
```

**Why GameMod orchestrates, coordinators stay primitives.** Each coordinator already knows how to
create/destroy **one** terminal of its kind. `LayoutManager` is the "create/destroy a **set**"
layer; it reaches into each coordinator's public API. The 2D coordinator gains a
`WindowLayoutRecord` (symmetric to the in-world `InWorldTerminalRecord`) so GameMod never has to
reach into 2D window internals.

---

## 5. Data model

### 5.1 On-disk TOML (one file per layout)

`<config>/.purrTTY/layouts/Flight Ops.toml`:

```toml
[layout]
name = "Flight Ops"
description = "5 in-world gauges + a control window"

# ── an in-world gatOS terminal that auto-launches the landing TUI ──
[[terminal]]
name        = "Landing Guidance"
kind        = "inworld"
theme       = "Solarized Dark"        # optional; resolved by name via ThemeCatalog

# shell (common to both kinds; mirrors ProcessLaunchOptions)
shell_type      = "CustomGame"
custom_shell_id = "gatOS"
startup_command = "cd /root/land-o-matic && cargo run --release"

# in-world placement
cols = 100
rows = 30
mode = "part"                          # "part" | "billboard"
vehicle_id   = ""                      # "" = controlled vehicle (most reliable)
part_id      = "CommandPod"            # best-effort first-match (see §3.5)
part_name    = "Command Pod"           # informational only
sub_part_id  = ""
offset       = [0.0, 0.0, 2.0]         # X,Y,Z metres
rotation     = [0.0, 0.0, 0.0]         # X,Y,Z degrees
size_meters  = [2.0, 2.0]              # quad width,height

# ── a 2D ImGui window running PowerShell ──
# Windows persist position + size only. cols/rows and font are NOT stored — the emulator
# auto-computes the grid from the live window pixel size and the resolved theme's font.
[[terminal]]
name        = "Console"
kind        = "window"
theme       = "Default"
shell_type  = "PowerShell"
pos         = [80.0, 80.0]             # screen px
size        = [880.0, 520.0]           # px (grid auto-computed from this + theme font)

# extra tabs in the same window (optional). The inline shell above is tab 0;
# each [[terminal.tab]] adds another session/tab in order.
[[terminal.tab]]
shell_type      = "CustomGame"
custom_shell_id = "gatOS"
startup_command = "watch -n 0.2 cat /sim/vessels/active/telemetry"
```

Notes:
- Enums (`kind`, `shell_type`, `mode`) use the **string-shim** pattern; unknown values fall back
  (`shell_type` → `Auto`, `kind` → `window`, `mode` → `part`) rather than throwing.
- Vectors are stored as small float arrays for readability; the mapper expands them to the discrete
  `InWorldTerminalRecord` fields (`PartOffsetX/Y/Z`, `PartRotationX/Y/Z`, `PartWidthMeters/HeightMeters`,
  and the billboard fields). Billboard placement uses `billboard_distance`, `billboard_offset`,
  `billboard_size`, `billboard_always_on_top`.
- `startup_command` may contain `\n` for multiple lines; a trailing newline is ensured on send.

### 5.2 In-memory types

```csharp
// purrTTY.GameMod/Layouts/LayoutDefinition.cs  — TOML POCO
public sealed class TerminalLayout
{
    [TomlPropertyName("layout")]    public LayoutHeader Header { get; set; } = new();
    [TomlPropertyName("terminal")]  public List<TerminalEntry> Terminals { get; set; } = new();
}
public sealed class LayoutHeader { public string Name = ""; public string? Description; }

public sealed class TerminalEntry
{
    public string Name { get; set; } = "";
    // kind: string-shim → TerminalKind
    [TomlIgnore]               public TerminalKind Kind { get; set; } = TerminalKind.Window;
    [TomlPropertyName("kind")] public string KindString { get; set; } = "window";
    public string? Theme { get; set; }

    // shell (tab 0) — flattened ShellSpec fields
    public ShellSpec Shell { get; set; } = new();
    [TomlPropertyName("tab")] public List<ShellSpec> ExtraTabs { get; set; } = new();   // window-only

    // window placement — position + size (px) ONLY. No cols/rows, no font:
    // the emulator derives the grid live from pixel size + the resolved theme's font.
    public double[]? Pos { get; set; }            // [x,y] px
    public double[]? Size { get; set; }           // [w,h] px

    // in-world placement (cols/rows are authoritative here — they size the texture)
    public int? Cols { get; set; }                // in-world only
    public int? Rows { get; set; }                // in-world only
    [TomlIgnore]               public string Mode { get; set; } = InWorldTerminalRecord.ModePart;
    [TomlPropertyName("mode")] public string ModeString { get; set; } = "part";
    public string VehicleId { get; set; } = "";
    public string PartId { get; set; } = "";
    public string? PartName { get; set; }         // informational
    public string SubPartId { get; set; } = "";
    public double[]? Offset { get; set; }         // [x,y,z] m
    public double[]? Rotation { get; set; }       // [x,y,z] deg
    public double[]? SizeMeters { get; set; }     // [w,h] m
    public float? BillboardDistance { get; set; }
    public double[]? BillboardOffset { get; set; }
    public double[]? BillboardSize { get; set; }
    public bool? BillboardAlwaysOnTop { get; set; }
}

// purrTTY.GameMod/Layouts/ShellSpec.cs — TOML mirror of ProcessLaunchOptions
public sealed class ShellSpec
{
    [TomlIgnore]                     public ShellType ShellType { get; set; } = ShellType.Auto;
    [TomlPropertyName("shell_type")] public string ShellTypeString { get; set; } = "Auto";
    public string? CustomShellPath { get; set; }
    public string? CustomShellId { get; set; }
    public List<string> Arguments { get; set; } = new();
    public string? WorkingDirectory { get; set; }
    public Dictionary<string,string> Environment { get; set; } = new();
    public string? StartupCommand { get; set; }

    public ProcessLaunchOptions ToLaunchOptions();                 // builds PLO + copies StartupCommand
    public static ShellSpec From(ProcessLaunchOptions? o);          // capture
}
```

The mapper (`LayoutManager` internals) converts:
- `TerminalEntry(kind=inworld)` ↔ `InWorldTerminalRecord` (GameMod; reused verbatim).
- `TerminalEntry(kind=window)` ↔ `WindowLayoutRecord` (Display; new — see §7/§8).

---

## 6. The auto-run-on-start mechanism (`StartupCommand`)

### 6.1 Backend change (one field, one injection site)

`purrTTY.Terminal/Pty/ProcessLaunchOptions.cs`:

```csharp
/// <summary>Optional command typed into the shell (as stdin, + newline) once it has started.
/// Used to auto-launch a TUI/program on connect. Works for every shell type, including the
/// gatOS SSH custom shell (the interactive login PTY keeps its env + size). Null/empty = nothing.</summary>
public string? StartupCommand { get; set; }
```
…and copy it in `Clone()`.

`purrTTY.Terminal/Sessions/TerminalSession.cs`, in `InitializeAsync` immediately **after**
`await ProcessManager.StartAsync(launchOptions, …)`:

```csharp
if (!string.IsNullOrEmpty(launchOptions.StartupCommand))
{
    var text = launchOptions.StartupCommand.EndsWith('\n')
        ? launchOptions.StartupCommand
        : launchOptions.StartupCommand + "\n";
    ProcessManager.Write(text);   // enqueues via the existing bounded PtyInputQueue writer thread
}
```

Why this is correct and not hacky:
- **One uniform site** above all three `IProcessManager` implementations (ConPTY, Unix, the
  `CustomShellPtyBridge` that fronts gatOS) — so every shell type behaves identically.
- **No fixed sleep** (project rule). PTY line discipline buffers the bytes; the shell consumes the
  queued line when it reaches its read loop, even if its prompt/banner hasn't drawn yet. The text
  echoes like a typed command — transparent and expected.
- `SessionManager.CreateSessionAsync` already `Clone()`s the options and overwrites
  `InitialWidth/Height`; `StartupCommand` rides along on the clone (hence the `Clone()` mirror).

### 6.2 Readiness nuance (documented, not over-engineered)

For nearly all shells (incl. gatOS ash/bash/zsh login shells) immediate-send works because the TTY
input buffer holds the line until the shell reads it. **If** a specific shell is later found to flush
pending input on startup, add an opt-in "send on first output" gate (fire once when the surface
first produces bytes) — but default to immediate send. Do **not** add a timer.

### 6.3 Exposing it beyond layouts (bonus, low cost)

Since the field is on `ProcessLaunchOptions`, the In-World create form and (optionally) the 2D New
Window path can expose a "Startup command" text field immediately. Recommended at least for the
In-World create form, since gatOS flight terminals are the motivating use case.

---

## 7. Capture — "Save current terminals as a layout"

`LayoutManager.CaptureCurrentAs(string name, string? description)`:

1. Enumerate live terminals via the coordinators (not just the registry, because we need full
   config, not just `INamedTerminal`):
   - **In-world**: `inWorld.Instances` → for each, `instance.Record.Clone()` is already the complete
     spec (name, cols/rows, `Launch`, `ThemeName`, mode, anchor ids, offsets/rotation/meters,
     billboard). Map → `TerminalEntry(kind=inworld)`. (Opacities are intentionally **not** captured
     here — they persist via the referenced named theme; see §3.4 / §18.)
   - **2D**: `controller.CaptureWindows()` → one `WindowLayoutRecord` per open window:
     - `Name` (`window.Name`), position + size (`window.LastKnownPosition` / `LastKnownSize`, when
       `HasObservedGeometry`), `ThemeName` (`window.Settings.ThemeName`), and `Tabs` = each session's
       retained `ProcessLaunchOptions`. **No cols/rows or font are captured** — they're derived on
       apply (grid from pixel size, font from the theme).
2. Build a `TerminalLayout`, map records → `TerminalEntry[]` (incl. `ShellSpec.From(...)`), and
   `LayoutCatalog.Save(layout)`.

**Required enabling change for 2D shell capture** (§3.6 gap): have the session remember its launch
options. `SessionManager.CreateSessionAsync` already creates `effectiveOptions = (launchOptions ??
default).Clone()`; store it on the session — `TerminalSession.LaunchOptions { get; }` — and expose
`SessionManager.Sessions` (the tab list) if not already public. `CaptureWindows()` reads
`window.Sessions` → `session.LaunchOptions` (ignoring the runtime-overwritten `InitialWidth/Height`).

> Capture is the harder direction and only matters for the "save what I have set up right now"
> workflow. The "load a curated set" direction (§8) never needs it. If the enabling change is
> deferred, capture can fall back to the window's configured *default* shell and the plan still
> delivers R2/R6/R7 (restore, collision-skip, teardown) for hand-written/loaded layouts.

---

## 8. Apply — "Load a layout into the game"

`LayoutManager.Apply(string layoutName) → LayoutApplyResult`:

```csharp
public sealed record LayoutApplyResult(int Created, IReadOnlyList<(string Name, string Reason)> Skipped);
```

Algorithm (runs on the **main thread inside the GUI loop** — see §10 for why):

```
layout = catalog.Load(layoutName)                  // null → log + return empty result
created = []
skipped = []
for entry in layout.Terminals (in file order):
    if !TerminalTargetRegistry.IsNameAvailable(entry.Name):       // R6
        ModLog.Log.Error($"Layout '{layoutName}': terminal '{entry.Name}' collides with a live terminal — skipped.")
        skipped += (entry.Name, "name already in use")
        continue
    theme = catalog(themes).Find(entry.Theme)                      // may be null → default
    switch entry.Kind:
      case Window:
        rec = map entry → WindowLayoutRecord (name, pos/size, theme name, tabs[])
        win = controller.CreateConfiguredWindow(rec, theme)        // names exactly, places, themes, opens tabs in order
        controller.IsVisible = true
        track(layoutName, win); created += win
      case InWorld:
        rec = map entry → InWorldTerminalRecord (incl. Launch w/ StartupCommand, theme name, anchor, placement)
        inst = inWorld.Create(rec)                                 // null on GPU failure → log + skip
        if inst == null: skipped += (entry.Name, "create failed (see log)"); continue
        track(layoutName, inst); created += inst
return LayoutApplyResult(created.Count, skipped)
```

Collision specifics (R6):
- The pre-check uses the registry's existing `IsNameAvailable`. Because the whole apply runs on the
  single main thread, there's no TOCTOU between the check and creation.
- **In-world**: `InWorldTerminalManager.Create` currently force-uniquifies via
  `SuggestUniqueName(record.Name)`. Since we pre-checked availability, it returns the exact name
  unchanged. (Optionally tighten `Create` to honor an already-unique name without the suggest call —
  not required.)
- **2D**: `TerminalWindow` currently always self-names `SuggestUniqueName("Terminal")`. Add a
  **desired-name path** so `CreateConfiguredWindow` registers the exact `entry.Name` (fall back to
  suggest only if somehow taken). Equivalent fallback without touching the ctor: open, then
  `window.TryRename(entry.Name)` (succeeds because we pre-checked).
- **Within-file duplicates** (two entries with the same name) self-resolve: the first creates and
  becomes live, the second now collides → logged + skipped. We additionally validate uniqueness at
  **save** time and warn.

New 2D controller API (Display):

```csharp
// Names exactly, applies geometry + theme, starts tab sessions in order, focuses, returns the window.
public TerminalWindow CreateConfiguredWindow(WindowLayoutRecord spec, ThemeDefinition? theme);

// Snapshot every open window's full spec (see §7).
public IReadOnlyList<WindowLayoutRecord> CaptureWindows();

// Immediate teardown of one window (dispose → unregister → remove from _windows).
public void CloseWindow(TerminalWindow window);
```

`TerminalWindow` additions: an optional `desiredName` ctor path (use it when registry-available,
else `SuggestUniqueName`), and `public void RequestPlacement(float2 pos, float2 size)` (sets the
same `_pendingPlacement` the ctor uses, applied next render and clamped to the work area).

---

## 9. Teardown — destroy a whole set

`LayoutManager` tracks what each apply created:

```csharp
private readonly Dictionary<string, List<INamedTerminal>> _loaded = new(StringComparer.OrdinalIgnoreCase);
```

- `TeardownSet(string layoutName)`:
  - For each tracked terminal **still live** (present in `TerminalTargetRegistry.All`):
    - `TerminalWindow win` → `controller.CloseWindow(win)`.
    - `InWorldTerminalInstance inst` → `inWorld.Remove(inst)` (the safe **deferred** 2-frame GPU
      teardown — never the shutdown-only `Dispose(freeGpu:false)` path).
  - Drop the set from `_loaded`. Terminals the user already closed are simply absent — no error.
- `TeardownAllLoaded()`: iterate every set.

Because `LayoutManager` stored the **concrete** objects (not just `INamedTerminal`), it dispatches by
type without casting guesswork. A terminal that the user closed manually is pruned lazily (skip if
not in the registry / already disposed).

No new in-world API is needed (`Remove` exists); 2D needs `CloseWindow` (§8).

---

## 10. Applying a layout is always user-initiated (no auto-apply)

**There is no auto-apply on game start — by design.** A layout is created in-game only when the user
explicitly loads it from the Layouts dialog (§11). Nothing is restored automatically: there is no
"default layout", no startup hook, and no env gate. (Consequence: a layout containing a gatOS
terminal boots the QEMU VM and runs its `startup_command` only at the moment the user applies that
layout — never at the main menu.)

`Apply` still runs on the **main thread inside the GUI loop**, because in-world `Create` needs an
active ImGui frame to measure the cell / allocate GPU. It does **not** need a vehicle present — a
part-anchored quad simply doesn't draw until its anchor resolves (§3.5), so loading a layout outside
flight is harmless: the quads light up once their vehicle exists.

---

## 11. UI — the "Layouts…" dialog + menu wiring

A non-modal, movable window mirroring `InWorldManagerUI` / `ThemeDialog`, reusing `ImGuiWidgets`
(`BeginFormTable`/`FormRow`, `FilterCombo`, `DestructiveButton`, status colors).

Sections:
1. **Saved layouts** (from `LayoutCatalog.All`): a list; per row → **Load** (`Apply`), **Tear down**
   (enabled when currently loaded; `DestructiveButton`), and **Delete** (`DestructiveButton`). Show
   "(loaded: N terminals)" when a set is live.
2. **Save current as…**: name `InputText` + optional description; **Save** calls
   `CaptureCurrentAs`. If the name exists, require an explicit Overwrite confirm.
3. **Apply result banner**: after a load, show "Loaded N, skipped M — see log" (skips come from R6
   collisions or in-world create failures). Use the `Warning`/`Error` colors when M > 0.
4. **Edit a saved layout** (R8): selecting **Edit** loads the layout's `TerminalLayout` POCO into an
   editable draft and lists its terminal entries. Per entry:
   - **Remove** the entry (`DestructiveButton`).
   - **Rename** the entry (`InputText`), validated for **uniqueness within the layout** (two entries
     sharing a name would make the second collide-skip on load, so the editor blocks it up front).
   - **Edit attributes** with the same field widgets the create/configure forms use: theme
     (`DrawThemePicker`), shell + **startup command**, and placement — for a window the pos/size; for
     in-world the cols/rows, mode, anchor (the tiered Vehicle→Part→SubPart picker when vehicles are
     live, else raw id text fields so editing works at the main menu), offset/rotation/size.
   - Appending a new terminal entry is natural here too, though the primary capture path stays
     "Save current as…".
   **Save** writes the draft back via `LayoutCatalog.Save` (atomic). **Editing changes only the
   on-disk config — it does not touch live terminals already loaded from that layout;** the edits
   take effect the next time the layout is applied. (Optionally also rename the layout *file* via a
   `LayoutCatalog.Rename(old, new)` — distinct from renaming a terminal inside it.)

Menu wiring (mirrors `OpenInWorldManager` exactly):
- `TerminalMenus.OpenLayoutManager` (`internal static Action?`), drawn near "In-World Terminals…",
  shown only when non-null.
- Set in `TerminalMod` after the `LayoutManager` is built; cleared in `DisposeResources`.
- Pump `_layoutManagerUi.Render()` from `TerminalMod.OnAfterUi` (next to `_themeDialog.Render()`).

---

## 12. Precise change list (by file)

**purrTTY.Terminal**
- `Pty/ProcessLaunchOptions.cs`: + `string? StartupCommand`; mirror in `Clone()`.
- `Sessions/TerminalSession.cs`: inject `StartupCommand` after `StartAsync`; + `LaunchOptions { get; }`.
- `Sessions/SessionManager.cs`: ensure `Sessions` (tab list) is enumerable/public; store the
  cloned `effectiveOptions` onto the session.

**purrTTY.Display**
- `Configuration/AtomicFile.cs`: `internal` → `public`.
- `Ghostty/TerminalWindow.cs`: optional `desiredName` ctor path; + `RequestPlacement(float2,float2)`.
- `Ghostty/WindowLayoutRecord.cs` (NEW): 2D spec POCO (Name, PosX/Y + Width/Height px, ThemeName,
  `List<ProcessLaunchOptions> Tabs`) — deliberately **no cols/rows and no font** (both derived live
  on apply: grid from pixel size, font from the resolved theme).
- `Ghostty/GhosttyTerminalController.cs`: + `CaptureWindows()`, `CreateConfiguredWindow(spec, theme)`,
  `CloseWindow(window)`.

**purrTTY.GameMod**
- `Layouts/LayoutDefinition.cs` (NEW): `TerminalLayout` / `LayoutHeader` / `TerminalEntry` TOML POCOs.
- `Layouts/ShellSpec.cs` (NEW): TOML mirror + `ToLaunchOptions()` / `From(...)`.
- `Layouts/LayoutCatalog.cs` (NEW): `GetLayoutsDirectory()`, `All`, `Load`, `Save`, `Delete`, `Refresh`.
- `Layouts/LayoutManager.cs` (NEW): `CaptureCurrentAs`, `Apply`, `TeardownSet`, `TeardownAllLoaded`,
  startup auto-apply, the record↔entry mappers, `_loaded` tracking.
- `Layouts/UI/LayoutManagerUI.cs` (NEW): the dialog (§11) — list + save-current + apply-result +
  the saved-layout editor (remove / rename / edit-attributes per terminal, over the `TerminalLayout`
  POCO; saves via `LayoutCatalog`).
- `Layouts/LayoutCatalog.cs`: editing reuses `Load`/`Save`; optionally add `Rename(old, new)` for the
  layout file itself.
- `TerminalMod.cs`: build `LayoutManager` + `LayoutManagerUI` in `OnFullyLoaded` (after controller +
  in-world manager); wire `TerminalMenus.OpenLayoutManager`; pump the UI in `OnAfterUi`; tear down in
  `DisposeResources`.
- `TerminalMenus.cs`: + `OpenLayoutManager` hook + the "Layouts…" menu item.

**Build**: no new dependencies (Tomlyn is already shipped/deployed by `purrTTY.GameMod.csproj`).
Note the stale csproj comment "In-world terminal settings persistence (TOML)…" can be repurposed.

---

## 13. gatOS recipes (concrete `startup_command` values)

From the gatOS investigation — all run in the interactive login PTY (env + `/sim` present):

| Goal | `startup_command` |
|---|---|
| Landing-guidance TUI (flagship) | `cd /root/land-o-matic && cargo run --release` |
| Fleet console / flight controls | `cd /root/dashboard-rs && cargo run --release` |
| Throttle/ignite control panel | `cd /root/gogogo-rs && cargo run --release` |
| Zero-build telemetry watch (ash-friendly) | `watch -n 0.2 cat /sim/vessels/active/telemetry` |
| Star-map TUI (one-time `apk add`) | `apk add astroterm && astroterm -u -C -c -s 100 -f 30 -i auckland` |

Caveats to surface in docs: `cargo run` requires a one-time `apk add --no-cache cargo rust` and the
example sources present in the guest; `jq` is not preinstalled. gatOS sessions are not
reattachable (no tmux), so a relaunched terminal starts a fresh shell and re-runs its
`startup_command`.

---

## 14. Edge cases & gotchas

1. **Name collision (R6)** — pre-checked per spec; logged + skipped; surfaced in the UI banner.
2. **Anchor drift (§3.5)** — accepted for now: we persist vehicle + part/sub-part id; a restore can
   land on a different same-type part, and a renamed/missing vehicle falls back to the controlled
   vehicle. `part_name` is stored for human re-picking. Improves when the game exposes embeddable
   per-part metadata.
3. **In-world create needs an active GUI frame** — apply only inside `OnAfterUi`/`OnAfterGui`; never
   from a background thread.
4. **gatOS VM boot on apply** — loading a layout with a gatOS terminal boots the QEMU VM and runs its
   `startup_command` at that moment (only ever on explicit user apply — there is no auto-apply).
5. **2D grid is pixel-derived (never stored)** — a window persists only pos/size; the emulator
   recomputes cols/rows from pixel size ÷ the resolved theme's cell every frame (`TrySetGridSize ==
   false`), so the grid re-flows correctly when the theme/font changes. In-world stores cols/rows
   (authoritative texture extent).
6. **Teardown uses the safe path** — in-world via `Remove` (deferred 2-frame GPU free), never the
   shutdown-only `Dispose(freeGpu:false)`.
7. **Custom-shell timing** — gatOS `StartAsync` may block on VM boot; the post-start stdin write
   only runs after it returns, so injection can't race ahead of channel-open.
8. **Theme missing on load** — `catalog.Find` returns null → terminal uses the global default theme
   (no crash). The UI can warn when a referenced theme name isn't found.
9. **Capture without retained launch options** — if §7's enabling change is deferred, 2D capture
   degrades to the configured default shell; loaded/hand-written layouts are unaffected.
10. **Atomic writes** — layout saves go through `AtomicFile` (temp + rename), like themes/config.

---

## 15. Testing

Following `docs/build-and-test.md` (quiet; **no fixed sleeps**):

- **`purrTTY.Terminal.Tests`** (integration):
  - `StartupCommand` round-trip: start a session against a controllable fake/echo process manager
    with `StartupCommand="X"`; assert the input queue received `"X\n"` (await the write completion /
    inspect the fake — no sleep).
  - `ProcessLaunchOptions.Clone()` copies `StartupCommand`.
- **`purrTTY.Display.Tests`** (pure logic, no ImGui):
  - `ShellSpec` ↔ `ProcessLaunchOptions` round-trip (all `ShellType`s, args, env, startup command).
  - `TerminalLayout` TOML round-trip via `TomlSerializer` (string-shims for `kind`/`shell_type`/`mode`;
    unknown enum values fall back; vectors expand to discrete fields). Mirror `ThemeTomlFormatTests`.
  - `LayoutCatalog` save/load/delete against a temp dir (set `ThemeConfiguration.OverrideConfigDirectory`).
  - Collision policy as a unit: given a fake set of live names, `Apply`'s planner skips colliding
    specs and reports them (extract the create-vs-skip decision into a pure method to test without
    a renderer).
  - Layout editing round-trip (R8): load a `TerminalLayout`, remove / rename / edit-attribute an
    entry, save, reload, and assert the mutation persisted and intra-layout names stayed unique.
    Confirm a window entry never serializes cols/rows or font.

---

## 16. Docs to update (instruction-maintenance mandate)

- `CLAUDE.md`: note the new `Layouts/` subsystem and that layouts are an opt-in persisted set on top
  of the still-session-only ad-hoc terminals (revises the refactor plan's decision #3 framing).
- `docs/code-navigation.md`: add the `Layouts/` files and the `StartupCommand` field.
- `docs/gotchas.md`: add the anchor-identity limitation (§3.5), apply-is-user-initiated + must-run-on-a-GUI-frame
  (§10), and the stdin-injection rationale (§6).
- `docs/how-to.md`: "Create/save/load a terminal layout"; "Auto-run a command on a terminal";
  the gatOS recipes (§13).
- `plans/TERMINAL_MANAGEMENT_REFACTOR_PLAN.md`: cross-link this plan from its §9 deferred items.

---

## 17. Implementation phases (checklist)

- [ ] **P1 — Auto-run mechanism (independently useful).** `ProcessLaunchOptions.StartupCommand` +
      injection in `TerminalSession.InitializeAsync` + `Clone()` + tests. Expose a "Startup command"
      field in the In-World create form. *Delivers R4/R5 on its own.*
- [ ] **P2 — Persistence plumbing.** Make `AtomicFile` public; `ShellSpec`, `LayoutDefinition`,
      `LayoutCatalog` (+ `GetLayoutsDirectory`); TOML round-trip tests.
- [ ] **P3 — Apply (load).** `WindowLayoutRecord` + `CreateConfiguredWindow`/`CloseWindow` on the
      controller; `TerminalWindow` desired-name + `RequestPlacement`; `LayoutManager.Apply` with
      collision skip (R6) and `_loaded` tracking. *Delivers R2/R3/R6 for hand-written/loaded layouts.*
- [ ] **P4 — Teardown.** `LayoutManager.TeardownSet` / `TeardownAllLoaded`. *Delivers R7.*
- [ ] **P5 — Capture (save current).** Retain `TerminalSession.LaunchOptions`; `CaptureWindows`;
      `CaptureCurrentAs`. *Delivers R1 fully.*
- [ ] **P6 — UI + menu + layout editing.** `LayoutManagerUI` (list / save-current / apply-result /
      **edit a saved layout**: remove + rename + edit-attributes per terminal, R8);
      `TerminalMenus.OpenLayoutManager`; wire in `TerminalMod`. *Delivers R8.*
- [ ] **P7 — Docs** (§16).

---

## 18. Open decisions (recommended defaults shown)

1. **Appearance: reference by theme name (recommended) vs embed inline.**
   *Recommended:* store `theme = "<name>"` and rely on the existing `ThemeCatalog` (consistent with
   how in-world opacities already persist only via named themes). *Alternative:* embed the full
   `ThemeDefinition` per terminal for self-contained layouts that capture unsaved live tweaks — add
   later behind a "snapshot appearance" toggle if wanted.
2. **File granularity: one file per layout (recommended) vs one multi-layout file.**
   *Recommended:* one `layouts/<name>.toml`, mirroring `themes/*.toml` (easy share/delete, matches
   the existing catalog pattern).
3. **Multi-tab 2D capture: include (recommended) vs single-shell windows only.**
   *Recommended:* capture an ordered tab list; single-tab is the degenerate common case.
4. **`InWorldTerminalManager.Create` name handling.** Optionally tighten it to honor an
   already-unique name instead of always calling `SuggestUniqueName`, so layout names round-trip
   exactly. Not required (the pre-check makes the suggest a no-op).
```
