# ImGui Playground Consumer Checks

Companion executable checks for [ImGuiPlayground](../ImGuiPlayground/README.md), not a mod
test project or a reusable host implementation test suite. The sibling folders form an
independent package-consumer workspace. The only ProjectReference is to the application;
`XBrutal.ImGuiStandalone` **`0.1.0-local.4`** is resolved transitively through its exact
PackageReference. There is no reference or linked-source fallback to the producer repository.

Local props import only the application's settings and block enclosing-repository build/package
configuration. Both projects select `ImGuiPlayground/NuGet.Config`, with the scoped local feed
and source mapping. External BRUTAL assemblies must be user-supplied and match the package's
compatibility contract. See the application README for feed/archive hash, prerequisites and
native/font ownership. The standalone solution is `../ImGuiPlayground/ImGuiPlayground.slnx`.

## Setup and execution

From `ImGuiPlayground.Checks/`, with .NET 10:

```bash
export KSA_DLL_DIR='/absolute/path/to/matching/managed/dll'
unset KSAFolder # KSAFolder takes precedence; do not leave a conflicting value.
dotnet run -v quiet
# Graphical gallery/input checks; retain images for review:
dotnet run --no-build -- --widgets /tmp/widget-captures
# No desktop required; portable + all supported RID consumer output checks:
dotnet run --no-build -- --packaging
dotnet run --no-build -- --help
```

PowerShell uses `$env:KSA_DLL_DIR = 'C:\path\to\matching\managed\dll'`. Use environment
selection for packaging child builds: outer `dotnet run -p:` arguments are not inherited.
Default Checks captures the camera application through `StandaloneHost`, asserting three
callbacks, complete physical RGBA dimensions and the package-delivered JetBrains Mono font
via a direct BRUTAL call. It runs synchronously on Main (macOS GLFW requires the main thread).
Graphical checks need a desktop/OpenGL driver; invisible capture is not a headless backend.
All checks are quiet on pass, fail with a nonzero exit code and never use fixed sleeps.

The .NET SDK 10.0.100 historical solution-path caveat remains: on macOS, an absolute `.slnx`
path through `/var` (a symlink to `/private/var`) was observed to omit project/package entries
from Checks' `.deps.json`. Use a relative solution path from its folder, a physically resolved
absolute path or a direct project build. Do not hand-edit `.deps.json`.

## Focused widget gallery checks

`--widgets [capture-directory]` uses the ordinary packaged-native host. It renders camera,
parts, tuning and the built-in demo at 800×500 logical size, plus the selected-part tree,
reload modal and full color picker popup. Assertions require bright text/colored pixels,
different scenes and expected application state. Real ImGui IO mouse press/release events
assert **Play** starts playback and **Loop** toggles fake state. After the Play click, a fresh
capture of the same model saves the settled green PLAYING label (submitted before the button).
Direct/gallery-opened demo captures check public window bounds and central content. The latter
uses an internal application request seam, not an assertion of checkbox/menu input.

Nine screenshots are retained:

```text
camera.png          parts.png          tuning.png
demo.png            parts-modal.png    tuning-picker.png
camera-play.png     camera-loop.png    demo-gallery.png
```

Without a directory they are saved in `widget-captures/` beside the checks executable.
This is representative application coverage, not exhaustive widgets/native qualification or
an identical-pixel promise across drivers/platforms. Text editing, menus, modal confirmation,
scrolling and color/numeric editing are useful additional manual review cases.

## Package-consumer output checks

`--packaging [path/to/ImGuiPlayground.csproj]` locates the sibling source project from the
executable; pass a project path when running outside the source tree. It starts bounded child
publishes into system-temporary directories, removed on completion. Checks verify:

- The restored exact XBrutal package archive matches the approved SHA-256.
- Portable publish keeps native RID subdirectories and has no obsolete flat native files.
- Framework-dependent osx-arm64, linux-x64 and win-x64 publishes receive matching native images.
- ImGui/managed library/font bytes equal the resolved XBrutal package assets.
- GLFW originals and BRUTAL aliases equal resolved **Ultz.Native.GLFW 3.4.0** assets.
- External copy-local ImGui/GLFW references and application notices reach output.

It never loads foreign binaries. Native architecture/export/ABI checks, runtime negatives,
renderer/input/lifecycle tests and PNG implementation tests now belong to the producer's
`checks/`, not this application. Windows/Linux execution remains pending.

For fresh-cache source provenance without touching global NuGet state, from the enclosing root:

```bash
export KSA_DLL_DIR='/absolute/path/to/matching/managed/dll'
unset KSAFolder
ART=$(mktemp -d /tmp/playground-consumer.XXXXXX)
export NUGET_PACKAGES="$ART/cache"
dotnet build ImGuiPlayground/ImGuiPlayground.slnx --nologo -v quiet
cat "$NUGET_PACKAGES/xbrutal.imguistandalone/0.1.0-local.4/.nupkg.metadata"
dotnet run --project ImGuiPlayground -- --smoke-test
dotnet run --project ImGuiPlayground -- --capture "$ART/camera.png"
dotnet run --project ImGuiPlayground.Checks --no-build -- --widgets "$ART/widgets"
dotnet run --project ImGuiPlayground.Checks --no-build -- --packaging
```

The metadata's `source` must be the configured local feed; hash its cached `.nupkg` against
the application README's pin. No global cache/config edits, native compiler or game deployment.

## Extraction and archive boundary

Copy only these two sibling folders outside purrtty, excluding `bin/`, `obj/` and `.tmp/`.
Configure the local feed and external assemblies, then build the relative standalone solution,
run default/`--widgets`/`--packaging` Checks and app smoke/capture. Parent props/targets/package
props and NuGet config must not replace this workspace's settings.

Removed generator, binding exporter, prototype and full-fixture qualification code/instructions
are archived at **purrtty `5d71c50943c13310d02066eb56b6fd4abc914790`**, not available CLI modes
in this version. Historical evidence and the frozen production source closure are documented
in the producer's `native/manifest.json` and [platform/archive status](../ImGuiPlayground/VALIDATE_PLATFORMS.md).
Full historical fixture certification was not rerun by these consumer checks.

## Files and licensing

- `Program.cs`: CLI and minimal application/package capture integration check.
- `WidgetGalleryChecks.cs`: nine captures and application state/Play/Loop assertions.
- `PackagingChecks.cs`: bounded consumer publishes and package-origin asset comparisons.
- [LICENSE](LICENSE): original project code; third-party terms are in the application's
  [THIRD-PARTY-NOTICES.md](../ImGuiPlayground/THIRD-PARTY-NOTICES.md) and restored package.
