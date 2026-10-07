# ImGui Playground Checks

Companion executable checks for [ImGuiPlayground](../ImGuiPlayground/README.md), not a mod
test project. The two sibling folders form one independent workspace; the only project
reference is to the playground. Local `Directory.Build.props` imports the playground's
settings and blocks enclosing-repository build/package configuration. The standalone
solution is `../ImGuiPlayground/ImGuiPlayground.slnx`.

## Setup and execution

Run these commands from this `ImGuiPlayground.Checks/` folder with the .NET 10 SDK:

```bash
export KSA_DLL_DIR="/absolute/path/to/KSA"
dotnet run -v quiet
# Packaging-only: no graphical session needed.
dotnet run -- --packaging
```

On PowerShell, set `$env:KSA_DLL_DIR = 'C:\Program Files\Kitten Space Agency'` instead.
Use an environment variable for packaging checks: child `dotnet` processes do not inherit
MSBuild `-p:` arguments from the initial build. Required managed assemblies remain external;
there is no automatic repository/game-directory lookup. See the host README for native
runtime prerequisites and compatibility limitations.

The default checks need a graphical session/OpenGL driver and run on `Main`, not NUnit
worker threads (macOS GLFW requires the main thread). They cover deterministic pixel
regions/orientation/scissors, large meshes, PNG round-trip, font selection/texture updates,
callback state reset, keyboard/character fidelity and callback-failure/repeated-run recovery.
They are quiet on success, fail with a nonzero exit code, and never use fixed sleeps.

`--packaging` locates the sibling source project relative to the executable, so it does
not depend on the caller's working directory. When running a copied/published checks
executable away from its source tree, pass the project explicitly:

```bash
dotnet run -- --packaging /absolute/path/to/ImGuiPlayground/ImGuiPlayground.csproj
```

Packaging checks cross-publish all three supported RIDs, verify native filenames, hashes,
architectures, managed dependencies, font and notices, replace deliberately stale/newer
output files, and require unsupported-RID/hash-mismatch failures. They do not execute
foreign binaries. Windows/Linux runtime behavior still needs validation on those platforms.

## Extraction check

To verify the workspace remains independent when changing build configuration:

1. Copy **only** `ImGuiPlayground/` and `ImGuiPlayground.Checks/` into a fresh directory
   outside the enclosing repository, preserving their sibling layout. Exclude `bin/`,
   `obj/` and `.tmp/`; do not copy parent build props, docs, licenses or solutions.
2. Set `KSA_DLL_DIR` to an absolute external game/assemblies path.
3. From the copied `ImGuiPlayground/` folder, run:

   ```bash
   dotnet build ImGuiPlayground.slnx --nologo -v quiet
   dotnet run --project ../ImGuiPlayground.Checks -- --packaging
   dotnet run --project ../ImGuiPlayground.Checks
   dotnet run --project ImGuiPlayground.csproj -- --capture .tmp/hello.png
   ```

4. Confirm changes remain confined to the standalone folders and no deployment occurs.
   For stronger isolation coverage, place failing `Directory.Build.props`,
   `Directory.Build.targets` and `Directory.Packages.props` files in the temporary parent:
   neither project should import them. A parent NuGet config should likewise not replace
   the explicitly selected playground config.

## Files and licensing

- `Program.cs`: main-thread rendering/capture fixtures and assertions.
- `InteropChecks.cs`: native callback/state-reset and input compatibility fixtures.
- `PackagingChecks.cs`: bounded child builds and platform-asset validation.
- [LICENSE](LICENSE): original project code; third-party terms and caveats are in the
  playground's [THIRD-PARTY-NOTICES.md](../ImGuiPlayground/THIRD-PARTY-NOTICES.md).
