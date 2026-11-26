# kFS tooling

The player- and developer-facing tools around kFS in KSA build 2026.11.25.5949: the workbench that
every command goes through, the on-disk workspace, the editor, the reference and debug windows, the
`kfs` console command, the (unreachable) command-line tools and the embedded example programs.

## Start here

- [kFS player quick start](/tooling/user-quick-start.md) — Step-by-step for a player — enable the kFS prototype, open the editor, create and build a program, load it onto the controlled vehicle, start it, watch its logs and traces, debug and stop it — with every step traced to the code that implements it.
- [kFS console commands](/tooling/console-commands.md) — Every verb of the in-game `kfs` console command — exact syntax, arguments, effect and output text — plus how the game console parses it, where the output goes, and how a mod can call, capture or extend it.

## Core objects

- [Workbench](/tooling/workbench.md) — The Workbench class is the facade behind most kfs console verbs (30 of 36) and the editor window, owning the workspace, the bench machine, the editor model and the choice of target (bench or vehicle).
- [kFS workspace and files](/tooling/workspace-and-files.md) — The on-disk kFS workspace under Documents/My Games/Kitten Space Agency/kfs — its folder structure, file extensions, generated headers, templates, naming rules, and exactly which files a build reads.
- [kFS build and load flow](/tooling/build-and-load-flow.md) — The exact call chain from a source file to a running program — project discovery, compile or assemble, image validation, loading onto the bench or a vehicle, start, stop and reset — with where diagnostics go and how layout and part mismatches are handled.

## Windows

- [kFS editor window](/tooling/editor-window.md) — KfsEditorWindow and its EditorModel — how the in-game kFS editor is opened, its menus, toolbar, hotkeys and eleven tabs, what each user action invokes, and what a mod can and cannot hook.
- [kFS reference window](/tooling/reference-window.md) — KfsReferenceWindow, the editor's built-in Opcodes and Services reference — its two pages, the exact text shown, how each column is derived from the game tables, and when a mod-added table row appears in it.
- [kFS debug window and dispatch profiler](/tooling/debug-window.md) — KfsDebugWindow (View > Debug > Show kFS Debug) and KfsDispatchProfiler — the on/off switch for the prototype, the per-tick timing tables, the dispatch flame graph, and where every number comes from.

## Command line and samples

- [kFS command-line tools (kvm-cli, kc-cli)](/tooling/cli.md) — The KSA.Kfs.Cli namespace — the kvm-cli and kc-cli entry points compiled into the game assembly, their arguments, files, exit codes and JSON outputs, and the finding that nothing in the shipped game calls them.
- [kFS embedded example programs](/tooling/examples.md) — Every kC and kASM example program embedded in the game (Examples.cs), reproduced verbatim with its name, menu placement and purpose.

## Extending

- [Extension points in the kFS tooling](/tooling/tooling-extension-points.md) — Where a StarMap/Harmony mod can hook the kFS user tooling — shipping examples and headers, driving build/load/start from code, capturing kFS and program output into its own UI, and making mod-added services visible in the reference window — with the visibility of every member involved.
