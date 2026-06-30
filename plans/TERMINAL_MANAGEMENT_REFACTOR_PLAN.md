# TERMINAL_MANAGEMENT_REFACTOR_PLAN — Named terminals, per-target theming, and N in-world instances

> Status: **COMPLETE** (Phases 0–9 implemented, in-game tested, and documented). Phase-by-phase,
> each phase built clean + was in-game testable before proceeding, matching the cadence of
> [GAME_SPACE_QUAD_PLAN.md](GAME_SPACE_QUAD_PLAN.md).
>
> Deviations from the plan as written: the in-world singleton kept the name
> `InWorldTerminalManager` (the plan proposed `InWorldTerminalCoordinator`) to reduce churn — it is
> the thin-coordinator role regardless; its statics are `Active`/`Instance`/`IsInputFocused`
> (rather than `HasDrawableInstances`/`FocusedId`/`IsAnyInputFocused`). In-world grid/font/shell
> changes are a `Recreate` (Remove + Create, deferred GPU free) rather than an in-place
> `TrySetGridSize` (which returns false). See [../docs/gotchas.md](../docs/gotchas.md) gotchas 24–27
> and [../CLAUDE.md](../CLAUDE.md) for the as-built behavior.

## 0. Goal & scope (what we are building)

Three intertwined changes that make terminals easier to manage:

1. **A "terminal theme" is a complete bundle** — color palette **+** font family/size **+** the
   three opacities (foreground / background / cell-background) **+** cursor/border/lock/hot-zone.
   Editing happens in **one new Theme dialog**, replacing today's scattered theme/font/opacity
   menu items.

2. **Themes apply to a *selected* terminal** — the Theme dialog has a **filtering dropdown picker**
   (our standard pattern) listing every terminal by name, defaulting to the **focused** terminal.
   "Apply to focused, or pick one by name" is the whole interaction. The selected target can be a
   2D window **or** an in-world instance.

3. **N in-world terminals, not one** — a new **In-World Terminal Manager** dialog creates and
   configures N independent in-world terminals (each: a **required unique name**, a shell, a fixed
   cols×rows, part-anchored or billboard placement, and a theme). The single hard-coded
   `InWorldTerminalManager` instance is split into a thin coordinator + per-instance objects.

**Unifying abstraction:** every terminal — 2D window or in-world instance — is a **named terminal**
in a single registry. Names are the new way to address terminals for theme application, sizing, and
in-world placement.

### Locked design decisions (confirmed with the user)

| Decision | Choice |
|---|---|
| Naming scope | **Unify 2D + in-world.** One registry. 2D windows get an auto-unique *editable* name; in-world instances require an explicit name at creation. |
| Theme editing target | **Edit the *selected* target.** The dialog's picker (default = focused) chooses which terminal every edit/apply hits — 2D or in-world. Changing font size on an in-world instance regenerates its offscreen texture. |
| In-world persistence | **Session-only.** In-world terminals are created fresh each session via the Manager dialog; nothing auto-restores. No per-instance TOML. (Greatly simplifies the in-world settings work.) |

### Out of scope (deferred)

- Per-2D-window theme **persistence across restarts** (saving a theme into the named catalog still
  works; new windows still seed from the configured default theme).
- A from-scratch **color editor** (RGB pickers for the 16-color palette). Today there is no
  color-editing UI; "themes" are applied/saved as bundles. This plan keeps that model — it bundles
  font+opacity into a theme and makes themes target-addressable, but does **not** add palette
  editing. (Easy follow-up: the dialog has the obvious home for it.)
- In-world **live theme editing** is supported per decision #2, but **billboard click-to-focus**,
  the **billboard forward-axis sign** verification, and **per-instance billboard tuning** remain as
  the previously-deferred in-world follow-ups.

---

## 1. What already exists (verified current state)

The good news from the deep analysis: **most of the data model the user wants is already present.**
This refactor is mostly *consolidation + addressing + N-ification*, not new core types.

### 1.1 Theming — the bundle already exists

- **`ThemeDefinition`** (`purrTTY.Display/Theming/ThemeDefinition.cs:104-139`) is already a complete
  bundle: required `ThemeColors Colors` plus **optional** `FontFamily`, `FontSize`,
  `BackgroundOpacity`, `ForegroundOpacity`, `CellBackgroundOpacity`, `CursorStyle`, `CursorBlink`,
  `BorderOnFocus/OnHover`, `BorderOpacity`, `LockMode`, and the hot-zone fields. (Optional `null` =
  "keep target's current value".) **This is exactly the "theme encompasses everything" the user
  asked for** — it just isn't always *populated* on save, and isn't *target-addressable*.
- **`ThemeColors`** (`ThemeDefinition.cs:37-95`): 16 ANSI + fg/bg/cursor/selection;
  `ToEngineTheme()` expands 16→256.
