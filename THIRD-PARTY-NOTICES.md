# Third-Party Notices

purrTTY incorporates the third-party components below. Full license texts ship in the
`third-party-licenses/` folder of the mod distribution (and live at the same path in this repo).

## Ghostty.Vt (libghostty-vt-dotnet)

- **Path in repo:** `vendor/Ghostty.Vt/`
- **Upstream:** https://github.com/deblasis/libghostty-vt-dotnet
- **License:** MIT (see `vendor/Ghostty.Vt/LICENSE`)
- **Description:** Managed .NET bindings for libghostty-vt. Vendored and extended by purrtty.

## libghostty-vt (Ghostty)

- **Upstream:** https://github.com/ghostty-org/ghostty
- **License:** MIT
- **Description:** The native VT engine. Pinned (not forked); built from source and
  distributed as prebuilt shared libraries under `vendor/Ghostty.Vt/native/<rid>/`
  (osx-arm64, win-x64, linux-x64).

### Statically linked into the native library

The prebuilt libghostty-vt binaries statically link the following:

- **Highway** — https://github.com/google/highway — Apache-2.0 (alternatively BSD-3-Clause);
  see `third-party-licenses/LICENSE.highway` and `LICENSE.highway-BSD3`.
- **simdutf** — https://github.com/simdutf/simdutf — MIT (alternatively Apache-2.0);
  see `third-party-licenses/LICENSE.simdutf`.

## Tomlyn

- **Shipped as:** `Tomlyn.dll`
- **Upstream:** https://github.com/xoofx/Tomlyn
- **License:** BSD-2-Clause — see `third-party-licenses/LICENSE.tomlyn`.
- **Description:** TOML parser/writer used for theme and configuration files.

## Microsoft.Extensions.Logging.Abstractions

- **Shipped as:** `Microsoft.Extensions.Logging.Abstractions.dll`
- **Upstream:** https://github.com/dotnet/runtime
- **License:** MIT — see `third-party-licenses/LICENSE.dotnet`.

## ModMenu.Attributes

- **Shipped as:** `ModMenu.Attributes.dll`
- **Upstream:** https://github.com/MrJeranimo/ModMenu
- **License:** MIT — see `third-party-licenses/LICENSE.modmenu`.
- **Description:** Attribute contract for registering menus with the ModMenu
  companion mod (used when ModMenu is installed; purrTTY falls back to its own
  menu hook otherwise).

## Lib.Harmony

- **Upstream:** https://github.com/pardeike/Harmony
- **License:** MIT — see `third-party-licenses/LICENSE.harmony`.
- **Description:** Runtime patching library. Compiled against; `0Harmony.dll` itself is
  supplied by the StarMap mod loader and is not shipped in this distribution.

## Nerd Fonts (bundled terminal fonts, `TerminalFonts/`)

- **Upstream:** https://www.nerdfonts.com/ (https://github.com/ryanoasis/nerd-fonts)
- **License:** see `third-party-licenses/LICENSE.nerdfonts` (Nerd Fonts patching/assets) plus
  the per-family licenses below.

| Family | License file |
|---|---|
| Departure Mono | `LICENSE.font_DepartureMono` |
| Hack | `LICENSE.font_Hack` |
| JetBrains Mono | `LICENSE.font_JetBrainsMono` |
| ProFont | `LICENSE.font_ProFont` |
| Proggy Clean | `LICENSE.font_ProggyClean` |
| ShareTech Mono | `LICENSE.font_ShareTechMono` |
| Space Mono | `LICENSE.font_SpaceMono` |

## iTerm2-Color-Schemes (bundled themes, `TerminalThemes/`)

- **Upstream:** https://github.com/mbadolato/iTerm2-Color-Schemes
- **License:** MIT — see `third-party-licenses/LICENSE.iterm2-color-schemes`.
- **Description:** The bundled color schemes are derived from this collection
  (alacritty-format TOML).
