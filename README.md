# Overview

purrTTY is an in-game terminal emulator mod for Kitten Space Agency (KSA).

# (Likely) FAQ

## What does it do?

It runs real shell sessions inside an in-game terminal window — ConPTY shells on Windows,
POSIX-pty shells on Linux/macOS, plus a cross-platform in-game Game Console shell — and
renders them with the ImGui framework KSA provides. Toggle it with the configured hotkey
(default F12); multiple windows and tabs, themes, and fonts are configurable from the
in-game menu.

## Does it do anything for kFS, the in-game flight computer?

**New, and not yet verified in the game.** purrTTY now also extends kFS, the programmable
flight computer built into KSA:

- kFS programs get `x_` services to read and control a vehicle's lights (on/off, colour, cone,
  intensity, range) and its part animations (deployables such as solar arrays, and actuators).
- A `purr` console command lists which index is which part (`purr parts`), writes example
  programs into the kFS workspace (`purr examples`) and restores lights a program changed
  (`purr lights reset`); the purrTTY menu has a **kFS** submenu for the same.
- **Edit in Neovim** opens your own `nvim` in a purrTTY terminal on a kFS source file, with
  diagnostics, hover and completion from a language server running inside the game, and commands
  to build, load and start the program from the editor.
- A separate command-line tool, `purr-kfs` (built from this repository, not part of the mod zip),
  checks and runs kFS programs outside the game.

All of this has been tested only outside the game, against the game's own kFS code; none of it
has been run in the game yet, so expect rough edges. It can be switched off with
`enabled = false` (and the language server with `lsp_enabled = false`) in
`.purrTTY/kfs.toml` in the game's `Kitten Space Agency` documents folder. While it is on, the
language server listens on the local loopback interface only and writes a session token to
`.purrTTY/kfs-lsp.json` next to that file. Details: `docs/how-to.md`.

## What powers the terminal emulation?

purrTTY does not implement its own VT emulator. All terminal emulation is delegated to
[libghostty-vt](https://github.com/ghostty-org/ghostty) — the standalone, conformance-tested
VT engine from Ghostty — via a vendored .NET binding (`vendor/Ghostty.Vt/`). Prebuilt native
libraries for win-x64, linux-x64, and osx-arm64 are bundled, so one mod zip runs on all three
platforms. See `AGENTS.md` for the architecture (ImGui frontend ⟷ renderer-neutral seam ⟷
headless backend) and `THIRD-PARTY-NOTICES.md` for licensing.

## Installing over an old version?

Delete the old `purrTTY/` folder from your mods directory first — unzipping over an existing
install leaves files from the previous version behind.

# Releases

GitHub Actions publishes stable `1.<run number>.0` releases from `main` and
`1.<run number>.0-beta` prereleases from `feature/**` branches, using the workflow's
existing run counter. Major is fixed at `1` and patch at `0`. The version matches
the packaged `mod.toml`, release title, and `purrTTY-<version>.zip` filename; tags
add a `v` prefix. Reruns preserve an already-published release.

After publication, stable releases retain the newest 10, including the existing
`v1.0.0`, `v1.0.1`, and `v1.1.0` releases in that pool. All feature branches share
the newest five beta prereleases. Older releases and their tags are deleted.
`release/**`, `fix/**`, `chore/**`, and pull requests into `main` run tests and
build only. Manual runs follow the selected branch's policy; tags never publish.
See [CI / releases](docs/build-and-test.md#ci--releases-githubworkflowsreleaseyml)
for details.

# Fonts

Fonts come from https://www.nerdfonts.com/

# Themes

Themes come from https://github.com/mbadolato/iTerm2-Color-Schemes

purrTTY is using the alacritty TOML formatted theme files