- **`ThemeCatalog`** (`purrTTY.Display/Theming/ThemeCatalog.cs`): enumeration + CRUD —
  `Default`/`BuiltInThemes`/`UserThemes`, `Find` (`:45`), `SaveUserTheme` (`:64`),
  `DeleteUserTheme` (`:79`), `Refresh` (`:32`). 18 built-in TOML themes in
  `purrTTY.GameMod/TerminalThemes/*.toml` (colors only).
- **`TerminalWindowSettings`** (`purrTTY.Display/Ghostty/TerminalWindowSettings.cs:16-57`) is the
  **per-window live state** holding all of the above: ThemeName, Colors, FontFamily, FontSize, the
  three opacities, cursor/border/lock/hot-zone. **Theming is already per-window at runtime.**

### 1.2 The global↔per-window seams (what "ONLY per-window" must rework)

Runtime state is per-window; **persistence + new-window seeding funnels through one global
`ThemeConfiguration`**. The exact seams:

- **Read (global→window):** `GhosttyTerminalController.CreateWindowSettingsFromDefaults()`
  (`GhosttyTerminalController.cs:205-235`) seeds every new window from the single
  `_config.SelectedThemeName/Font*/.*Opacity/...`.
- **Duplicate read:** `InWorldTerminalRenderer.BuildSettings()`
  (`purrTTY.Display/Ghostty/InWorldTerminalRenderer.cs:233-253`) reads the same global config.
- **Write (window→global):** `GhosttyTerminalController.PersistDisplayDefaults()` (`:193-203`) →
  `ThemeConfiguration.SyncRuntimeDisplaySettings()` (`ThemeConfiguration.cs:472-508`) copies the
  focused window's whole settings into the single `[settings]` slot (incl. `SelectedThemeName`).

### 1.3 Focus & target resolution already exist (2D)

- **`GhosttyTerminalController.FocusTarget`** (`GhosttyTerminalController.cs:58-88`) already resolves
  "the focused window, else last-focused, else first" — this is the seed for "apply to focused".
- Per-window focus flows from `ImGui.IsWindowFocused()` → `TerminalWindow._hasFocus` →
  `FocusChanged` → `_lastFocusedWindow`. Static `IsAnyTerminalActive`
  (`GhosttyTerminalController.cs:39`) is the OR over windows of `HasFocus || IsContextMenuOpen`.

### 1.4 The scattered theming UI (what gets consolidated)

All in `purrTTY.GameMod/TerminalMenus.cs`, acting on `controller.FocusTarget`:
`DrawThemeMenu` (`:316`, palette pick + save/delete/refresh), `DrawFontMenu` (`:377`, size+family),
`DrawFocusMenu` (`:434`, cursor/blink/border/lock/hot-zone + opacity), `DrawWindowMenu` (`:589`,
chrome/perf/kitty toggles + the 3 opacity sliders). The widget bodies here are **lifted into the new
dialog**; non-theme toggles (chrome/perf/kitty) stay as a small "Window" section.

### 1.5 Sessions / shells / sizing

- **Create:** `SessionManager.CreateSessionAsync(title, launchOptions, ct)`
  (`purrTTY.Terminal/Sessions/SessionManager.cs:126`). Dimensions ride on
  `ProcessLaunchOptions.InitialWidth/Height` (`ProcessLaunchOptions.cs:85-90`), overridden by the
  manager's last-known dims (`SessionManager.cs:150-152`).
- **Resize:** there is **no aggregate resize** — the frontend calls four things in sequence
  (`TerminalWindow.cs:384-389`): `Surface.Resize(cols,rows,cellW,cellH)`,
  `session.UpdateTerminalDimensions`, `ProcessManager.Resize`,
  `Sessions.UpdateLastKnownTerminalDimensions`. The same sequence exists in
  `InWorldTerminalRenderer.cs:179-182`. **We will wrap this in one helper** (see §4.7).
- **Shells:** `ShellType` enum = `Auto, PowerShell, Wsl, PowerShellCore, Cmd, Custom, CustomGame`
  (`ProcessLaunchOptions.cs:6`). `CustomGame` → `CustomShellRegistry.Instance.CreateShell(id)` via
  `CustomShellPtyBridge`. Enumerate for a picker from **two** sources, exactly as the 2D menu does:
  `ShellMenuCache.Current` (`purrTTY.GameMod/ShellMenuCache.cs`, native PTY shells, detected once
  off-thread) + `CustomShellRegistry.Instance.GetAvailableShells()`
  (`purrTTY.CustomShellContract/CustomShellRegistry.cs:83`, in-process shells).
