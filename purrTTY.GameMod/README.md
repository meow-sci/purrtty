# purrTTY.GameMod

The final mod assembly for purrTTY — a terminal emulator mod for Kitten Space
Agency. This project wires the terminal into the game: StarMap lifecycle hooks,
Harmony patches (input gating, menu fallback, console capture), the top-level
game menus, and the bundled themes/fonts.

Terminal emulation itself is delegated to **libghostty-vt** via the vendored
`Ghostty.Vt` binding; the mod runs on Windows, macOS, and Linux (ConPTY /
POSIX-pty shells respectively, plus the in-game Game Console shell everywhere).

See the repo root for the authoritative docs:

- [`README.md`](../README.md) — project overview, features, installation
- [`CLAUDE.md`](../CLAUDE.md) — architecture, build/test/deploy commands, and
  the key behaviors & gotchas (the onboarding doc)

## Build & deploy

```bash
dotnet build purrTTY.GameMod        # builds and deploys the purrTTY/ mod folder
```

The `CopyCustomContent` target wipes and rewrites the deployed `purrTTY/`
folder in the KSA mods directory (override with `PURRTTY_DIST_DIR`), bundling
the managed assemblies, the native libghostty-vt for all three supported
platforms, and the `TerminalThemes/` + `TerminalFonts/` assets.

Launch KSA and toggle the terminal with the configured hotkey (default **F12**).
