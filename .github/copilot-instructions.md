# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

purrTTY is a C# VT100/xterm terminal emulator for the Kitten Space Agency (KSA) game engine.

What this project/mod does:
- Runs real shell sessions (PowerShell/cmd/other configured shells) inside an in-game terminal window.
- Emulates terminal behavior in a headless core (parsing, cursor state, buffers, modes, OSC/CSI handling).
- Renders the terminal through ImGui in KSA and exposes game-integrated shell options (including custom game shell support).

How it does it:
- `purrTTY.Core` implements parsing + emulation + session/process orchestration.
- `purrTTY.Display` bridges emulator state to ImGui rendering and input handling.
- `purrTTY.GameMod` hosts lifecycle hooks (`StarMap*` attributes), creates the session manager/controller, and toggles UI visibility.
- `purrTTY.CustomShellContract` + `purrTTY.CustomShells` provide extensible custom shell integrations (for example, KSA game console shell).

## Build and Test Commands

### Building
```bash
dotnet build                    # Build entire solution
dotnet build purrTTY.Core         # Build core library only
dotnet build purrTTY.GameMod      # Build game mod (outputs to purrTTY.GameMod/dist/)
```

### Testing

Use the PowerShell test script by default. It keeps test output manageable and parses failures into a compact summary.

If the script is unavailable or fails before the test run starts, fall back to `dotnet test` only for a specific project or filtered subset so output stays narrow.

```bash
.\scripts\dotnet-test.ps1                            # Preferred: run all tests
.\scripts\dotnet-test.ps1 -Filter "Category=Unit"    # Preferred: run filtered tests
dotnet test purrTTY.Core.Tests --filter "Category=Unit"  # Fallback if the script cannot be used
```

The script saves results to `.testresults/results.trx` and parses failures with `test-errors.ts`.

Important:
- ALWAYS prefer `./scripts/dotnet-test.ps1` over raw `dotnet test` for regular development runs.
- ALWAYS use bun to run `.ts` scripts in this repo.

### Running
```bash
dotnet run --project purrTTY.TestApp                   # Run standalone console app
```

## Architecture

### Project Dependencies
```
purrTTY.Core (headless, no game dependencies)
    ↑
    ├── purrTTY.Core.Tests (unit & property tests)
    ├── purrTTY.TestApp (console app)
    ├── purrTTY.CustomShellContract (custom shell abstractions)
    │       ↑
    │       └── purrTTY.CustomShellContract.Tests
    ├── purrTTY.CustomShells (custom shell implementations)
    │       ↑
    │       └── purrTTY.CustomShells.Tests
    ├── purrTTY.Logging (shared logging helpers)
    └── purrTTY.Display (ImGui integration, depends on KSA DLLs)
            ↑
            ├── purrTTY.Display.Playground
            ├── purrTTY.Display.Tests
            └── purrTTY.GameMod (final game mod DLL, includes mod.toml)
```

### Core Architecture (purrTTY.Core)

The terminal emulator follows a multi-stage parsing and execution pipeline.

**Parsing Pipeline:**
- Input bytes -> `Parser` -> escape sequence parsers (`CsiParser`, `EscParser`, `OscParser`, `DcsParser`)
- Parsers emit typed messages: `CsiMessage`, `EscMessage`, `OscMessage`, `DcsMessage`
- Messages routed through terminal parser handlers, then to emulator operations.

**Key Components:**
- `TerminalEmulator`: Main orchestrator, still large but now delegates heavily to operation classes
- `TerminalEmulatorBuilder`: Constructs emulator dependencies and operation handlers
- `ProcessManager`: Manages Windows ConPTY shell processes (PowerShell, cmd)
- `CustomShellPtyBridge`: Adapter for custom shell implementations via `ICustomShell` interface
- `SessionManager`: Multi-session lifecycle, switching, restart, and tab ordering
- `DualScreenBuffer`: Manages primary and alternate screen buffers
- `ScreenBuffer`: Grid of cells with cursor, scrollback, and scroll regions

**Important Subsystems:**
- `Input/`: Keyboard encoding, bracketed paste mode
- `Parsing/`: Escape sequence parsers (CSI, OSC, ESC, DCS)
- `Terminal/`: Core terminal logic, process management, custom shell support
- `Tracing/`: SQLite-based logging for debugging (disabled by default, see purrTTY.Core/Tracing/README.md)
- `Types/`: Data structures (Cell, Cursor, ScreenBuffer, messages)
- `Utils/`: Character classification, UTF-8 handling

### TerminalEmulator Refactor Status

The major refactor that split emulator behavior into operation classes is complete.