- **gatOS clarification:** "gatOS" is **not a distinct shell type today** — there is no gatOS code
  in the three projects. It is a remote Linux env reached via **SSH through an ordinary PTY shell**
  (see `plans/OS_PLAN.md`, `plans/KITTY_PLAN.md:214-216`). The only built-in custom shell is
  **`GameConsoleShell`** (id `"GameConsoleShell"`, the in-process KSA console). **Design
  consequence:** the in-world shell picker reuses the *same enumeration* the 2D menu uses, so
  `GameConsoleShell` and any future-registered shell (incl. a future gatOS custom shell, or an
  SSH-launching PTY profile) appear automatically — no special-casing.

### 1.6 In-world subsystem — single-instance inventory

`InWorldTerminalManager` (`purrTTY.GameMod/InWorld/InWorldTerminalManager.cs`) **conflates two
roles**: (a) a singleton coordinator (statics `Active`:50 / `IsInputFocused`:57 / `Instance`:60, the
postfix seam, per-frame driving, input arbitration), and (b) **one terminal instance**
(`_target/_ctx/_backend/_perFrame/_content/_quad/_picker/_settings`, fields `:30-41`).

Per-instance GPU graph (one of each, per terminal): `OffscreenRenderTarget` (color+depth+sampler+
renderpass, ~4 MB color at 1024²), `OffscreenImGuiContext` (shares the **main font atlas** — cheap),
`OffscreenImGuiBackend` (descriptor pool 256, bound to that renderpass), `PerFrameRenderer`
(cmd pool + 2 cmd buffers + 2 fences + **one extra queue submit/frame**), `InWorldTerminalRenderer`
(its **own** `SessionManager` = one shell, fonts, app-mouse state), `InWorldQuad`.

**`InWorldQuad` waste at N:** its two pipelines + pipeline layout + descriptor-set *layout* + vertex
input + unit-quad VB/IB are **identical** for every instance; only the descriptor *set* (which
image) and the MVP push-constant differ (`InWorld/Display/InWorldQuad.cs`). Hoist the identical bits
to a **shared quad resource** (§4.8).

**Statics that block N:** `Active` (postfix gate, `RenderMainPassPatch.cs:29`; cleared on any quad
failure `:43` → kills all), `Instance` (the single quad the postfix draws, `:36`), `IsInputFocused`
(one global focus bool, read by `Patcher.cs` Patch01/Patch03 + `TerminalMod.cs:86` hotkey gate).

---

## 2. Target architecture

```
NAMED-TERMINAL REGISTRY  (new, in purrTTY.Display)
   INamedTerminal  ── Name (unique) + Kind + HasFocus + ApplyTheme(ThemeDefinition) + TrySetGridSize(cols,rows)
   TerminalTargetRegistry  ── register/unregister, enumerate All, Resolve(name), Focused, name-uniqueness
        ▲                         ▲
        │ implements              │ implements
   TerminalWindow (2D)        InWorldTerminalInstance (3D)
        │                         │
   GhosttyTerminalController   InWorldTerminalCoordinator  (was InWorldTerminalManager; thin singleton)
                                   └── List<InWorldTerminalInstance>  + shared quad resource + Manager dialog + picker

THEME DIALOG  (new, purrTTY.GameMod)  ── target picker (FilterCombo) → edits/applies to the selected INamedTerminal
IN-WORLD MANAGER DIALOG  (new)        ── create/list/configure InWorldTerminalInstance (name, shell, cols×rows, placement, theme)
FilterCombo  (new reusable widget)    ── BeginCombo + ImInputString filter box + substring match
```

The two render worlds stay independent at the GPU level (2D = main ImGui pass; in-world = N
offscreen passes + scene postfix). They are unified only at the **addressing layer**
(`INamedTerminal`) and the **theme bundle** (`ThemeDefinition`).

---

## 3. Component design

### 3.1 `TerminalTheme` bundle — formalize what a theme captures

No new type needed; **`ThemeDefinition` is the bundle.** The work is to guarantee a theme is
*complete* when saved and *fully applied* when selected:

