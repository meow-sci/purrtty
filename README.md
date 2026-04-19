# caTTY-cs - C# Terminal Emulator for KSA

A C# terminal emulator implementation for the Kitten Space Agency (KSA) game engine, translated from the TypeScript caTTY implementation.

## Project Structure

```
catty-ksa/
├── caTTY.Core/              # Headless terminal logic (no dependencies)
│   ├── Terminal/            # Core terminal emulation
│   ├── Input/               # Input processing and encoding
│   ├── Parsing/             # Escape sequence parsers
│   ├── Types/               # Data structures and enums
│   └── Utils/               # Utility functions
├── caTTY.ImGui/             # ImGui display controller
│   ├── Controllers/         # Terminal controller
│   ├── Rendering/           # ImGui rendering logic
│   └── Input/               # ImGui input handling
├── caTTY.TestApp/           # Standalone console application
├── caTTY.GameMod/           # Game mod build target (DLL output)
├── caTTY.Core.Tests/        # Unit and property tests for Core
│   ├── Unit/                # Unit tests
│   └── Property/            # Property-based tests (FsCheck)
└── caTTY.ImGui.Tests/       # Unit and property tests for ImGui
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
dotnet run --project caTTY.TestApp
```

### Build the game mod
```bash
dotnet build caTTY.GameMod
```

The game mod DLL will be output to `caTTY.GameMod/dist/` along with the required `mod.toml` file.

## Dependencies

- **.NET 10** - Latest LTS version with C# 13 language features
- **NUnit 3.14.0** - Testing framework
- **FsCheck.NUnit 2.16.6** - Property-based testing
- **StarMap.API 0.3.6** - KSA mod API
- **Lib.Harmony 2.4.2** - Runtime patching for game integration

## KSA Game Integration

The `caTTY.ImGui` and `caTTY.GameMod` projects reference KSA game DLLs from the installation directory:
- `Brutal.Core.Common.dll`
- `Brutal.Core.Strings.dll`
- `Brutal.Core.Numerics.dll`
- `Brutal.ImGui.dll`
- `KSA.dll`

The default installation path is `C:\Program Files\Kitten Space Agency\`. This can be overridden by setting the `KSAFolder` property in `Directory.Build.props`.

## Project References

- **caTTY.TestApp** → caTTY.Core
- **caTTY.ImGui** → caTTY.Core
- **caTTY.GameMod** → caTTY.ImGui → caTTY.Core
- **caTTY.Core.Tests** → caTTY.Core
- **caTTY.ImGui.Tests** → caTTY.ImGui

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

1. **Develop and test core logic**: Work in `caTTY.Core` with tests in `caTTY.Core.Tests/`
2. **Test standalone**: Run `caTTY.TestApp` for quick console-based testing
3. **Integrate with ImGui**: Implement display logic in `caTTY.ImGui` with tests in `caTTY.ImGui.Tests/`
4. **Deploy to game**: Build `caTTY.GameMod` and copy `dist/` contents to KSA mods folder

### Testing Strategy

The solution follows the conventional .NET pattern of per-project test projects:

- **caTTY.Core.Tests**: Tests the headless terminal logic in isolation
  - Unit tests for core components (parsers, terminal state, etc.)
  - Property-based tests for correctness properties using FsCheck
- **caTTY.ImGui.Tests**: Tests the ImGui integration layer
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