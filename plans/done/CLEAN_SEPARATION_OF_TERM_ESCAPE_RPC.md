We have a custom RPC mechanism using custom terminal escape sequences (both CSI and OSC).

I want there to be as much of a clean separation of concerns as possible in the project structure.

I think that a new csproj "caTTY.TermSequenceRpc" should be added and all "handler" code and as much RPC related interface definitions and glue code moved here, leaving only the minimum required RPC related code in caTTY.Core.

I don't like the idea of caTTY.Core depending directly on "caTTY.TermSequenceRpc" since "caTTY.TermSequenceRpc" will need dependencies on the KSA game DLLs, and I would like to avoid caTTY.Core from needing direct references to the game DLLs (the intent is for caTTY.Core to contain the headless terminal emulator logic, not game specific logic)

Come up with a good solution to how the project arrangement should be implemented for RPC handoff and implement it.