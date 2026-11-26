# kFS extension spikes

Throw-away console programs that established, against the real `KSA.dll` of build
2026.11.25.5949 and **outside the game**, how kFS can be extended and tested. They are evidence for
`plans/KFS_EXTENSION_FOR_PARTS_PLAN.md` and are written up, with their output, in
`docs/kfs-info/modding/offline-harness.md`. They are not part of `purrtty.slnx`.

| Folder | What it shows |
| --- | --- |
| `01-tables-and-services` | The embedded tables load offline; `GameTables.Load` accepts extra service rows; a kC call needs a header prototype and a `SystemHeaders.Builtins` entry; an unhandled id is rejected with "no vehicle to act on"; two Harmony postfixes on `VehicleComputer` deliver the request to mod code and return a result. |
| `02-args-results-kasm` | `f32` and `vec3` arguments, argument-domain rejection, an `f32` result, and the kASM path (`SYSCALL SVC_…`). (A `vec3` *result* crashes the kC lowering — that case was removed from the file after it was observed.) |
| `03-loadembedded-patch` | A Harmony postfix on `GameTables.LoadEmbedded` reaches the game's lazy `KfsIntegration.Tables`. |
| `04-workbench-end-to-end` | No DLL copies: the snapshot assemblies are resolved at run time and `KSA.dll`'s PE machine field is patched in memory on arm64. The game's own `Workbench` builds a kC file from a workspace folder, loads and starts it on a `FakeVehicle`, and mod services are observed and answered. |
| `05-lsp-neovim` | A minimal language server over the real kC compiler (diagnostics, hover, completion; TCP and stdio), attached to a headless Neovim; position mapping, latency and concurrency measurements. |
| `06-service-catalog-and-sim` | The full planned API: 22 services and their enumerations generated from one catalogue (table rows, `purrtty.h`, `purrtty.kinc`), a router behind the two host hooks, simulated lights and animations, and a self-checking runner (76 checks). |

## Running

Spike 04 (and 05) only need the folder that holds `KSA.dll`:

```sh
cd 04-workbench-end-to-end
KSA_DLL_DIR=/path/to/ksa-game-assemblies/current/dll dotnet run
```

Spikes 01–03 use top-level statements that touch game types immediately, so they were built against
a scratch copy of the DLLs: see the comment in `lib-copy.csproj.txt` and `flip-pe.py`.

Nothing here was run inside the game.