- **Save:** `TerminalWindow.SnapshotAsTheme()` (today captures colors only,
  `TerminalWindow.cs:185`) must also populate `FontFamily`, `FontSize`, all three opacities, and
  cursor/border/lock/hot-zone from the live `Settings`. So a saved theme round-trips the **whole**
  appearance, not just colors. (`ThemeTomlFormat` already has `[font]/[window]/[cursor]/[focus]/
  [lock]` sections — `ThemeTomlFormat.cs` — they're just not always written today.)
- **Apply:** applying a `ThemeDefinition` to a target sets every non-null field. This already exists
  as `TerminalWindow.ApplyTheme()` (`TerminalWindow.cs:174`); we route it through `INamedTerminal`.

### 3.2 `INamedTerminal` + `TerminalTargetRegistry` (new, `purrTTY.Display`)

```csharp
public enum TerminalKind { Window, InWorld }

public interface INamedTerminal
{
    string Name { get; }                       // unique within the registry
    TerminalKind Kind { get; }
    bool HasFocus { get; }
    void ApplyTheme(ThemeDefinition theme);     // full bundle apply
    bool TrySetGridSize(int cols, int rows);    // true for in-world (fixed grid); false for 2D (pane-driven)
    bool TryRename(string newName);             // false if duplicate/blank
}

public static class TerminalTargetRegistry
{
    public static IReadOnlyList<INamedTerminal> All { get; }
    public static INamedTerminal? Focused { get; }            // the one with HasFocus (2D or in-world)
    public static bool Register(INamedTerminal t);            // false on duplicate name
    public static void Unregister(INamedTerminal t);
    public static INamedTerminal? Resolve(string name);
    public static string SuggestUniqueName(string baseName);  // "Terminal 2", "Terminal 3", ...
}
```

- Lives in `purrTTY.Display` (both `TerminalWindow` and `InWorldTerminalRenderer`/instance reference
  Display types; the in-world coordinator in GameMod references Display already).
- **Uniqueness** enforced on `Register`/`TryRename` (case-insensitive). Blank rejected.
- `Focused` is the bridge to "apply to the focused terminal": it returns whichever registered
  terminal reports `HasFocus` (2D windows via `_hasFocus`; in-world via the coordinator's focused
  instance token). At most one is focused (mutual exclusion already enforced between 2D and in-world
  in `InWorldTerminalManager.OnAfterGui:190-194`).

### 3.3 2D `TerminalWindow` becomes `INamedTerminal`

- Add `string Name` to `TerminalWindowSettings` (or the window), seeded
  `TerminalTargetRegistry.SuggestUniqueName("Terminal")` on `OpenWindow`
  (`GhosttyTerminalController.cs:91`). **Auto-unique, editable** (rename in the Theme dialog).
- `Kind => Window`; `HasFocus => _hasFocus`; `ApplyTheme` = existing `ApplyTheme`;
  `TrySetGridSize` returns **false** (2D windows size to their ImGui pane — see `TerminalWindow.cs:
  373-399`; manual cols/rows isn't meaningful there, the dialog shows "auto (pane-driven)").
- Register on open, unregister on prune (`GhosttyTerminalController.cs:319-337`).
- Show the name in the window title bar so the user can correlate picker entries with windows.

### 3.4 `FilterCombo` — the standard filtering dropdown (NEW reusable widget)

**This does not exist yet** — there is no text-filter-inside-a-combo anywhere in the codebase. Build
it once from the proven building blocks and reuse it for the theme picker, the target picker, and
the in-world shell picker.

```csharp
// purrTTY.GameMod/UI/ImGuiWidgets.cs  (GameMod-side; uses Brutal.ImGuiApi + ImInputString)
// Returns true on the frame an item is chosen; selectedKey set to that item's key.
public static bool FilterCombo(
    string id, string preview, ImInputString filter,
    IReadOnlyList<(string Key, string Label)> items, out string? selectedKey);
```

Implementation notes (from `InWorldLaunchUI.DrawPartCombo:170-218` + the modal text-field at
`TerminalMod.cs:381-397`):
- `BeginCombo(id, preview)`; first row is the filter `InputText` (focus it on
  `IsWindowAppearing`); then `Selectable` rows filtered by **case-insensitive substring** of
  `Label`. Disambiguate duplicate labels with `##{Key}` (`InWorldLaunchUI.cs:212`).
- **CRITICAL gotcha** — `ImInputString` + Brutal `InputText` only refreshes `.Length` on a *true*
  return. For a live filter read every frame, **do NOT pass `EnterReturnsTrue`**, or call
  `filter.EvaluateLength()` each frame before reading `.ToString()` (`TerminalMod.cs:389-395`).
  Otherwise the buffer reads empty while typing and the filter never narrows. See memory
  `[[iminputstring-enterreturns-true]]`.

### 3.5 Theme dialog (NEW modal, `purrTTY.GameMod`)

Clone the `RenderSaveThemeModal` scaffold (`TerminalMod.cs:363-454`) for the open/draw lifecycle;
lift the appearance widgets out of `TerminalMenus` `DrawFocusMenu`/`DrawWindowMenu`/`DrawFontMenu`/
`DrawThemeMenu`. Layout:

1. **Target row** — `FilterCombo` over `TerminalTargetRegistry.All` (label = `Name (Window|In-World)`),
   default-selected to `TerminalTargetRegistry.Focused`. A "↻ focused" button re-snaps to focused.
   All edits below apply to **the selected target** via `INamedTerminal`.
2. **Palette** — `FilterCombo` over `ThemeCatalog` built-in + user themes → `target.ApplyTheme(def)`.
   "Save current as…" (opens existing save-theme modal, now capturing the **full** bundle §3.1),
   "Delete", "Refresh".
3. **Font** — family `FilterCombo` (from `PurrTTYFontManager.GetAvailableFontFamilies()`) + size
   `DragInt` (clamp 4-72). For an in-world target, a font-size change triggers
   `instance.Resize(...)` (texture regen, §4.7).
4. **Opacity** — three `DrawOpacitySlider`-style sliders (Foreground / Background / Cell-background),
   persisted on `IsItemDeactivatedAfterEdit()` (`TerminalMenus.cs:635-655`).
5. **Advanced** (collapsing header) — cursor style/blink, border on focus/hover + opacity, lock
   mode, hot-zone. (These are already in `ThemeDefinition`; keep them but de-emphasized.)

Persistence idiom unchanged: mutate `target`'s settings → for 2D, `controller.PersistDisplayDefaults`;
for in-world, nothing persisted (session-only) but the live surface updates.

**Menu change:** `DrawThemeMenu`/`DrawFontMenu` and the opacity/cursor/border bits of
`DrawFocusMenu`/`DrawWindowMenu` are **removed** and replaced by a single **"Theme…"** menu item
(new `TerminalMenus.OpenThemeDialog` delegate). The chrome/perf/kitty toggles (not part of a theme)
stay in a slimmed "Window" submenu.

### 3.6 In-world: coordinator / instance split

**`InWorldTerminalInstance` (new, `purrTTY.GameMod/InWorld/`)** — everything per-terminal, lifted
from today's `InWorldTerminalManager`:

```csharp
public sealed class InWorldTerminalInstance : INamedTerminal, IDisposable
{
    public string Name { get; private set; }         // required, unique
    public TerminalKind Kind => TerminalKind.InWorld;
    public bool HasFocus => _coordinator.FocusedId == _id;

    private readonly Guid _id;
    private InWorldTerminalRecord _record;            // in-memory settings (mode, cols/rows, placement, theme)
    private OffscreenRenderTarget _target;
    private OffscreenImGuiContext _ctx;
    private OffscreenImGuiBackend _backend;
    private PerFrameRenderer _perFrame;
    private InWorldTerminalRenderer _content;         // its own shell session + fonts
    private InWorldQuad _quad;                         // descriptor set + MVP only (pipeline shared, §4.8)

    public void Frame(double dt);                      // was PerFrameRenderer.Frame driving
    public void RecordDraw(CommandBuffer cmd);         // quad draw; failure disables ONLY this instance
    public bool TryRaycast(Ray ray, out double t, out float2 uv);
    public void ApplyTheme(ThemeDefinition theme);     // rebuild settings/fonts; resize texture if font size changed
    public bool TrySetGridSize(int cols, int rows);    // regen target+quad descriptor; true
}
```

Build/teardown is the existing `BuildResources`/`TeardownResources` body
(`InWorldTerminalManager.cs:238-344`) moved verbatim into the instance, parameterized by `_record`.

**`InWorldTerminalCoordinator` (new; renamed/reduced `InWorldTerminalManager`)** — the singleton:

```csharp
public sealed class InWorldTerminalCoordinator : IDisposable
{
    public static InWorldTerminalCoordinator? Instance { get; private set; }
    public static bool HasDrawableInstances;          // was Active: true iff ≥1 live instance
    public static Guid? FocusedId;                     // was IsInputFocused (bool) → which instance
    public static bool IsAnyInputFocused => FocusedId != null;   // collapses the old bool reads

    private readonly List<InWorldTerminalInstance> _instances = new();
    public IReadOnlyList<InWorldTerminalInstance> Instances => _instances;
    private SharedQuadResource _sharedQuad;            // §4.8
    private InWorldManagerUI _managerUI;               // §4.9
    public void OnAfterGui(double dt);                 // draw manager UI, arbitrate focus, picker, drive each instance
    public InWorldTerminalInstance Create(InWorldTerminalRecord record);  // builds + registers
    public void Remove(InWorldTerminalInstance inst);
}
```

`Patcher.cs` Patch01/Patch03 + `TerminalMod.cs` hotkey gate change from
`InWorldTerminalManager.IsInputFocused` → `InWorldTerminalCoordinator.IsAnyInputFocused` (mechanical
rename; same meaning).

### 3.7 In-world per-instance record (in-memory; session-only)

```csharp
public sealed class InWorldTerminalRecord   // NOT persisted (decision #3)
{
    public string Name;                       // required, unique
    public ProcessLaunchOptions Launch;       // shell choice (built from the picker)
    public int Cols = 100, Rows = 30;         // FIXED grid (user-chosen); texture derived from cols×rows×cell
    public string Mode = "part";              // "part" | "billboard"
    public string? ThemeName;                 // applied from catalog at create
    // part: TargetPartId, PartOffset[3], PartRotation[3], PartMeters[2]
    // billboard: Distance, Offset[2], Meters[2], AlwaysOnTop
}
```

- **Grid-driven sizing (new):** in-world terminals get a **fixed cols×rows** (user-chosen in the
  create dialog). Texture extent = `cols*cellPxW × rows*cellPxH` from the instance's theme font,
  clamped to `[256,4096]` per axis (existing clamp, `InWorldTerminalManager.cs:249-250`). This
  replaces today's texture-pixel-first model — it matches the user's "pick its row/col dimensions"
  mental model and makes resize well-defined.
- **One resize helper:** add `TerminalSession.ApplyGridSize(cols, rows, cellW, cellH)` (or a
  `SessionManager` method) wrapping the **four-call sequence** that today is duplicated at
  `TerminalWindow.cs:384-389` and `InWorldTerminalRenderer.cs:179-182`. Both the 2D path and the
  in-world instance call it. (Pure consolidation; reduces drift.)
- **`InWorldSettings` TOML retired.** With session-only instances there is no per-instance
  persistence; `purrtty-inworld.toml` and `InWorldSettings.LoadOrDefault/Save` are removed. The
  Manager's create-form defaults are sensible constants (optionally remembered in-memory for the
  session). The `PURRTTY_INWORLD` dev gate (`InWorldTerminalManager.cs:348`) changes to "create one
  default in-world instance on load" for convenience.

### 3.8 Shared quad resource (hoist the identical GPU state)

`SharedQuadResource` (coordinator-owned, one copy): the two pipelines (`_pipelineDepthWrite` +
`_pipelineNoDepth`), pipeline layout, descriptor-set layout, vertex input, and the unit-quad
VB/IB — all identical across instances. `InWorldQuad` shrinks to **per-instance state only**: the
descriptor *set* (binding this instance's `OffscreenRenderTarget.ColorImageView` + `Sampler`) and the
MVP push-constant. `RecordDraw` binds the shared pipeline, then this instance's descriptor set +
push constant. Saves N× pipeline/layout/geometry allocations. **Deferrable** — N works without it
(each quad self-contained), but it's the dominant per-instance GPU-cost win and worth doing once N
is proven (Phase 7).

### 3.9 In-World Terminal Manager dialog (NEW modal, `purrTTY.GameMod`)

`InWorldManagerUI` — supersedes `InWorldLaunchUI` (reusing its `DrawPartForm`/`DrawBillboardForm`/
`DragRow` bodies, `InWorldLaunchUI.cs:142-230`). Two panes:

- **Instance list** — each live instance: name, mode, shell, cols×rows, focused marker, buttons
  [Configure] [Focus] [Close]. "Focus" sets `FocusedId` (billboard focus path; part mode is also
  click-to-focus via the picker).
- **Create / Configure form** — **Name** (`ImInputString`, **required, unique** — disabled "Create"
  until non-blank & not a duplicate, mirroring the save-theme modal's validation
  `TerminalMod.cs:415-425`); **Shell** (`FilterCombo` over `ShellMenuCache.Current` +
  `CustomShellRegistry.GetAvailableShells()`); **Cols** / **Rows** (`DragInt`, clamped); **Mode**
  buttons (Part / Billboard) → the existing tailored placement form; **Theme** (`FilterCombo` over
  the catalog). Create → `coordinator.Create(record)` → builds GPU graph + shell + registers in
  `TerminalTargetRegistry`.

Each instance needs **unique popup ids** (`"...##inworld_{id}"`) since N configure-popups can exist.

### 3.10 Render postfix, focus token, picking — N-aware

- **Postfix** (`RenderMainPassPatch.cs`): iterate the coordinator's live instances, each
  `RecordDraw` in its own try/catch so a single failing quad disables **only that instance**, not
  all:
  ```csharp
  if (!InWorldTerminalCoordinator.HasDrawableInstances) return;
  foreach (var inst in InWorldTerminalCoordinator.Instance!.Instances)
      try { inst.RecordDraw(cmd); } catch { inst.DisableSelf(); }
  ```
- **Picking** (coordinator, replacing `QuadPicker`'s single-quad test): on left click, ray-test
  **every part-mode** instance's quad with `Cursor.InputRay`, choose the **nearest by the `t`**
  `TryRaycast` already returns (`InWorldQuad.cs:309`), focus that instance; a click in empty world
  space (and `!WantCaptureMouse`) clears `FocusedId`. Billboard instances stay menu-focused.
- **Per-frame mouse→cell** (the existing app-mouse forward, `InWorldTerminalManager.cs:208-217`)
  routes to the **focused** instance only.

### 3.11 Menu wiring (`TerminalMenus.cs` / `TerminalMod.cs`)

- New static delegates on `TerminalMenus`: `OpenThemeDialog`, `OpenInWorldManager`. Assigned in
  `TerminalMod.InitializeTerminal`, nulled in `DisposeResources` (same idiom as the existing 5
  in-world delegates, `TerminalMenus.cs:54-67`, `TerminalMod.cs:166-170`/`:812-823`).
- Menu items: **"Theme…"** (opens §3.5), **"In-World Terminals…"** (opens §3.9). Remove the old
  single "In-World Terminal" toggle / "Configure In-World…" / "Focus In-World Terminal" items and
  the scattered theme/font/opacity menus.
- Both dialogs pumped each frame in `OnAfterUi` and OR'd into `modalVisible` (the hotkey suppressor,
  `TerminalMod.cs:80-86`).

---

## 4. Persistence changes

| File | Today | After |
|---|---|---|
| `purrtty.toml` `[settings]` | single global theme/font/opacity slot, written by `SyncRuntimeDisplaySettings` | unchanged shape, but reframed as **"default theme for new windows"** only. Per-window live edits no longer overwrite it unless the user saves a named theme. |
| `themes/*.toml` (catalog) | colors-mostly; font/opacity sections optional+rarely written | **always written** on save (full bundle §3.1). No format change — `ThemeTomlFormat` already supports the sections. |
| `purrtty-inworld.toml` | single `InWorldSettings` POCO | **retired** (decision #3, session-only in-world). |

No new files. No migrations (in-world persistence is dropped, not migrated).

---

## 5. Input & focus across N

- **One keyboard owner at a time.** `FocusedId` (in-world) is mutually exclusive with 2D focus —
  keep the existing guard (`if GhosttyTerminalController.IsAnyTerminalActive → clear in-world
  focus`, `InWorldTerminalManager.cs:190-194`). `TerminalTargetRegistry.Focused` returns the single
  focused terminal across both worlds.
- **Game-key gating** stays correct: `Patch01.TerminalOwnsKeyboard` and `Patch03_HotkeyGuard`
  (`Patcher.cs`) read `GhosttyTerminalController.IsAnyTerminalActive ||
  InWorldTerminalCoordinator.IsAnyInputFocused` — unchanged semantics after the rename.
- **Keyboard forward** to the focused in-world instance via the existing
  `TerminalInputEncoder.ProcessKeyboard` path (`InWorldTerminalManager.cs:200-204`); Esc still
  reaches the shell (no Esc-unfocus).

---

## 6. Phased implementation plan (ordered; each builds + is in-game testable)

> Cadence matches the prior feature: implement a phase, `dotnet build purrtty.slnx` +
> `dotnet test`, then the user tests in-game before the next phase.
>
> **All phases below (0–9) are ✅ done, in-game tested, and documented.**

- **Phase 0 — Theme bundle is complete.** Make `SnapshotAsTheme` capture font + all opacities +
  cursor/border/lock; ensure apply sets every non-null field; verify `ThemeTomlFormat` round-trips
  the full bundle. *No UI change.* Test: "Save Current As…", inspect the `themes/*.toml` has
  `[font]`/`[window]` sections; apply it to a fresh window → font+opacity restored.

- **Phase 1 — `FilterCombo` widget.** Build the filtering dropdown (§3.4) with the `EvaluateLength`
  gotcha handled. Drop it into one existing combo (e.g. the in-world part picker) to prove it.
  Test: typing narrows the list live.

- **Phase 2 — `INamedTerminal` + registry + 2D naming.** Add the interface/registry; make
  `TerminalWindow` implement it (auto-unique editable name, shown in title bar); register/unregister
  on open/prune. *No behavior change* beyond names appearing. Test: open 3 windows → 3 unique names;
  rename one; close one → registry prunes.

- **Phase 3 — Theme dialog (2D only).** New modal with target picker (`FilterCombo` over the
  registry, default focused) + palette/font/opacity/advanced, editing the selected 2D window.
  Remove the old scattered theme/font/opacity menu items; add "Theme…". Test: edit window A's theme,
  pick window B in the dropdown, edit B independently; "apply to focused" follows focus.

- **Phase 4 — In-world coordinator/instance split (still 1 instance).** Extract
  `InWorldTerminalInstance`; reduce `InWorldTerminalManager`→`InWorldTerminalCoordinator` holding a
  one-element list; statics → `HasDrawableInstances`/`FocusedId`/`IsAnyInputFocused`; postfix
  iterates; mechanical renames in `Patcher.cs`/`TerminalMod.cs`. **Pure refactor — identical
  behavior with one instance.** Test: the existing single in-world terminal still renders, focuses,
  types, and mouse-maps exactly as before.

- **Phase 5 — In-world record + grid-driven sizing + retire TOML.** Replace `InWorldSettings` with
  in-memory `InWorldTerminalRecord`; switch to fixed cols×rows → derived texture; add the shared
  `ApplyGridSize` resize helper (used by both 2D and in-world). Dev gate creates one default
  instance. Test: the default in-world terminal comes up at the chosen cols×rows; no
  `purrtty-inworld.toml` is written.

- **Phase 6 — N instances + Manager dialog.** `InWorldManagerUI` (create/list/configure), each
  instance registered in the target registry; postfix draws N quads; picking chooses the nearest
  part-mode quad; per-instance focus. Test: create 2–3 in-world terminals (different parts +
  billboard, different shells incl. Game Console); click between part-anchored ones to focus; each
  runs its own shell.

- **Phase 7 — Shared quad resource (perf).** Hoist pipelines/layout/VB-IB to `SharedQuadResource`;
  `InWorldQuad` keeps only descriptor set + MVP. *Deferrable.* Test: N instances still render; lower
  per-instance VRAM/pipeline count.

- **Phase 8 — Theme + resize apply to in-world from the dialogs.** Theme dialog can select an
  in-world instance and live-edit it (font-size change → texture regen); Manager dialog's theme
  picker applies a catalog theme; cols/rows change resizes. Test: apply Monokai to a named in-world
  terminal; bump its font size → texture regenerates crisply; change its cols/rows live.

- **Phase 9 — Hardening + docs + final suite.** Lifecycle review (teardown order with N instances,
  no use-after-free in the postfix during a Close), name-collision edge cases, then the **Instruction
  Maintenance Mandate** docs (§8) + full `dotnet test`.

---

## 7. Risks & gotchas

- **`ImInputString` live-read** (filter boxes, name fields): call `EvaluateLength()` each frame or
  avoid `EnterReturnsTrue`, else the buffer reads empty while typing. Memory
  `[[iminputstring-enterreturns-true]]`. Affects `FilterCombo` and the required-name field.
- **Postfix use-after-free at N:** closing an instance must clear it from the coordinator's list
  **before** freeing its GPU handles, and `HasDrawableInstances` must be re-derived — the postfix
  runs on the same main thread, so order the mutation before teardown (the existing single-instance
  rule, `InWorldTerminalManager.cs:154-170`, generalized to "remove from list, then teardown").
- **N queue submits/frame:** each instance submits its offscreen pass on the shared graphics queue
  (`PerFrameRenderer.Frame`). Bound N in the Manager UI (sensible cap, e.g. 8) and surface the cost;
  don't silently allow dozens.
- **Font-size change on in-world = resize:** changing `FontSize` alters cell metrics → texture
  extent for fixed cols×rows → must regenerate `OffscreenRenderTarget` + the quad's descriptor set.
  Route font-size edits through `instance.Resize`, not just a settings poke.
- **Name uniqueness races:** creation validates against the registry at submit; the registry is the
  single source of truth (case-insensitive). 2D auto-names use `SuggestUniqueName`.
- **gatOS expectation:** the shell picker won't show a literal "gatOS" entry (none exists). Document
  that gatOS is reached via a PTY shell running SSH, and any registered custom shell appears
  automatically — so the picker is correct/future-proof without special-casing.
- **Don't regress the byte-identical 2D path:** Phases 2–3 touch `TerminalWindow`; keep the
  render/input bodies untouched, add only naming + the dialog seam.

---

## 8. Documentation to update (Instruction Maintenance Mandate)

- **`CLAUDE.md`** — note the named-terminal registry + per-target theming model; the in-world
  coordinator/instance split; that in-world is session-only (no `purrtty-inworld.toml`).
- **`docs/code-navigation.md`** — new files: `INamedTerminal`/`TerminalTargetRegistry`,
  `ImGuiWidgets.FilterCombo`, the Theme dialog, `InWorldTerminalCoordinator`/
  `InWorldTerminalInstance`/`InWorldTerminalRecord`/`InWorldManagerUI`, `SharedQuadResource`;
  retire `InWorldSettings`/`InWorldLaunchUI`/`QuadPicker` (folded into the coordinator).
- **`docs/gotchas.md`** — add: FilterCombo `EvaluateLength`; N-instance postfix teardown order;
  in-world font-size-as-resize; N queue-submit cost.
- **`docs/how-to.md`** — recipes: "create an in-world terminal", "apply a theme to a named
  terminal", "add a new shell to the picker" (it's automatic via the registry).
- This plan's status header → mark phases done as they land.

---

## 9. Open items / future

- Palette (16-color) editor in the Theme dialog (today: apply/save bundles only).
- Per-2D-window theme persistence across restarts (today: catalog + default seed).
- In-world persistence/auto-restore (explicitly deferred — decision #3 is session-only).
- Billboard click-to-focus, billboard forward-axis verification, richer billboard tuning
  (carried over from the in-world feature's deferred list).
```
