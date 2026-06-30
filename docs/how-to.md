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
- Create form: a unique **name**, a **shell** (`FilterCombo` over `ShellMenuCache.Current` +
  `CustomShellRegistry` — every registered shell appears automatically), fixed **cols×rows**, an
  **anchor mode** (radio): *Vehicle Part* (tiered Vehicle→Part→optional SubPart pickers, resolved by
  `VehicleLookup`) or *Camera Billboard*, and a **theme**. Create → `InWorldTerminalManager.Create(record)`
  builds the GPU graph + shell and registers the instance in the target registry.
- Instances are **session-only** (never persisted). They are listed with per-instance focus /
  configure / red **Destroy**; configuring lets you live-edit theme + placement and recreate-resize the
  grid. Set `PURRTTY_INWORLD=1` to auto-create one default instance on load (dev convenience).
- Lifecycle rules when extending this: respect the deferred GPU teardown (gotcha 25), the
  no-device-touch-at-shutdown rule (gotcha 26), and the per-instance cost (gotcha 27).

## Making an in-world terminal see-through
- The quad honors the theme's **three opacities** so the 3D world shows through, like a 2D window.
  Lower any of **Background** / **Foreground** / **Cell background** opacity below 100%:
  - **Live**, per instance: the **Configure** panel in "In-World Terminals…" has an **Opacity**
    section (three % sliders). Edits apply instantly and are **session-only** (not persisted).
  - **Via a theme**: apply (or save) a theme whose `[window]` table sets `background_opacity` etc.;
    applying it to the in-world target carries the values in. `BackgroundOpacity` is the dominant
    "how see-through is the whole panel" knob.
- It works by reusing the existing per-cell opacity render (no second path): the off-screen target
  clears transparent, the background rect carries `BackgroundOpacity`, and the quad's custom
  premultiplied-alpha frag composites the result over the scene. To change/extend the compositing,
  read **gotcha 28** first — straight-alpha or skipping the un-premultiply both regress it.

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
