# caTTY Terminal Emulator Game Mod

A terminal emulator mod for Kitten Space Agency (KSA) that provides full VT100/xterm-compatible terminal emulation
within the game.

## Features

- **Full Terminal Emulation**: Complete VT100/xterm compatibility with escape sequence support
- **Real Shell Integration**: Connects to actual shell processes (PowerShell, cmd, etc.)
- **ImGui Integration**: Native ImGui rendering within the KSA game engine
- **Keybind Toggle**: Press F12 to show/hide the terminal window
- **Resource Management**: Proper cleanup and lifecycle management

## Installation

### Prerequisites

- Kitten Space Agency game installed
- Windows 10 version 1809+ (required for ConPTY support)
- KSA game DLLs available at: `C:\Program Files\Kitten Space Agency\`

### Installation Steps

1. **Build the Mod** (if building from source):
   ```bash
   cd catty-ksa/caTTY.GameMod
   dotnet build
   ```

2. **Deploy the Mod**:
    - Copy all files from `catty-ksa/caTTY.GameMod/dist/` to your KSA mods directory
    - The mod files include:
        - `caTTY.GameMod.dll` - Main mod assembly
        - `caTTY.GameMod.deps.json` - Dependency information
        - `mod.toml` - Mod metadata

3. **Launch KSA**:
    - Start the game normally
    - The mod will initialize automatically when all mods are loaded

## Usage

### Basic Operation

1. **Toggle Terminal**: Press `F12` to show/hide the terminal window
2. **Shell Interaction**: Type commands and interact with the shell normally
3. **Focus Management**: Click in the terminal window to focus it for input
4. **Process Management**: The mod automatically manages shell processes

### Keyboard Shortcuts

- `F12` - Toggle terminal window visibility
- `Ctrl+C` - Send interrupt signal to shell
- `Ctrl+D` - Send EOF signal to shell
- `Ctrl+Z` - Send suspend signal to shell
- Arrow keys - Navigate command history and cursor
- `Enter` - Execute commands
- `Tab` - Tab completion (if supported by shell)

### Shell Selection

The mod automatically selects the best available shell:

1. PowerShell (Windows PowerShell) - Default on Windows
2. PowerShell Core (pwsh) - If available
3. Command Prompt (cmd) - Fallback option

## Technical Details

### Architecture

- **Headless Core**: Pure C# terminal emulation logic
- **ImGui Controller**: Game-specific display and input handling
- **Process Manager**: Windows ConPTY integration for real shell processes
- **Event-Driven**: Non-blocking architecture that doesn't interfere with game loop

### Dependencies

The mod depends on the following KSA game DLLs:

- `Brutal.Core.Common.dll`
- `Brutal.Core.Numerics.dll`
- `Brutal.ImGui.dll`
- `KSA.dll`

### Resource Management

- **Automatic Cleanup**: Resources are automatically disposed when the mod unloads
- **Process Termination**: Shell processes are properly terminated on mod unload
- **Memory Management**: Efficient memory usage with minimal garbage collection pressure
- **Error Handling**: Robust error handling prevents game crashes

## Troubleshooting

### Common Issues

**Terminal doesn't appear when pressing F12:**

- Ensure the mod loaded successfully (check game console for initialization messages)
- Verify all required KSA DLLs are present
- Check that Windows 10 version 1809+ is installed

**Shell process fails to start:**

- Verify PowerShell or cmd.exe is available on the system
- Check Windows ConPTY support (Windows 10 1809+)
- Review game console for error messages

**Input not working:**

- Click in the terminal window to focus it
- Ensure the terminal window is visible (press F12)
- Check that the shell process is running

### Debug Information

The mod logs important events to the game console:

- Initialization status
- Shell process start/stop events
- Error conditions
- Resource disposal

Look for messages prefixed with "caTTY GameMod:" in the game console.

## Development

### Building from Source

```bash
# Clone the repository
git clone <repository-url>
cd cat-ghostty

# Build the entire solution
dotnet build catty-ksa/caTTY-cs.sln

# Or build just the game mod
dotnet build catty-ksa/caTTY.GameMod/caTTY.GameMod.csproj
```

### Project Structure

```
caTTY.GameMod/
├── TerminalMod.cs          # Main mod implementation
├── caTTY.GameMod.csproj    # Project file with KSA references
├── mod.toml                # Mod metadata
├── dist/                   # Build output for deployment
└── README.md               # This file
```

### API Integration

The mod integrates with KSA using the StarMap.API framework:

- `[StarMapMod]` - Marks the main mod class
- `[StarMapAfterGui]` - Called after GUI rendering for terminal display
- `[StarMapAllModsLoaded]` - Called for initialization
- `[StarMapUnload]` - Called for cleanup

## License

This project is part of the caTTY terminal emulator implementation.

## Support

For issues and support:

1. Check the troubleshooting section above
2. Review game console logs for error messages
3. Ensure all prerequisites are met
4. Report issues with detailed error information
