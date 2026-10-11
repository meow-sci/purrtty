# ImGui Playground — Third-Party Notices

These notices cover the ImGuiPlayground application and companion consumer Checks.
Original project code is covered by [LICENSE](LICENSE). Third-party components retain
their own terms; application notices/licenses accompany build/publish output.

- **XBrutal.ImGuiStandalone 0.1.0-local.4:** the separately packaged host, OpenGL renderer,
  GLFW input, capture/PNG implementation and native accessors. It owns the source-built
  Dear ImGui 1.92.2 docking runtime assets and font delivery. Its archive contains `LICENSE`,
  `THIRD-PARTY-NOTICES.md`, native provenance/compatibility/accessor contracts and license texts.
  There is no duplicated native/host implementation or font payload in this application.
- **Dear ImGui:** <https://github.com/ocornut/imgui>, MIT, copyright Omar Cornut and contributors.
  Native production images now come from XBrutal's frozen source-owned builds, not the former
  game-derived wrapper images. License copy: [licenses/LICENSE.imgui.txt](licenses/LICENSE.imgui.txt).
- **GLFW:** <https://www.glfw.org/>, zlib/libpng license. Native delivery is the package's exact
  transitive [Ultz.Native.GLFW 3.4.0](https://www.nuget.org/packages/Ultz.Native.GLFW/3.4.0)
  dependency. Neither this application nor XBrutal vendors GLFW binaries. The package stages
  BRUTAL filename aliases automatically. License: [licenses/LICENSE.glfw.txt](licenses/LICENSE.glfw.txt).
- **Microsoft.Extensions.Logging / ObjectPool and dependencies:** standard Microsoft .NET
  packages, MIT, resolved transitively. See [licenses/LICENSE.dotnet.txt](licenses/LICENSE.dotnet.txt)
  and dependency metadata.
- **JetBrains Mono Regular:** copyright 2020 The JetBrains Mono Project Authors, SIL OFL 1.1.
  The package automatically supplies the unmodified font and `licenses/JetBrainsMono-Regular-license.txt`;
  there is no application-owned font staging. Provenance is recorded in that package.
- **KSA/BRUTAL:** managed assemblies remain external licensed user-supplied references via
  `KSAFolder` / `KSA_DLL_DIR`, copied to local output as normal .NET dependencies, not vendored
  or supplied by NuGet. Resolve their redistribution rights before publishing application output.

Historical generator/prototype/fixture sources and notices remain archived in purrtty at
`5d71c50943c13310d02066eb56b6fd4abc914790`; see [the archive boundary](VALIDATE_PLATFORMS.md).
No old game-derived native wrapper or experimental fixture binary is shipped by this application.
