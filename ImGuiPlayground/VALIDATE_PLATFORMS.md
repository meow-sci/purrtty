# Standalone consumer platform status and archive boundary

The active application consumes **XBrutal.ImGuiStandalone 0.1.0-local.4**. Native production
ownership/rebuilds, host/input/renderer/capture/accessors and reusable tests belong to the
producer repository **`brutal-imgui-standalone`**, not to purrtty. The package ships source-owned
Dear ImGui 1.92.2 docking/WCHAR32 images for osx-arm64, linux-x64 and win-x64. GLFW remains
an exact transitive Ultz.Native.GLFW 3.4.0 dependency, not a vendored binary payload.

| Current consumer gate | macOS ARM64 | Windows x64 | Linux x64 |
|---|---|---|---|
| Portable default build/run smoke and framebuffer capture | Passed | Native-host execution pending | Native-host execution pending |
| Direct BRUTAL gallery and nine images, Play/Loop assertions | Passed | Pending | Pending |
| Portable and explicit-RID publish asset inspection | Passed | Cross-published/inspected only | Cross-published/inspected only |
| Manual desktop input/resizing/DPI/monitor transitions | Not fully qualified | Pending | Pending |

Cross-publish/static inspection is **not foreign execution certification**. No Wine-as-native
claim is made. Native GLFW/OpenGL requires a working desktop/driver even for invisible capture.
Linux may provide Xvfb/Mesa externally, but it has not been runtime-certified here. Unsupported
process RIDs include Intel macOS, ARM Windows/Linux, musl and 32-bit.

## Current target-host checks

Keep the two playground folders as siblings; install .NET 10 and configure the local package
feed in `ImGuiPlayground/NuGet.Config`. User-supplied BRUTAL managed assemblies must match the
loaded hash/MVID contract in package `docs/COMPATIBILITY.md`. `KSAFolder` overrides `KSA_DLL_DIR`;
unset a conflicting value. From the enclosing root on the **matching native target host**:

```bash
export KSA_DLL_DIR='/absolute/path/to/matching/managed/dll'
unset KSAFolder
dotnet build ImGuiPlayground/ImGuiPlayground.slnx --nologo -v quiet
dotnet run --project ImGuiPlayground -- --smoke-test
dotnet run --project ImGuiPlayground -- --capture /tmp/playground-camera.png
dotnet run --project ImGuiPlayground.Checks --no-build
dotnet run --project ImGuiPlayground.Checks --no-build -- --widgets /tmp/playground-widgets
dotnet run --project ImGuiPlayground.Checks --no-build -- --packaging
```

PowerShell uses `$env:KSA_DLL_DIR`; choose Windows capture paths. No native compilation,
external native directory or game deployment is involved. Use a fresh isolated `NUGET_PACKAGES`
for feed-origin evidence; do not clear the global cache or modify machine-wide NuGet config.
Retain OS/architecture, .NET SDK/runtime, desktop/display scale, GPU/driver, commands/exit codes,
cache source metadata, package/native hashes and all nine PNGs. Also manually test keyboard,
Unicode, clipboard, focus loss, resize/minimize/restore, closing/reopening and Windows 250%
scaling across monitors. The package owns DPI conversion; do not add font/style scaling here.

Known limits remain: no OS multiviewport backend, gamepads, cursor shapes/warping, full IME or
arbitrary game Vulkan texture handles. Complete native exports do not make every raw BRUTAL
field/layout safe. Packed aliases, differing strides and opaque records remain restricted.

## Frozen historical native qualification

The former generator/prototype/full fixture/exporter/qualification implementations and their
old instructions are archived at the exact **purrtty baseline revision**:

```text
5d71c50943c13310d02066eb56b6fd4abc914790
```

Historical source/instruction pointers (use `git show <revision>:<path>`):

- `ImGuiPlayground/native/full/qualification/README.md`
- `ImGuiPlayground/native/full/{api,layout,manual,profile,accessors,combined}/`
- `ImGuiPlayground.Checks/BindingContractExporter.cs`, `Full*Checks.cs`,
  `CombinedComponentChecks.cs`, `PrototypeAbiChecks.cs`, `prototype_runner.py`
- Baseline `ImGuiPlayground/README.md`, `ImGuiPlayground.Checks/README.md`, and this checklist.

The producer's `native/manifest.json` (included as package `docs/manifest.json`) records:

```text
production bundle: sol-delivery-20261008T144358Z-33442-combined-final3
qualification archive SHA-256:
b0c089d45a6aeac64e8affa9ddbc2fb48675ec1c00d23e805f346be98cd5fd39
```

The exact hash-bound production source closure was preserved in the producer before deleting
obsolete tracked tooling here. Existing ignored `.tmp` bundles/evidence were not deleted.
Historical full macOS fixture/component and production renderer/input/capture gates passed
at the baseline, but the full generator/fixture tooling was **not relocated or rerun** in the
package extraction. Producer checks provide a current representative production/helper floor,
not a substitute claim of exhaustive historical fixture certification. Future ABI regeneration
and full-fixture requalification require a separate work item; there are no active historical
qualification CLI modes here.
