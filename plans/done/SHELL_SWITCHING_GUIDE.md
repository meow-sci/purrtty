# Shell Switching Guide

This guide explains how to easily switch between different shells in caTTY terminal emulator.

## Default Shell

**WSL2 is now the default shell on Windows** for better terminal compatibility and Linux tooling support.

## Quick Shell Switching

### Method 1: Using ShellConfiguration Helper (Recommended)

The easiest way to switch shells is using the `ShellConfiguration` helper class:

```csharp
// Default (WSL2 on Windows)
var options = ShellConfiguration.Default();

// WSL2 configurations
var options = ShellConfiguration.Wsl();                    // Default WSL distribution
var options = ShellConfiguration.Wsl("Ubuntu");           // Specific distribution
var options = ShellConfiguration.Wsl("Ubuntu", "/home/username"); // With working directory

// Windows shells
var options = ShellConfiguration.PowerShell();            // Windows PowerShell
var options = ShellConfiguration.PowerShellCore();        // PowerShell Core (pwsh)
var options = ShellConfiguration.Cmd();                   // Command Prompt

// Common pre-configured shells
var options = ShellConfiguration.Common.Ubuntu;           // Ubuntu WSL2
var options = ShellConfiguration.Common.Debian;           // Debian WSL2
var options = ShellConfiguration.Common.GitBash;          // Git Bash
var options = ShellConfiguration.Common.Msys2Bash;        // MSYS2 Bash

// Custom shell
var options = ShellConfiguration.Custom(@"C:\custom\shell.exe", "--arg1", "--arg2");
```

### Method 2: Using ProcessLaunchOptions Factory Methods

```csharp
// Default (WSL2 on Windows)
var options = ProcessLaunchOptions.CreateDefault();

// WSL2 configurations
var options = ProcessLaunchOptions.CreateWsl();
var options = ProcessLaunchOptions.CreateWsl("Ubuntu");
var options = ProcessLaunchOptions.CreateWsl("Ubuntu", "/home/username");

// Windows shells
var options = ProcessLaunchOptions.CreatePowerShell();
var options = ProcessLaunchOptions.CreatePowerShellCore();
var options = ProcessLaunchOptions.CreateCmd();

// Custom shell
var options = ProcessLaunchOptions.CreateCustom(@"C:\path\to\shell.exe", "arg1", "arg2");
```

## How to Switch Shells

### In TestApp (caTTY.TestApp/TerminalTestApp.cs)

1. Open `catty-ksa/caTTY.TestApp/TerminalTestApp.cs`
2. Find the shell configuration section (around line 60)
3. Comment out the current option and uncomment your preferred shell
4. Build and run: `dotnet run` from the `caTTY.TestApp` directory

### In GameMod (caTTY.GameMod/TerminalMod.cs)

1. Open `catty-ksa/caTTY.GameMod/TerminalMod.cs`
2. Find the shell configuration section in the `InitializeTerminal()` method
3. Comment out the current option and uncomment your preferred shell
4. Build the mod: `dotnet build --configuration Release`
5. Copy the DLL to your KSA mods folder

## Shell Requirements

### WSL2 (Default)
- **Requirement**: Windows 10 version 1903+ or Windows 11
- **Installation**: Install from Microsoft Store or enable Windows feature
- **Benefits**: Full Linux environment, better terminal compatibility, access to Linux tools

### PowerShell
- **Requirement**: Built into Windows
- **Benefits**: Windows-native, good for Windows administration

### PowerShell Core (pwsh)
- **Requirement**: Separate installation required
- **Benefits**: Cross-platform, modern PowerShell features

### Command Prompt
- **Requirement**: Built into Windows
- **Benefits**: Simple, lightweight, Windows-native

### Custom Shells
- **Examples**: Git Bash, MSYS2, Cygwin, custom terminals
- **Requirement**: Must be installed separately
- **Benefits**: Specialized environments and toolchains

## Troubleshooting

### WSL2 Not Found
If you get "WSL not found" errors:
1. Install WSL2: `wsl --install` in an admin PowerShell
2. Or install from Microsoft Store: search for "Ubuntu" or "Debian"
3. Or enable Windows feature: "Windows Subsystem for Linux"

### PowerShell Core Not Found
If you get "pwsh not found" errors:
1. Install PowerShell Core from: https://github.com/PowerShell/PowerShell/releases
2. Or use Windows Package Manager: `winget install Microsoft.PowerShell`

### Custom Shell Not Found
If you get custom shell errors:
1. Verify the shell path exists
2. Check that the executable has proper permissions
3. Ensure any required dependencies are installed

## Examples

### Switch to Ubuntu WSL2
```csharp
var options = ShellConfiguration.Common.Ubuntu;
```

### Switch to PowerShell with custom arguments
```csharp
var options = ShellConfiguration.PowerShell();
options.Arguments.Add("-ExecutionPolicy");
options.Arguments.Add("Bypass");
```

### Switch to Git Bash
```csharp
var options = ShellConfiguration.Common.GitBash;
```

### Switch to custom shell with working directory
```csharp
var options = ShellConfiguration.Custom(@"C:\tools\shell.exe", "--login");
options.WorkingDirectory = @"C:\projects";
```

## Why WSL2 as Default?

WSL2 provides several advantages for terminal emulation:
- **Better compatibility**: Full Linux environment with proper terminal support
- **Rich tooling**: Access to Linux command-line tools and package managers
- **Modern shell**: Bash/Zsh with better escape sequence support
- **Development friendly**: Better support for development tools and workflows
- **Unicode support**: Proper UTF-8 and wide character handling

You can always switch back to PowerShell or Command Prompt if needed using the methods above.