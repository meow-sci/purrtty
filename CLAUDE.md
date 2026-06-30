# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

purrTTY is a terminal emulator mod for the Kitten Space Agency (KSA) game engine.

As of the libghostty-vt migration, purrTTY **no longer ships its own VT emulator**. It delegates
all terminal emulation to **libghostty-vt** — the standalone, conformance-tested VT engine from
[Ghostty](https://github.com/ghostty-org/ghostty) — via a **vendored, owned** C# binding
(`Ghostty.Vt`). purrtty owns a clean three-layer architecture on top of it.

What this project/mod does:
- Runs real shell sessions (ConPTY shells on Windows; POSIX-pty shells on Linux/macOS; an in-game `GameConsoleShell` cross-platform) inside an in-game terminal window.
- Feeds shell output to libghostty-vt and renders the resulting grid through ImGui in KSA.
- Encodes keyboard/mouse/paste input through libghostty-vt's encoders and writes it back to the shell.

## Architecture — three clean layers

```
FRONTEND   purrTTY.Display (ImGui)          — GhosttyTerminalController + FrameGridRenderer
   │  consumes TerminalFrame (OUT) / drives ITerminalSurface (IN) — NO engine types cross this seam
BACKEND    purrTTY.Terminal (headless, renderer-neutral)
   │  ITerminalSurface + TerminalFrame + GhosttyTerminalSurface + OSC sidecar + sessions
BINDING    vendor/Ghostty.Vt (vendored, owned, net10)  — Terminal/RenderState/encoders + purrtty extensions
NATIVE     vendor/Ghostty.Vt/native/<rid>/ — prebuilt shared libs from pinned ghostty, checked in (osx-arm64, win-x64, linux-x64)
```

The **renderer-neutral seam** is the heart of the design: the backend produces a `TerminalFrame`
snapshot (rows of pre-resolved cells, cursor, scrollbar, colors) and accepts commands/events via
`ITerminalSurface`. **No ImGui/Vulkan/KSA types cross this boundary**, so a future Vulkan/hybrid
frontend is a frontend swap, not a backend rewrite.

### Project dependencies

```
purrTTY.Logging
vendor/Ghostty.Vt              # vendored libghostty-vt binding (+ native lib) — MIT, see its README
purrTTY.CustomShellContract    # ICustomShell, CustomShellRegistry, WellKnownShellEnvironment (namespace purrTTY.Core.Terminal)
purrTTY.CustomShells           # GameConsoleShell — refs CustomShellContract + KSA DLLs ONLY (deliberately
    │                          #   no purrTTY.Display: shell config arrives via launch-option env vars)
purrTTY.Terminal               # BACKEND: surface + sessions + RELOCATED PTY layer (Pty/)
    │  refs vendor/Ghostty.Vt + purrTTY.CustomShellContract + purrTTY.Logging
    └── purrTTY.Terminal.Tests (NUnit integration tests)
purrTTY.Display                # ImGui FRONTEND: refs purrTTY.Terminal + KSA ImGui DLLs
    └── purrTTY.Display.Tests  (pure-logic theming/config tests — no ImGui)
purrTTY.GameMod                # final mod DLL: refs Display + CustomShells; StarMap hooks + Harmony patches
```

> The PTY/process layer was relocated out of the retired `purrTTY.Core` project into
> `purrTTY.Terminal/Pty/` (its types keep the `purrTTY.Core.Terminal` namespace, which is shared
> with `CustomShellContract`). There is no longer a `purrTTY.Core` project.

### Named terminals, per-target theming, and N in-world terminals

Every terminal — a 2D `TerminalWindow` **or** an in-world render-to-texture instance — is an
`INamedTerminal` (`purrTTY.Display/Ghostty/TerminalTargetRegistry.cs`) with a unique, case-insensitive
name in one process-wide `TerminalTargetRegistry`. Names are how the UI addresses terminals:

- **Theme dialog** (`purrTTY.GameMod/UI/ThemeDialog.cs`, menu **"Theme…"**) — a filtering target
  picker (defaulting to the focused terminal) selects which terminal each appearance edit applies to.
  A theme is a **complete bundle** (`ThemeDefinition`): palette + font family/size + the three
  opacities + cursor/border/lock/hot-zone. This replaced the old scattered theme/font/opacity menus.
- **In-world manager** (`purrTTY.GameMod/InWorld/UI/InWorldManagerUI.cs`, menu **"In-World
  Terminals…"**) — creates/lists/configures **N** independent in-world terminals, each with a name,
  shell, fixed cols×rows, anchor (vehicle Part/SubPart or camera billboard), and theme.
- **Layouts manager** (`purrTTY.GameMod/Layouts/UI/LayoutManagerUI.cs`, menu **"Layouts…"**) —
  saves/loads/edits/tears-down named **sets** of terminals (2D **and** in-world) as TOML files in
  `<config>/.purrTTY/layouts/`. Apply is **always user-initiated** (no auto-apply on game start); a
  terminal whose name collides with a live one is logged and skipped. Each terminal may carry a
  **startup command** (`ProcessLaunchOptions.StartupCommand`) typed into its shell on start — e.g. a
  gatOS flight-computer TUI. The data model + catalog live in `purrTTY.Display/Layouts/`; the
  orchestrator (`LayoutManager`) + dialog live in `purrTTY.GameMod/Layouts/`.

The in-world subsystem is a **thin coordinator + per-instance objects**: `InWorldTerminalManager`
(coordinator; owns `List<InWorldTerminalInstance>`, focus arbitration, the deferred-teardown queue,
and the render-postfix statics `Active`/`Instance`/`IsInputFocused`) over `InWorldTerminalInstance`
(one off-screen GPU graph + dedicated shell + world-space quad each). The identical GPU state
(pipelines/layout/geometry) is hoisted into one shared `SharedQuadResource`. In-world terminals are
**session-only** — created fresh via the dialog, never persisted as singletons (no `purrtty-inworld.toml`)
— though a **set** of terminals (2D and in-world) can be saved as a named **layout** and re-created
later via the Layouts manager (above). See the in-world lifecycle gotchas in
[docs/gotchas.md](docs/gotchas.md) (deferred GPU teardown mid-session vs. no-device-touch at shutdown).

In-world quads honor the theme's **three opacities** (faithful per-pixel transparency over the 3D
scene, like a 2D window): the off-screen target clears transparent, the background rect carries
`BackgroundOpacity`, and the quad composites premultiplied-alpha through purrtty's **only custom
shader** (`SharedQuadResource.QuadFragGlsl`, runtime-compiled via `RenderCore.ShaderModuleUtils`).
See gotcha 28 before touching the compositing.

## Build and Test Commands

```bash
dotnet build purrtty.slnx              # Build the whole solution
dotnet build purrTTY.Terminal          # Backend only
dotnet build purrTTY.GameMod           # Game mod (also deploys to the KSA mods dir, incl. native libs)
```

The mod output is **platform-agnostic**: every build bundles the prebuilt native libs for all
three RIDs (osx-arm64, win-x64, linux-x64), so one build from any host OS produces a single mod
dist that runs on Windows, macOS, and Linux.

```bash
dotnet test purrtty.slnx --nologo -v quiet                                  # full suite (4 test projects)
dotnet test purrTTY.Terminal.Tests/purrTTY.Terminal.Tests.csproj --nologo -v quiet   # engine integration only
```

Tests must be **quiet** (zero output on pass/skip) and must **never use fixed sleeps** — see
[docs/build-and-test.md](docs/build-and-test.md) for the full rules.

## Documentation

| File | Contents |
|------|----------|
| [docs/build-and-test.md](docs/build-and-test.md) | KSA paths, CI/release pipeline, test standards (quiet + no fixed sleeps), building native libghostty-vt |
| [docs/code-navigation.md](docs/code-navigation.md) | File-by-file navigation for all layers: binding, backend, frontend, PTY, game mod, custom shells, named-terminal registry, in-world subsystem, layouts |
| [docs/gotchas.md](docs/gotchas.md) | 31 key behaviors and gotchas (threading, dirty flags, ConPTY pump, mouse encoding, kitty graphics, in-world teardown, in-world premultiplied-alpha transparency, layout persistence, in-world part-follow tracking, etc.) |
| [docs/how-to.md](docs/how-to.md) | Recipes: change rendering, add themes, extend binding, add shells, create in-world terminals, theme a named terminal, save/load/edit a layout, auto-run a command, deploy |

Other reference:
- Solution: `purrtty.slnx`
- Shared build config + KSA DLL paths (per-OS): `Directory.Build.props`
- Migration plan + status: `LIBGHOSTTY_ANALYSIS.md`; provenance/licensing: `vendor/Ghostty.Vt/README.md`, `THIRD-PARTY-NOTICES.md`
- Kitty graphics plan: `KITTY_PLAN.md`

## Code Standards (from Directory.Build.props)
- .NET 10 / C# 13; nullable enabled; warnings-as-errors (except CS1591).
- `purrTTY.Terminal` additionally NoWarns CS1591; the vendored `Ghostty.Vt` relaxes both
  warnings-as-errors and XML-doc generation in its csproj (third-party-derived sources).

## Instruction Maintenance Mandate (MUST)

Whenever you make meaningful repository changes, you MUST evaluate and update this file AND the
relevant `docs/` files in the same work item if it affects: project structure/dependencies, the
backend/frontend seam, the engine binding surface, build/test/deploy commands, or feature status.
Remove defunct guidance immediately; prefer verified code paths over plans when documenting
behavior; keep navigation pointers current. Do not document the deleted bespoke emulator as if
it still exists.