Navigation: `purrTTY.Core/Terminal/EmulatorOps/`
- Cursor and positioning: `TerminalCursorMovementOps.cs`, `TerminalCursorSaveRestoreOps.cs`, `TerminalCursorStyleOps.cs`
- Erase operations: `TerminalEraseInDisplayOps.cs`, `TerminalEraseInLineOps.cs`, selective erase variants
- Scroll/insert/delete: `TerminalScrollOps.cs`, `TerminalInsertLinesOps.cs`, `TerminalDeleteLinesOps.cs`
- OSC handling ops: `TerminalOscTitleIconOps.cs`, `TerminalOscClipboardOps.cs`, `TerminalOscHyperlinkOps.cs`, `TerminalOscColorQueryOps.cs`
- Mode and input behavior: `TerminalDecModeOps.cs`, `TerminalPrivateModesOps.cs`, `TerminalBracketedPasteOps.cs`, `TerminalInputOps.cs`

### Display Layer (purrTTY.Display)

`TerminalController` has been decomposed into subsystem classes in `purrTTY.Display/Controllers/TerminalUi/`.

Key files:
- `TerminalController.cs` (facade)
- `TerminalControllerBuilder.cs` (wires dependencies)
- `TerminalUiRender.cs` (grid rendering)
- `TerminalUiInput.cs` (input routing)
- `TerminalUiTabs.cs` (session tab UI)
- `TerminalUiSelection.cs` (selection/copy behavior)
- `TerminalUiMouseTracking.cs` (mouse protocol behavior)
- `TerminalUiResize.cs` (resize and dimensions)
- `TerminalUiSettingsPanel.cs` (settings and persistence)
- `TerminalUiFonts.cs` (font loading/switching)

### Custom Shell Architecture

Custom shell contract and implementation points:
- `purrTTY.CustomShellContract/ICustomShell.cs`
- `purrTTY.CustomShellContract/Base/BaseLineBufferedShell.cs`
- `purrTTY.Core/Terminal/CustomShellPtyBridge.cs`
- `purrTTY.CustomShells/GameConsoleShell.cs`

Important behavior:
- Shell discovery is reflection-based via `CustomShellRegistry`.
- `SessionManagerFactory` intentionally forces `purrTTY.CustomShells` assembly load before discovery.
- `GameConsoleShell` integrates with KSA console output using Harmony patching (`purrTTY.GameMod/Patches/ConsoleWindowPrintPatch.cs`).

### Game Mod Lifecycle (purrTTY.GameMod)

Primary file: `purrTTY.GameMod/TerminalMod.cs`

Lifecycle hooks:
- `[StarMapAllModsLoaded]`: patch + initialize terminal and controller
- `[StarMapAfterGui]`: update/render terminal UI each frame
- `[StarMapUnload]`: unpatch and dispose resources

User interaction:
- Terminal visibility toggle hotkey defaults to F12 and can be changed from the top-level purrTTY game menu via the Toggle Hotkey modal
- Mod menu integration calls the same toggle path

## Code Navigation Guide

Use this map to quickly find the right place to make changes.

Start here:
- Solution entry: `purrtty.slnx`
- Shared build config and game DLL path: `Directory.Build.props`
- Feature inventory and progress notes: `docs/FEATURE_TRACKING.md`

Core emulator and parsing:
- Parser state machine: `purrTTY.Core/Parsing/Parser.cs`
- OSC private-use parsing marker: `purrTTY.Core/Parsing/OscParser.cs`
- Parser dispatch handlers: `purrTTY.Core/Terminal/TerminalParserHandlers.cs`
- Main emulator facade: `purrTTY.Core/Terminal/TerminalEmulator.cs`
- Emulator operation classes: `purrTTY.Core/Terminal/EmulatorOps/`
- Session lifecycle: `purrTTY.Core/Terminal/SessionManager.cs`
- Session creation and process/custom-shell branching: `purrTTY.Core/Terminal/Sessions/TerminalSessionFactory.cs`
- ConPTY process host: `purrTTY.Core/Terminal/ProcessManager.cs`

Display and UI:
- Controller facade: `purrTTY.Display/Controllers/TerminalController.cs`
- Controller builder: `purrTTY.Display/Controllers/TerminalControllerBuilder.cs`
- UI subsystems: `purrTTY.Display/Controllers/TerminalUi/`
- Session manager factory and persisted shell config: `purrTTY.Display/Configuration/SessionManagerFactory.cs`

Game and integration:
- Mod entry/lifecycle: `purrTTY.GameMod/TerminalMod.cs`
- Harmony patch bridge for game console output: `purrTTY.GameMod/Patches/ConsoleWindowPrintPatch.cs`
- Test app entry point: `purrTTY.TestApp/TerminalTestApp.cs`

Custom shells:
- Shell interface/metadata: `purrTTY.CustomShellContract/`
- Built-in game shell: `purrTTY.CustomShells/GameConsoleShell.cs`

## Development Patterns

### Testing Strategy
- **Unit tests**: Per-component tests in matching test projects
- **Property tests**: FsCheck-based in `Property/` folders
- **Test organization**: Mirrors source structure (e.g., `purrTTY.Core.Tests/Unit/ProcessManagerTests.cs` → `purrTTY.Core/Terminal/ProcessManager.cs`)
- **Test execution**: MUST use `.\scripts\dotnet-test.ps1` to avoid massive stdout bloat

### Complex Areas and Callouts

