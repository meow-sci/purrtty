# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

purrTTY is a terminal emulator mod for the Kitten Space Agency (KSA) game engine.

As of the libghostty-vt migration, purrTTY **no longer ships its own VT emulator**. It delegates
all terminal emulation to **libghostty-vt** — the standalone, conformance-tested VT engine from
[Ghostty](https://github.com/ghostty-org/ghostty) — via a **vendored, owned** C# binding
(`Ghostty.Vt`). purrtty owns a clean three-layer architecture on top of it.

What this project/mod does:
- Runs real shell sessions (ConPTY shells on Windows; an in-game `GameConsoleShell` cross-platform) inside an in-game terminal window.
- Feeds shell output to libghostty-vt and renders the resulting grid through ImGui in KSA.
- Encodes keyboard/mouse/paste input through libghostty-vt's encoders and writes it back to the shell.

## Architecture — three clean layers

```
FRONTEND   purrTTY.Display (ImGui)          — GhosttyTerminalController + FrameGridRenderer
   │  consumes TerminalFrame (OUT) / drives ITerminalSurface (IN) — NO engine types cross this seam
BACKEND    purrTTY.Terminal (headless, renderer-neutral)
   │  ITerminalSurface + TerminalFrame + GhosttyTerminalSurface + OSC sidecar + sessions
BINDING    vendor/Ghostty.Vt (vendored, owned, net10)  — Terminal/RenderState/encoders + purrtty extensions
NATIVE     vendor/Ghostty.Vt/native/libghostty-vt.dylib (built from pinned ghostty; macOS/osx-arm64 for now)
```

The **renderer-neutral seam** is the heart of the design: the backend produces a `TerminalFrame`
snapshot (rows of pre-resolved cells, cursor, scrollbar, colors) and accepts commands/events via
`ITerminalSurface`. **No ImGui/Vulkan/KSA types cross this boundary**, so a future Vulkan/hybrid
frontend is a frontend swap, not a backend rewrite.

### Project dependencies

```
purrTTY.Logging
vendor/Ghostty.Vt              # vendored libghostty-vt binding (+ native lib) — MIT, see its README
purrTTY.Core                  # PTY/process host (ConPTY), custom-shell bridge, shell config — NO emulator
    ↑ (CustomShellContract)
purrTTY.CustomShellContract / purrTTY.CustomShells
purrTTY.Terminal              # BACKEND: refs vendor/Ghostty.Vt + purrTTY.Core (PTY) + Logging
    ↑
    └── purrTTY.Terminal.Tests (NUnit integration tests)
purrTTY.Display               # ImGui FRONTEND: refs purrTTY.Terminal + purrTTY.Core + KSA ImGui DLLs
purrTTY.GameMod               # final mod DLL: refs Display + CustomShells; StarMap hooks + Harmony patches
```

> Transitional note: `purrTTY.Terminal` references `purrTTY.Core` to reuse the emulator-independent
> PTY/process layer in place. A future cleanup may relocate that layer into `purrTTY.Terminal` and
> drop the reference (and possibly fold `purrTTY.Core` away entirely).

## Build and Test Commands

```bash
dotnet build purrtty.slnx              # Build the whole solution
dotnet build purrTTY.Terminal          # Backend only
dotnet build purrTTY.GameMod           # Game mod (also deploys to the KSA mods dir, incl. native lib)
```

### Testing

The terminal-emulation behavior is **trusted to libghostty-vt** and is not re-tested. The tests
cover purrtty's **integration** with the engine.

```bash
dotnet test purrTTY.Terminal.Tests/purrTTY.Terminal.Tests.csproj --nologo -v quiet
```

`purrTTY.Terminal.Tests` (NUnit) validates frame production, theming, selection, OSC sidecar,
key/mouse encoding, bracketed paste, DSR replies, scrollback, and the session-wiring data flow.
Keep test output minimal so it does not flood the CLI.

> The legacy emulator test/app projects (`purrTTY.Core.Tests`, `purrTTY.Display.Tests`,
> `purrTTY.Display.Playground`, `purrTTY.TestApp`) were deleted with the emulator.

### Building the native libghostty-vt

```bash
export PATH="/opt/homebrew/opt/zig@0.15/bin:$PATH"   # ghostty needs zig 0.15
cd /path/to/ghostty && zig build -Demit-lib-vt       # → zig-out/lib/libghostty-vt.dylib
# copy into vendor/Ghostty.Vt/native/
```

The native lib is **pinned, not forked** (current pin recorded in `vendor/Ghostty.Vt/README.md`).
Only osx-arm64 is vendored today; multi-RID is follow-up work.

## Code Navigation Guide

Start here:
- Solution: `purrtty.slnx`
- Shared build config + KSA DLL paths (per-OS): `Directory.Build.props`
- Migration plan + status: `LIBGHOSTTY_ANALYSIS.md`; provenance/licensing: `vendor/Ghostty.Vt/README.md`, `THIRD-PARTY-NOTICES.md`

Vendored binding (`vendor/Ghostty.Vt/`):
- Engine surface: `src/Terminal.cs`, `src/RenderState.cs`, `src/TerminalOptions.cs`, encoders (`src/KeyEncoder.cs`, `src/MouseEncoder.cs`)
- Native P/Invoke: `src/Native/NativeMethods.cs` (+ `NativeMethods.Selection.cs`)
- Native loader (KSA ALC): `src/Native/NativeLibraryResolver.cs` (ModuleInitializer + `SetDllImportResolver`)
- purrtty additions: `src/Terminal.Selection.cs` (selection + default cursor style/blink), `MaxScrollback` in `TerminalOptions.cs`, per-row `RowSelection` in `RenderState.cs`

Backend (`purrTTY.Terminal/`):
- Seam contract: `ITerminalSurface.cs`; frame value types: `Rendering/` (`TerminalFrame`, `FrameRow`, `FrameCell`, `RgbaColor`, `CellFlags`/`UnderlineStyle`/`CellWidth`/`CursorShape`)
- Engine wrapper: `Ghostty/GhosttyTerminalSurface.cs` (single-threads native; theme push; key/mouse encode)
- OSC 52 clipboard / OSC 1 icon: `Ghostty/OscSidecar.cs` (managed tee of the output stream)
- Neutral input types: `Input/` (`TerminalKey`, `TerminalKeyEvent`, `TerminalMouseEvent`, `GridPoint`, `KeyModifiers`)
- Sessions: `Sessions/` (`TerminalSession`, `SessionManager`, `TerminalSessionFactory` — the construction seam)

Frontend (`purrTTY.Display/`):
- Controller: `Ghostty/GhosttyTerminalController.cs` (implements `Controllers/ITerminalController.cs`)
- Grid drawing: `Ghostty/FrameGridRenderer.cs`; default palette: `Ghostty/DefaultTheme.cs`
- New session factory: `Ghostty/GhosttySessionManagerFactory.cs` (persisted `ThemeConfiguration`)
- Reused chrome: `Rendering/PurrTTYFontManager.cs`, `Configuration/` (fonts, theme config, shell config)

PTY/process (`purrTTY.Core/Terminal/`):
- ConPTY host: `Process/*`, `ProcessManager.cs`; interface: `IProcessManager.cs`
- Custom-shell adapter: `CustomShellPtyBridge.cs`; launch options: `ProcessLaunchOptions.cs`
- Session/process event args + `SessionState`: `SessionEventArgs.cs`, `ProcessEventArgs.cs`

Game/integration (`purrTTY.GameMod/`):
- Lifecycle + window toggle: `TerminalMod.cs` (constructs `GhosttySessionManagerFactory` + `GhosttyTerminalController`)
- Harmony patches: `Patcher.cs` (gates `KSA.Program.OnKey` via `GhosttyTerminalController.IsAnyTerminalActive`), `Patches/ConsoleWindowPrintPatch.cs`

Custom shells:
- Contract: `purrTTY.CustomShellContract/` (`ICustomShell`, `CustomShellRegistry`, `BaseLineBufferedShell`)
- Built-in game shell: `purrTTY.CustomShells/GameConsoleShell.cs` (emits VT bytes → `Surface.Write`)

## Key behaviors & gotchas

1. **Single-threaded native access.** `ITerminalSurface.Write` is the only thread-safe entrypoint
   (it enqueues PTY bytes). The engine is mutated only on the frontend tick inside `BuildFrame()`.
   All other surface members must be called on the tick thread.

2. **Data flow per session** (`Sessions/TerminalSession`):
   PTY output → `ProcessManager.DataReceived` → `Surface.Write`; engine replies → `Surface.PtyReply`
   → `ProcessManager.Write`; user input → frontend `Surface.EncodeKey`/`EncodeMouse` → `session.SendInput`.

3. **Key encoder quirk.** libghostty's key encoder can emit a spurious lone `NUL` on its first use;
   `GhosttyTerminalSurface.EncodeKey` self-heals (re-encode once when a non-Ctrl key yields `[0x00]`).
   Named keys encode from `Key` alone; `Text` is only for printable input.

4. **Pre-resolved colors.** Push the theme via `Surface.SetTheme` so `TerminalFrame` cells carry
   final RGB; the frontend draws them directly (no SGR resolution in the frontend).

5. **Custom shell discovery** is reflection-based; `GhosttySessionManagerFactory` forces the
   `purrTTY.CustomShells` assembly to load before discovery (do not remove without replacing it).

## Common Workflows

### Changing terminal/rendering behavior
- Frame production / cell mapping: `purrTTY.Terminal/Ghostty/GhosttyTerminalSurface.cs`
- Drawing: `purrTTY.Display/Ghostty/FrameGridRenderer.cs`
- Add a backend integration test in `purrTTY.Terminal.Tests` and run it.

### Extending the engine binding
1. Add the native P/Invoke in `vendor/Ghostty.Vt/src/Native/` (verify the symbol exists in the dylib with `nm`).
2. Wrap it in the relevant `Ghostty.Vt` class (mark purrtty additions clearly).
3. Surface it through `ITerminalSurface`/`TerminalFrame` if the frontend needs it.

### Adding a custom shell
1. Implement `ICustomShell` (or inherit `BaseLineBufferedShell`) in `purrTTY.CustomShells`.
2. Ensure metadata + a stable shell ID; verify discovery via `CustomShellRegistry`.

### Deploying the game mod
1. `dotnet build purrTTY.GameMod` — copies the mod DLLs **and the native libghostty-vt** to the mods dir.
2. Launch KSA; toggle the terminal with the configured hotkey (default F12).

## Code Standards (from Directory.Build.props)
- .NET 10 / C# 13; nullable enabled; warnings-as-errors (except CS1591).
- The vendored `Ghostty.Vt` and the backend value types relax CS1591/warnings-as-errors in their csproj.

## Instruction Maintenance Mandate (MUST)

Whenever you make meaningful repository changes, you MUST evaluate and update this file in the same
work item if it affects: project structure/dependencies, the backend/frontend seam, the engine
binding surface, build/test/deploy commands, or feature status. Remove defunct guidance immediately;
prefer verified code paths over plans when documenting behavior; keep navigation pointers current.
Do not document the deleted bespoke emulator as if it still exists.
