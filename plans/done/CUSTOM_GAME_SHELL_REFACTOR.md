* you MUST retain the SAME exact functionality as currently exist
* you MUST create new dotnet projects for better code demarcation of custom shell contracts and custom shell implementations.  the contract demarcation is important to expose it as a contract for third parties to adhere to as well.
    * Suggested projects:
        * caTTY.CustomShellContract
        * caTTY.CustomShellContract.Tests
        * caTTY.CustomShells
        * caTTY.CustomShells.Tests
* the existing GameConsoleShell MUST be refactored to the caTTY.CustomShells project
* the core functionality of GameConsoleShell MUST be lifted into abstracted base classes in caTTY.CustomShellContract
* the way the existing GameConsoleShell is setup which includes some Harmony patching MUST continue be the same.
        * Most custom shells WILL NOT need Harmony patching.  GameConsoleShell is unique because it's interfacing with a built-in game console bit of code, so this is a one-off implementation.  It's OK to keep it's one-off Harmony patching setup in caTTY.GameMod.
* you MUST ensure the same overall functionality remains the same
* you MUST ensure the way that custom shells data in/out of the stdout/stderr/stdin streaming pty interfaces remains the same
