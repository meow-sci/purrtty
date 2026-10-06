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

# Standalone ImGui development

[`ImGuiPlayground/`](ImGuiPlayground/README.md) is an optional all-C# .NET 10
hello-world app using KSA's actual BRUTAL ImGui API and default JetBrains Mono font.
A managed GLFW/OpenGL host targets macOS, Linux, and Windows without launching the game
(macOS arm64 validated so far). No custom C++ bridge or native build toolchain.
After the one-time native-library setup:

```bash
dotnet run --project ImGuiPlayground
dotnet run --project ImGuiPlayground -- --capture .tmp/hello.png
```

Edit `ImGuiPlayground/Program.cs` to iterate on UI. It is independent of the terminal
mod and is the starting point for a reusable mod-UI test library, not yet a packaged library.

# Fonts

Fonts come from https://www.nerdfonts.com/

# Themes

Themes come from https://github.com/mbadolato/iTerm2-Color-Schemes

purrTTY is using the alacritty TOML formatted theme files
