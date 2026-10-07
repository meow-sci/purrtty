# Pinned playground native runtimes

The playground deliberately separates **external managed game assemblies** from
**reproducible native runtime assets**:

- `Brutal.*.dll` remain external via explicit `KSAFolder` / `KSA_DLL_DIR` configuration
  in the playground's own build settings. They are not vendored here; no enclosing
  repository configuration or `KSA.dll` is required by the host.
- Native ImGui binaries below are unmodified copies from operator-supplied KSA builds.
- Native GLFW is restored from **Ultz.Native.GLFW 3.4.0**, not a local game/Homebrew install.
  This is a native-only package; it does not replace the `Brutal.Glfw` managed API.

There is no native compiler, custom bridge, game installation scan, or native-library
path setting in a normal build. `NativeLibraries.props` is the authoritative manifest
of per-file **SHA-256 pins and output names**. `ValidatePlaygroundNative` verifies the
selected pair before copying; a changed binary requires a deliberate pin update.

## Selection and layout

`RuntimeIdentifier` (`dotnet build/publish -r ...`) wins; without one, the SDK host RID
is used. Supported targets are exactly **osx-arm64, linux-x64, win-x64**. Unsupported
RIDs fail explicitly rather than falling back to the host's binary. Each output contains
one native pair, flat beside the executable, including when referenced by the checks project.

| RID | Vendored ImGui input | GLFW input inside NuGet | GLFW output alias |
| --- | --- | --- | --- |
| `osx-arm64` | `osx-arm64/native/libimgui.dylib` | `runtimes/osx-arm64/native/libglfw.3.dylib` | `libglfw.dylib` |
| `linux-x64` | `linux-x64/native/libimgui.so` | `runtimes/linux-x64/native/libglfw.so.3` | `libglfw.so` |
| `win-x64` | `win-x64/native/imgui.dll` | `runtimes/win-x64/native/glfw3.dll` | `glfw3.dll` |

The aliases are required by BRUTAL GLFW's own resolver. Package runtime assets are
excluded from automatic NuGet copying to avoid a second, competing runtime layout.
Copying uses `Always`, not `PreserveNewest`: a newly selected package can contain older
timestamps than a previously staged DLL. A clean destination is recommended when changing
publish RIDs; a single output directory is not a universal multi-platform distribution.

## ImGui provenance

These copies were supplied/approved for vendoring by the repository operator. Source
locations below describe acquisition, **not build-time paths**. Native files are not
rebuilt, stripped, patched, or re-signed by this project.

| RID | Source | Bytes | SHA-256 |
| --- | --- | ---: | --- |
| `osx-arm64` | Previously validated older KSA distribution, `runtimes/osx-arm64/native/libimgui.dylib` (native arm64 Mach-O; not a Wine binary) | 1240336 | `19e99251a2511477ab60365e3fa8613f8d56565c06feab67be17fd277ec2409c` |
| `linux-x64` | Operator's latest `ksa-linux/libimgui.so` full distribution | 1561600 | `3a7983b30f4e2f6c2bf3fda5b6757a81053a71e54349033f454317e99d8ea45b` |
| `win-x64` | `ksa-game-assemblies/current/dll/imgui.dll`, checkout `80956e54f5542479cb2636ece7bf3654cbe72bcb`, whose build metadata is `2026.10.7.5541` | 1151488 | `6bbed31165a33c587333cad17582294a4691f68a2085c4c2a21cf7f513c1e9fc` |

All contain the ImGui `1.92.2` version string; macOS also returned that version at runtime.
The checked Windows/Linux export tables contain all 590 extern names declared in the
current decompiled `ImGui.cs`; the older macOS file lacks `TextV` (the host uses
`TextUnformatted`, not that entry point). Export presence does **not** prove ABI compatibility.
Keep the runtime version/layout checks and integration tests when changing managed bindings.

**Known macOS limitation:** the older native library uses 16-bit `ImWchar`, whereas the
current generated character-vector wrapper declares `uint`. The host does not read those
vectors or supply explicit glyph-range buffers. Supplementary characters are replaced by
the native engine, and arbitrary raw wchar-backed binding access is unsafe with that copy.
See the playground README; this is not full native/binding parity.

## GLFW provenance

- Package: <https://www.nuget.org/packages/Ultz.Native.GLFW/3.4.0>
- Archive: <https://api.nuget.org/v3-flatcontainer/ultz.native.glfw/3.4.0/ultz.native.glfw.3.4.0.nupkg>
- Archive SHA-256: `f8121464ef88c15bc1c421da4e39c4029a116d8994d38a600fbb9e8d989d13be`
- Package metadata: .NET Foundation and Contributors; native GLFW; Zlib license.
- Individual selected native-file hashes are pinned in `NativeLibraries.props`.

## System dependencies and validation scope

- macOS arm64: system Cocoa/OpenGL/C++ runtime; no Homebrew GLFW requirement.
- Linux x64: glibc/libstdc++/libgcc, a graphical session and GL driver; the supplied ImGui
  binary imports glibc symbols through **GLIBC_2.29**. GLFW also needs the appropriate
  platform window-system libraries. Alpine/musl is not a supported target.
- Windows x64: system graphics drivers and the **Microsoft Visual C++ 2015–2022 x64
  Redistributable** (`VCRUNTIME140.dll`, `VCRUNTIME140_1.dll`) plus Windows' UCRT. Do not
  copy Windows system DLLs into this repository.

macOS smoke, rendering/input checks and PNG capture have been run with this native pair.
All three RIDs have cross-publish/hash/architecture checks; **Windows/Linux execution has
not been validated on this Mac**. Packaging is not a substitute for target-OS execution.

## Updating a pin

1. Obtain the operator-approved native binary from a known KSA build. Preserve it unchanged
   in the corresponding RID directory; record provenance and SHA-256 here and in the manifest.
2. For GLFW, deliberately update the package version and all three asset hashes together.
3. Confirm architecture, exports, native dependencies, version and managed/native layouts.
   Version strings and `DebugCheckVersionAndDataLayout` alone do not validate every structure.
4. From the `ImGuiPlayground/` folder, with `KSA_DLL_DIR` set in the environment, run:

   ```bash
   dotnet run --project ../ImGuiPlayground.Checks -- --packaging
   dotnet run --project ../ImGuiPlayground.Checks
   dotnet run --project ImGuiPlayground.csproj -- --capture .tmp/hello.png
   ```

   The first command needs no desktop and checks all RIDs, stale-output replacement,
   unsupported-RID rejection and hash enforcement. The other checks run on the current OS;
   repeat on each supported OS before claiming cross-platform runtime validation.
5. Keep build settings and notices inside the standalone workspace; verify extraction
   using the [checks recipe](../../ImGuiPlayground.Checks/README.md#extraction-check).

## Licensing / redistribution

Dear ImGui's upstream MIT notice and GLFW's Zlib notice are in `../licenses/`. That does
**not** establish redistribution permission for KSA's complete BRUTAL-specific native
wrapper binaries. They are vendored at the operator's explicit request; permissions for
publishing these game-derived binaries still need to be resolved separately. Managed
KSA/BRUTAL DLLs remain user-supplied external references, even though normal build/publish
output copies their runtime dependencies. See [the standalone notices](../THIRD-PARTY-NOTICES.md).
