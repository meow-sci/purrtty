# ImGui Playground — Third-Party Notices

These notices cover ImGuiPlayground and its companion ImGuiPlayground.Checks project.
Original project code is covered by [LICENSE](LICENSE); third-party components retain
their own terms below. License texts and this notice accompany build/publish output.

- **Dear ImGui:** <https://github.com/ocornut/imgui>, MIT, copyright Omar Cornut and
  contributors. The managed renderer follows the OpenGL backend's contracts/shader math
  at `v1.92.2-docking` (`04c3466d23a72abee3696dcba698b0e02fee6057`), adapted to C#.
  Ordinary app/checks builds do not download or compile C++ sources. An explicitly invoked,
  maintainer-only ABI feasibility prototype separately builds hash-pinned, unmodified Dear ImGui
  core/demo C++ in ignored `.tmp/native/`; it is not a runtime asset and is never promoted.
  License: [licenses/LICENSE.imgui.txt](licenses/LICENSE.imgui.txt).
- **GLFW:** <https://www.glfw.org/>, zlib/libpng license. Native assets are restored from
  pinned [Ultz.Native.GLFW 3.4.0](https://www.nuget.org/packages/Ultz.Native.GLFW/3.4.0),
  selected per RID and hash-verified before copying to output. License:
  [licenses/LICENSE.glfw.txt](licenses/LICENSE.glfw.txt); provenance: [runtimes/README.md](runtimes/README.md).
- **Microsoft.Extensions.Logging / ObjectPool and dependencies:** standard Microsoft .NET
  packages, MIT; see [licenses/LICENSE.dotnet.txt](licenses/LICENSE.dotnet.txt) and package metadata.
- **JetBrains Mono Regular:** unmodified default game font from
  `ksa-linux/Content/Core/JetBrainsMono-Regular.ttf`, vendored in `fonts/`.
  Copyright 2020 The JetBrains Mono Project Authors; SIL OFL 1.1. The accompanying
  `JetBrainsMono-Regular-license.txt` and provenance are beside the font.
- **KSA/BRUTAL:** managed assemblies remain external, user-supplied references through
  `KSAFolder` / `KSA_DLL_DIR`, copied to local output as normal .NET dependencies, not vendored.
  At the operator's explicit request, unmodified native ImGui binaries are vendored for
  `osx-arm64`, `linux-x64` and `win-x64` under `runtimes/`; their hashes and source provenance
  are recorded there. Upstream Dear ImGui's MIT notice does not establish redistribution
  rights for KSA's complete wrapper binaries. No additional permission is implied: resolve
  game-component licensing before publishing those binaries or playground output.
- **Playground native ABI adapter:** the original bounded prototype adapter and probe are
  licensed under MIT; see [native/LICENSE.adapter.txt](native/LICENSE.adapter.txt). The adapter
  forwards to the pinned upstream API and is experimental, not full BRUTAL compatibility.
