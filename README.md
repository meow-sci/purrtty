# Overview

purrTTY is an in-game terminal emulator mod for Kitten Space Agency (KSA).

# (Likely) FAQ

## What does it do?

It runs real shell sessions inside an in-game terminal window — ConPTY shells on Windows,
POSIX-pty shells on Linux/macOS, plus a cross-platform in-game Game Console shell — and
renders them with the ImGui framework KSA provides. Toggle it with the configured hotkey
(default F12); multiple windows and tabs, themes, and fonts are configurable from the
in-game menu.

## What powers the terminal emulation?

purrTTY does not implement its own VT emulator. All terminal emulation is delegated to
[libghostty-vt](https://github.com/ghostty-org/ghostty) — the standalone, conformance-tested
VT engine from Ghostty — via a vendored .NET binding (`vendor/Ghostty.Vt/`). Prebuilt native
libraries for win-x64, linux-x64, and osx-arm64 are bundled, so one mod zip runs on all three
platforms. See `CLAUDE.md` for the architecture (ImGui frontend ⟷ renderer-neutral seam ⟷
headless backend) and `THIRD-PARTY-NOTICES.md` for licensing.

## Installing over an old version?

Delete the old `purrTTY/` folder from your mods directory first — unzipping over an existing
install leaves files from the previous version behind.

# Fonts

Fonts come from https://www.nerdfonts.com/

# Themes

Themes come from https://github.com/mbadolato/iTerm2-Color-Schemes

purrTTY is using the alacritty TOML formatted theme files