1. `TerminalEmulator` + `EmulatorOps` split:
- Behavior often spans parser handler -> emulator facade -> op class.
- When debugging sequence behavior, trace all three layers before changing logic.

2. Parser behavior and OSC private ranges:
- OSC `>= 1000` is currently parsed as private-use (`osc.private`), but there is no in-tree RPC project or injected handler pipeline.
- Treat OSC private-use handling as implementation-defined unless and until a concrete in-tree integration is added.

3. Display controller decomposition:
- Most rendering/input behavior now lives in `TerminalUi/*` subsystems.
- Avoid re-monolithizing `TerminalController.cs`; add behavior in subsystem files and wire in builder.

4. Session and shell configuration persistence:
- `SessionManagerFactory` pulls persisted settings and dimensions from theme configuration.
- Bugs around startup shell selection typically involve theme config loading + custom shell discovery order.

5. Custom shell discovery:
- Reflection-based discovery depends on assembly load state.
- `SessionManagerFactory.EnsureCustomShellsAssemblyIsLoaded()` is intentional; do not remove without replacing discovery strategy.

### Code Standards (from Directory.Build.props)
- .NET 10 with C# 13
- Nullable reference types enabled
- Warnings treated as errors (except CS1591 for missing XML docs)
- All projects generate XML documentation

### Tracing
- SQLite-based tracing in `purrTTY.Core/Tracing/`
- Disabled by default for performance
- Enable with `TerminalTracer.Enabled = true`
- Database location: `%TEMP%\purrTTY_trace.db` (Windows)

### Tools
- ALWAYS use bun to run .ts scripts

## Common Workflows

### Adding a new escape sequence handler
1. Identify sequence family (CSI, OSC, ESC, DCS).
2. Update parser logic in `purrTTY.Core/Parsing/` (for example `CsiParser.cs`, `OscParser.cs`).
3. Update/extend dispatch in `purrTTY.Core/Terminal/ParserHandlers/`.
4. Implement behavior in the appropriate class under `purrTTY.Core/Terminal/EmulatorOps/`.
5. Add targeted unit/property tests in `purrTTY.Core.Tests/`.
6. Run tests with `.\scripts\dotnet-test.ps1`.

### Adding a custom shell implementation
1. Implement `ICustomShell` in `purrTTY.CustomShellContract` (or inherit `BaseLineBufferedShell`).
2. Add implementation in `purrTTY.CustomShells` (or another shell assembly).
3. Ensure metadata is correct and shell ID is stable.
4. Verify discovery through `CustomShellRegistry` and session launch via `SessionManagerFactory`.
5. Add unit tests in the corresponding `*.Tests` project.
6. Run tests with `.\scripts\dotnet-test.ps1`.

### Testing terminal behavior
1. Use `purrTTY.TestApp` for quick console testing
2. Add unit tests in `purrTTY.Core.Tests/`
3. For display issues, test with `purrTTY.Display.Playground` or full game mod
4. Always run tests via `.\scripts\dotnet-test.ps1`

### Deploying game mod
1. Build: `dotnet build purrTTY.GameMod`
2. Copy `purrTTY.GameMod/dist/*` to KSA mods folder
3. Launch KSA, press F12 to toggle terminal

## Defunct Guidance Removal and Reality Check

The following are NOT present in the current repository and must not be documented as implemented:
- `purrTTY.TermSequenceRpc` project
- `RpcBootstrapper`, `IRpcHandler`, `IOscRpcHandler`, `KsaOscRpcHandler`, `KsaGameActionRegistry`
- `TerminalEmulator.Create(...)` overloads that accept RPC handler arguments

If you see comments or old notes mentioning those APIs, treat them as stale historical context.
Only document functionality that exists in the current codebase.

## Instruction Maintenance Mandate (MUST)

Whenever you make meaningful repository changes, you MUST evaluate and update this file in the same work item if needed.

You MUST update this file when any of the following change:
1. Project structure, project dependencies, or renamed/moved key files.
2. Core architecture or control flow (parser, emulator ops, session lifecycle, controller layering).
3. Public APIs, constructors, or integration patterns that agents are likely to copy.
4. Build/test/run commands, required tooling, or deployment steps.
5. Feature status transitions (implemented, removed, replaced, or deferred).

Rules:
- Remove defunct guidance immediately; do not leave stale references.
- Prefer verified code paths over plans/comments when documenting behavior.
- Keep navigation pointers current so future coding agents can find the right files quickly.
- If uncertain whether behavior is implemented, verify in code before documenting.

## Important Files

- `Directory.Build.props`: Shared build configuration, KSA installation path
- `.editorconfig`: C# formatting rules
- `docs/FEATURE_TRACKING.md`: VT100/xterm feature implementation status
- `purrTTY.Core/Tracing/README.md`: Tracing system documentation
- `purrTTY.GameMod/README.md`: Game mod installation and usage guide
- `scripts/dotnet-test.ps1`: Test runner that suppresses verbose output and auto-parses results (MUST USE THIS)
- `scripts/test-errors.ts`: TRX parser (automatically invoked by dotnet-test.ps1)
