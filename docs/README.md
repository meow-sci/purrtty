# purrTTY-cs - C# Terminal Emulator for KSA

A C# terminal emulator implementation for the Kitten Space Agency (KSA) game engine, translated from the TypeScript purrTTY implementation.

## Project Structure

```
purrTTY.Core/              # Headless terminal logic (no dependencies)
├── Terminal/            # Core terminal emulation
├── Input/               # Input processing and encoding
├── Parsing/             # Escape sequence parsers
├── Types/               # Data structures and enums
└── Utils/               # Utility functions
purrTTY.ImGui/             # ImGui display controller
├── Controllers/         # Terminal controller
├── Rendering/           # ImGui rendering logic
└── Input/               # ImGui input handling
purrTTY.TestApp/           # Standalone console application
purrTTY.GameMod/           # Game mod build target (DLL output)
purrTTY.Core.Tests/        # Unit and property tests for Core
├── Unit/                # Unit tests
└── Property/            # Property-based tests (FsCheck)
purrTTY.ImGui.Tests/       # Unit and property tests for ImGui
├── Unit/                # Unit tests
└── Integration/         # Integration tests
```

## Build Commands

### Build the entire solution
```bash
dotnet build
```

### Run tests
```bash
dotnet test
```

### Run the console test application
```bash
dotnet run --project purrTTY.TestApp
```

### Build the game mod
```bash
dotnet build purrTTY.GameMod
```

The game mod DLL will be output to `purrTTY.GameMod/dist/` along with the required `mod.toml` file.

## Dependencies

- **.NET 10** - Latest LTS version with C# 13 language features
- **NUnit 3.14.0** - Testing framework
- **FsCheck.NUnit 2.16.6** - Property-based testing
- **StarMap.API 0.3.6** - KSA mod API
- **Lib.Harmony 2.4.2** - Runtime patching for game integration

## KSA Game Integration

The `purrTTY.ImGui` and `purrTTY.GameMod` projects reference KSA game DLLs from the installation directory:
- `Brutal.Core.Common.dll`
- `Brutal.Core.Strings.dll`
- `Brutal.Core.Numerics.dll`
- `Brutal.ImGui.dll`
- `KSA.dll`

The default installation path is `C:\Program Files\Kitten Space Agency\`. This can be overridden by setting the `KSAFolder` property in `Directory.Build.props`.

## Project References

- **purrTTY.TestApp** → purrTTY.Core
- **purrTTY.ImGui** → purrTTY.Core
- **purrTTY.GameMod** → purrTTY.ImGui → purrTTY.Core
- **purrTTY.Core.Tests** → purrTTY.Core
- **purrTTY.ImGui.Tests** → purrTTY.ImGui

## Configuration

### Directory.Build.props
Shared build configuration for all projects:
- Target Framework: net10.0
- Language Version: C# 13.0
- Nullable Reference Types: Enabled
- Treat Warnings as Errors: Enabled
- XML Documentation: Enabled

### .editorconfig
C# formatting and style rules for consistent code formatting across the solution.

## Development Workflow

1. **Develop and test core logic**: Work in `purrTTY.Core` with tests in `purrTTY.Core.Tests/`
2. **Test standalone**: Run `purrTTY.TestApp` for quick console-based testing
3. **Integrate with ImGui**: Implement display logic in `purrTTY.ImGui` with tests in `purrTTY.ImGui.Tests/`
4. **Deploy to game**: Build `purrTTY.GameMod` and copy `dist/` contents to KSA mods folder

### Testing Strategy

The solution follows the conventional .NET pattern of per-project test projects:

- **purrTTY.Core.Tests**: Tests the headless terminal logic in isolation
  - Unit tests for core components (parsers, terminal state, etc.)
  - Property-based tests for correctness properties using FsCheck
- **purrTTY.ImGui.Tests**: Tests the ImGui integration layer
  - Unit tests for ImGui controllers and rendering
  - Integration tests for game engine interaction

This approach provides:
- Clear separation of concerns
- Focused test scopes
- Better maintainability as the project grows
- Standard .NET testing conventions

## Reference Implementation

The `KsaExampleMod/` folder in the repository root provides a complete working example of KSA game mod structure. Refer to it for:
- Project file configuration with KSA DLL references
- Mod metadata file structure (`mod.toml`)
- StarMap attribute-based mod implementation
- Harmony patching patterns
- Asset management and build targets