# Build and Test — Details

## KSA paths (Directory.Build.props)

`KSAFolder` — the KSA reference assemblies (KSA.dll, Brutal.\*.dll, Planet.\*.dll) the projects
compile against — resolves in order, first match wins, every tier host-OS-agnostic:
1. `KSA_DLL_DIR` env var (or `-p:KSA_DLL_DIR=...`) — what CI uses
2. a `ksa-game-assemblies` checkout cloned **next to this repo** (DLLs in its `current/dll/`) — clone-and-go
3. per-OS defaults (Windows game install; `~/repos/meow-sci/ksa-game-assemblies/current/dll/` otherwise)

The deploy destination (`SelectedDistModDir`, where GameMod's `CopyCustomContent` writes the
`purrTTY/` mod folder) honors `PURRTTY_DIST_DIR` the same way; otherwise it defaults to the
per-OS KSA mods dir.

**Every build of `purrTTY.GameMod` deploys.** `CopyCustomContent` runs after each build of that
project — and a solution build or a `dotnet test` of the solution builds it — and it first
**deletes** the destination `purrTTY/` folder. Without `PURRTTY_DIST_DIR` the destination is the
real mods folder of the game on this machine. Set `PURRTTY_DIST_DIR` (for example
`PURRTTY_DIST_DIR="$PWD/dist/"`) for every build that is not meant as a deployment.

`KSA_DLL_DIR` is read twice: by MSBuild at build time (above), and at **run time** by the kFS
tests and by `purr-kfs`, which load the game's assemblies themselves (see
[kFS test projects](#kfs-test-projects-purrttykfstesting-purrttykfstests-purrttykfstoolstests)).
The kFS test assemblies also remember the `KSAFolder` they were built with, so `dotnet test` works
without the variable when the build found the assemblies; `purr-kfs` has no such fallback and
needs `KSA_DLL_DIR` or its `--ksa <dir>` option.

A missing/misresolved KSA assemblies dir fails fast with one actionable MSBuild error
(`ValidateKSAAssemblies` in `Directory.Build.props`; projects with KSA refs set
`RequiresKSAAssemblies=true`) instead of a CS0246 avalanche.

## KSA compatibility baseline

Reviewed on **2026-11-25** with the `upgrade-ksa` skill:

- Last validated baseline: **2026.11.16.5901**, assemblies commit `8d2ad13`.
- Target: **2026.11.25.5949**, snapshot commit `618c2a1` (snapshot date 2026-11-25).
  The supplied previous snapshot was the validated 5901 build, and the changelogs chain (5901 → 5949).
- **Verdict: one mechanical fix, now implemented; no KSA API change required one.** The build
  compiled cleanly before any change.
  - KSA rev 5916 can restart the game (`Process.Start` + `Environment.Exit`) when a star-system
    switch would not fit in VRAM. That exit skips `StarMapUnload`.
  - The Unix pty master is now close-on-exec, so shells still get their hangup instead of being
    orphaned (gotcha 16).
- Debug and Release solution builds: **0 warnings, 0 errors**. Full NUnit suite in each
  configuration on macOS arm64: **383 passed, 2 platform skips, 0 failed**. All **six
  Harmony patches** applied offline. Release-policy checks: **10 passed**.
- Builds were directed to the workspace's `dist/` with `PURRTTY_DIST_DIR`; no live game
  installation was deployed to. No live GPU session, system switch or StarMap load/unload was
  exercised.
- The numbers above are those of the review. The kFS work (the `purrTTY.Kfs*` projects and
  `purrTTY.GameMod/Kfs/`) was added afterwards against the same build. Its coupling to the game —
  six more Harmony targets (one of them a fallback that is normally not applied), four reflected
  non-public members and several behavioural assumptions, listed in section 9 of the
  `upgrade-ksa` skill (`.agents/skills/upgrade-ksa/SKILL.md`) — was audited against the decompiled
  5949 sources while it was written and is exercised by the offline kFS tests, but **it has not
  been through a compatibility review and none of it has run in the game.**

See [the full per-touchpoint review](ksa-5949-compatibility.md) for source references,
render-order/input/identity checks and remaining in-game smoke checks; the
[5801 review](ksa-5801-compatibility.md) documents the patch-probe methodology.

To revalidate using supplied assembly snapshots without deploying to the default game directory:

```bash
KSA_DLL_DIR="/path/to/ksa-game-assemblies/current/dll" PURRTTY_DIST_DIR="$PWD/dist/" dotnet build purrtty.slnx
KSA_DLL_DIR="/path/to/ksa-game-assemblies/current/dll" PURRTTY_DIST_DIR="$PWD/dist/" dotnet test purrtty.slnx --nologo -v quiet
```

## CI / releases (`.github/workflows/release.yml`)

Three jobs. A **test matrix** runs `dotnet test purrtty.slnx -c Release` on ubuntu-latest,
windows-latest, and macos-latest — each runner loads **its own** vendored native libghostty-vt
(linux-x64 / win-x64 / osx-arm64), so `RawCellLayout.Validate()` and the per-OS PTY backends get
real coverage on every platform after a pin bump. The test and build jobs check out
`meow-sci/ksa-game-assemblies` (private repo holding the KSA DLLs under `current/dll/`; access
via the `KSA_GAME_ASSEMBLIES_PAT` secret — a read-only fine-grained PAT, same pattern as flexo).
TRX results are published as per-OS check runs (`dorny/test-reporter`, pinned to the v3 commit
SHA because it holds `checks: write`) and uploaded as `test-results-<os>` artifacts.
Runner labels follow GitHub's supported stable images; `macos-latest` uses ARM64 to match
the vendored `osx-arm64` library. The workflow uses `actions/checkout@v7`,
`actions/setup-dotnet@v6` (installing .NET 10), and `actions/upload-artifact@v7`.

A **build job** (ubuntu, `needs: test`) verifies the release policy with
`python3 scripts/check-release-policy.py`, computes metadata, and builds the Release dist
via `PURRTTY_DIST_DIR`. Publishing branches also stamp `mod.toml`, zip `purrTTY/`, and
publish a GitHub release. This follows Unscience's Borea-compatible versioning policy:

- `main` → stable version `1.${GITHUB_RUN_NUMBER}.0`, tag `v<version>`, title
  `purrTTY <version>`, asset `purrTTY-<version>.zip`, and matching packaged `mod.toml` version.
- `feature/**` (including nested branches) → the same naming with a `-beta` suffix;
  marked prerelease and excluded from GitHub's Latest designation.
- `release/**`, `fix/**`, `chore/**`, and PRs into `main` → tests and build only,
  without version stamping or publication. Other branches do not trigger push builds.
- Manual runs publish only when targeting `main` or `feature/**` branches. Tag refs never publish.

`scripts/release-metadata.sh` accepts only branch pushes and manual branch runs for
publication. It validates the run number as a positive integer; branch names only choose
the channel and are never included in release names or shell commands. Major stays at `1`,
patch at `0`, and the existing workflow run counter continues across all branches (gaps are
expected). Later steps consume metadata via `env:`, never raw shell interpolation.

`scripts/publish-release.sh` targets the tested commit, includes generated notes and the
installation warning, and preserves an existing release on reruns. Reruns keep their run
number and version; publication never deletes and recreates a release. A newer run
supersedes an older run on the same ref.

A **prune job** runs after a successful publishing build. Cleanup is serialized per
channel without cancelling an in-progress cleanup for another branch. It fetches every
page of GitHub releases and uses `scripts/release-retention.py` to select stale tags:

- Stable: keep the newest 10 published automated stable releases, counting existing
  `v1.0.0`, `v1.0.1`, and `v1.1.0` toward that limit. These legacy releases eventually expire.
- Beta: keep the newest five published beta releases across all feature branches.
- Order by publication time, then release ID, rather than the target commit's age.
  Ignore drafts and unrelated release formats. Delete each stale release and its tag.
  Legacy tips have already been removed; orphan tags are not cleaned up.

Run `python3 scripts/check-release-policy.py` locally to verify metadata, mocked publishing,
and retention without contacting GitHub. The suite covers pagination, channel isolation,
legacy stable migration, publication ordering, and rerun behavior.

## Testing

The terminal-emulation behavior is **trusted to libghostty-vt** and is not re-tested. The tests
cover purrtty's **integration** with the engine.

`purrTTY.Terminal.Tests` (NUnit) validates frame production, theming, selection, the OSC
sidecar (incl. split-chunk/ST framing, the clipboard-query path, oversize-discard, and CAN
abort), key/mouse encoding, bracketed paste, DSR replies, scrollback, the DEC 2026 hold +
safety timeout, the session-wiring data flow and `SessionManager` lifecycle
(configurator-before-publication, restart reuse — driven through a registered headless custom
shell), the `CustomShellPtyBridge` adapter contract, and the PTY input contracts
(`PtyInputContractTests`: Windows argv quoting, Unix discrete-argv + `Auto` resolution, and
`PtyInputQueue` ordering/overflow/failure/stuck-writer semantics — pure logic, runs on every
host OS). `UnixProcessManagerTests` exercises the POSIX pty backend against real `/bin/sh`
children (output, exit codes, pty echo, initial winsize, working directory, shell detection);
it runs on macOS (dev) and linux-x64 (CI) and self-skips on Windows — this is the pre-player
coverage for Linux shell launching. `ConPtyProcessManagerTests` is its Windows twin (real
cmd.exe under ConPTY; pins the unified fast-exit contract — spawn-then-die reports once via
`ProcessExited` with the real code, never as a start failure — and the dying shell's tail
output being flushed); it runs on the windows-latest CI leg and self-skips elsewhere. Both twins
also pin the resize contract (a no-op before start, after exit and after stop; never throwing
while racing an exit; `ObjectDisposedException` after dispose — gotcha 40) and
`ProcessLaunchOptions.RemovedEnvironmentVariables`; the Windows halves of these have run on CI
only. `SessionExitTests` covers session-level exit handling against real child processes on every
OS (one test body, the command chosen per OS): `CloseOnExit` and `CloseOnSuccessfulExit` through
`SessionManager.CloseExitedSessions`, a session that stays with its final screen and survives
resize, input and close, a hidden session, and a session that fails to start. `purrTTY.CustomShellContract.Tests` +
`purrTTY.CustomShells.Tests` cover the custom-shell layer (incl. the Brutal-version-sensitive
`OnConsolePrint` capture, exercised against the real `ImColor8`/`ConsoleWindow` color fields).
`purrTTY.Display.Tests` covers the pure-logic display pieces headlessly: theme TOML round-trip,
all 18 bundled schemes parse, theme-override clamping, `AtomicFile`, the layout TOML format
(including the `close_on_exit` / `close_on_successful_exit` keys, written only when set), the
window-capture rule for a window whose session is still starting, and `SessionExitPolicy`.

### kFS test projects (`purrTTY.Kfs.Testing`, `purrTTY.Kfs.Tests`, `purrTTY.Kfs.Tools.Tests`)

Two NUnit projects test the kFS work, on one support library:

- `purrTTY.Kfs.Testing` — the offline runtime (`KfsRuntime`, `SimVehicle`, the simulated parts and
  telemetry, `ProjectBuilder`, `SimConsoleHost`, `SimHostSource`) and `KsaAssemblyResolver`. Not a
  test project and never shipped; `purr-kfs` uses it too.
- `purrTTY.Kfs.Tests` — the extension itself (`purrTTY.Kfs`): catalogue, table augmentation,
  header, installer, Harmony host hooks, router, the main-thread pump, the `purr` command, the
  example programs, the game back end's rules and light overrides, the Neovim launch plan.
- `purrTTY.Kfs.Tools.Tests` — the tools built on it: `purr-kfs` (`purrTTY.Kfs.Cli`), the project
  resolver, the language server (`purrTTY.Kfs.Lsp`) and the Neovim runtime files
  (`purrTTY.GameMod/nvim/`).

Both test projects run the game's **real** kFS (tables, kC compiler, kASM assembler, VM, host)
together with purrTTY's real code in the test process — no game. **This is all the verification
the kFS work has: nothing of it has run in the game.** The tests prove behaviour against the game's
own toolchain and a simulated vehicle; they cannot prove what only a running game shows (that small
patched methods are not inlined, thread placement under load, the look of a light, a real `nvim` in
a real in-game terminal). Those checks are the plan's
[§10](../plans/KFS_EXTENSION_FOR_PARTS_PLAN.md#10-in-game-validation-checklist).

`KSA.dll` is referenced compile-only (`Private=false`) and is **never copied** to any output
(`ResolverConfigurationTests.No_Game_Assemblies_In_Outputs` checks this; when `PURRTTY_DIST_DIR` is
set it also checks that the mod folder there has `purrTTY.Kfs.dll`, no game assembly, and neither
`purrTTY.Kfs.Testing.dll` nor any test assembly). At run time `KsaAssemblyResolver`
(`purrTTY.Kfs.Testing`) resolves it from `KSA_DLL_DIR`, else from the `KSAFolder` the test assembly
was stamped with at build time (`[AssemblyMetadata("KSAFolder", …)]`). On arm64 it patches the PE
machine field of the IL-only, x64-marked `KSA.dll` **in memory** (`LoadFromStream`); on x64 it
loads the file as is (`LoadFromAssemblyPath`). The file on disk is never modified.

- The resolver is installed by a `[ModuleInitializer]` (`ModuleInit.cs` in each test project).
  NUnit reflects over the assembly before any of its code runs, so **game types may appear only
  in method bodies and helper-method signatures** — never as field/property types, base types or
  interfaces, test-method parameters, attribute arguments, iterator element types, or as game
  structs captured by lambdas / held in async methods. The full rule and the observed failures are
  in the comment at the top of each `ModuleInit.cs`.
- The rule reaches through purrTTY's own types: **a type declared in a test assembly cannot
  implement an interface whose members have game types in their signatures**, even when the
  interface itself is purrTTY's (reported by the authors of the language-server tests: NUnit's
  reflection fails on such a type; unlike a plain helper method, an interface implementation has
  its signatures resolved when the type is loaded). `ILiveProgram` (`purrTTY.Kfs.Lsp`) is one — its members return the
  game's `MachineSnapshot` and take lists of the game's `LogLine` — so a test fake implements the
  game-free `ILiveSource` instead, and `IKfsGameSide` (the in-game language host's view of the
  game) deliberately has no game type in any signature so that tests can fake it. When a new seam
  is meant to be faked in tests, keep game types out of its member signatures, or put the fake in
  `purrTTY.Kfs.Testing`.
- Breaking the rule can make NUnit fail to load the whole assembly; each project's `.runsettings`
  file sets `TreatNoTestsAsError`, so the run then exits non-zero ("No test is available in
  …purrTTY.Kfs.Tests.dll" at normal verbosity) instead of passing with zero tests.
- Fixtures that need the game derive from `GameFixture`: with no game directory configured they
  skip silently; with one configured but unusable they fail with the resolver's message.
- Types that must break the rule — anything holding game-typed fields, implementing a game
  interface or taking delegates over game structs — go in `purrTTY.Kfs.Testing` (`SimVehicle`,
  `FaultyVehicleParts`, `SimHostSource`, the telemetry converter). The kFS test count must only go
  up; a drop to zero means the assembly no longer loads.
- `purr-kfs` obeys the same constraint for a different reason: `Program.cs`, `CliApp.cs`,
  `CliOptions.cs` and `HelpText.cs` contain no game type, because the resolver is installed by
  `CliApp` only after the command line is parsed; game types live in `purrTTY.Kfs.Cli/Commands/`.

**Process-wide state and fixture layout.** Harmony patches, the game's `SystemHeaders` statics,
`KfsIntegration._tables` and `PartsRegistry` are process-wide, so both assemblies are
`[NonParallelizable]` (`AssemblyInfo.cs`) and the fixtures are split by namespace.

`purrTTY.Kfs.Tests`:

- `purrTTY.Kfs.Tests.Runtime` (`Runtime/`) — everything that runs programs. `RuntimeSetUp`
  (`[SetUpFixture]`) starts the process's one `KfsRuntime` through the real installer before the
  first fixture of the namespace and disposes it after the last, asserting the process is clean.
  Fixtures derive from `RuntimeFixture` (`Runtime`, `Vehicle(...)`); `Kc.Ticked(body, globals)`
  wraps a task body in a 10 Hz program whose body sees the tick number `k`.
- `purrTTY.Kfs.Tests.Installer` (`Installer/`) — install/uninstall/publication and the runtime's
  own lifetime (`InstallTests`), and the rule that `<purrtty.h>` stays registered while a compile
  may be reading the game's header table (`HeaderInUseTests`, gotcha 47); each test installs or
  starts what it needs and its tear-down asserts nothing is left (no patch, header, builtin,
  binding or `_tables`).
- `purrTTY.Kfs.Tests` (root) — catalogue, emitter, self-check, tables, compiler, resolver, and the
  game-free pieces (`KsaPartRulesTests`, `PartsListingTests`, `PurrConsoleTests`,
  `NeovimLaunchPlanTests`, `LightOverridesTests`); no runtime alive (`CatalogCompileTests`
  registers the header itself with `SystemHeaderScope`).

`purrTTY.Kfs.Tools.Tests`:

- `purrTTY.Kfs.Tools.Tests.Runtime` (`Runtime/`, `Runtime/Lsp/`, `Runtime/Nvim/`) — everything
  that compiles or runs kFS code. `ToolsRuntimeSetUp` owns the one `KfsRuntime`; fixtures derive
  from `ToolsRuntimeFixture`. `purr-kfs` commands run **in process** through `CliApp.Run` (helper
  `PurrKfs`), find that runtime through `KfsRuntime.Current` and leave it running.
- `Usage/`, `Projects/`, `Lsp/` — no game and no runtime: command-line usage and help, the project
  resolver over an in-memory workspace, JSON-RPC framing, the document store, position mapping,
  the main-thread queue, the output tee and the piece-level hardening tests.
- Workspaces are real temporary folders (`TempWorkspace`); no test reads or writes the player's
  kFS workspace or configuration folder.

**Snapshots.** `SnapshotTests` (`purrTTY.Kfs.Tests`) compares the emitted `purrtty.h`,
`purrtty.kinc`, service rows and enum entries with `purrTTY.Kfs.Tests/Snapshots/`.
`KasmSyntaxTests` (`purrTTY.Kfs.Tools.Tests/Runtime/Nvim/`) regenerates the shipped Vim syntax
file `purrTTY.GameMod/nvim/syntax/kasm.vim` from the game — the mnemonics of its opcode table and
the directive names its assembler recognises — and compares it with the checked-in file; the
generator is `purrTTY.Kfs.Tools.Tests/Nvim/KasmSyntax.cs`. After an intended catalogue change, or
after a game update that changes opcodes or directives, run the tests once with
`PURRTTY_UPDATE_SNAPSHOTS=1` (the files are rewritten **in the source folders** and the tests
pass), review the diff and commit it. `kasm.vim` is a checked-in file, not a build product: a
build never regenerates it.

**The simulator's frame order.** A `SimVehicle` frame runs **step → pump → dispatch**, the order
of the game (`Program.PrepareFrame`: `Universe.ApplyVehicleSolvers` commits the results of the
physics job that just finished, then `KfsIntegration.BeforeInputEvents` — where purrTTY's pump is
hooked — runs, then `Universe.ExecuteNextVehicleSolvers` starts the next job, which dispatches the
kFS computer before it updates the part modules):

1. **Step** — `SimVehicleParts.Step(dt)` advances the parts with the goals and switches as they
   were before this frame's pump, and commits their state.
2. **Pump** — `BindingPump.DrainAndApply` applies the commands the previous frame's ticks issued.
3. **Dispatch** — the ticks that are due run, all against the same part state.

So a command issued in frame N is applied by the pump of frame N+1 and moves a part in the step of
frame N+2. What a program reads back falls in two groups:

- **Live values** change as soon as the pump writes them and are visible to the dispatch of the
  same frame: a light's switch (`X_LIGHT_ON`), a light's appearance, an animation's goal
  (`X_DEPLOY_TARGET`, `X_ACTUATOR_TARGET`), and `MOVING` (which compares the committed position
  with the live goal).
- **Committed values** come from the last physics step and are visible one frame later:
  `POSITION` and `X_DEPLOY_STATE`.
- `X_LIGHT_LIT` is mixed: lit means the live switch is on **and** the committed power state says
  powered. So switching a light **on** reads lit one frame later, switching it **off** reads dark
  at once, and a light without a switch always reads lit.

At time warp one frame runs several ticks, and all of them read the same values. Both rules are
pinned by `Runtime/FrameOrderTests.cs`. (Before this work the simulator ran pump → step →
dispatch, which showed committed values one frame too early; tests written against that order
were moved by one frame.) A paused frame runs the pump only.

**Writing a test with `KfsRuntime` / `SimVehicle`.** Everything runs in simulated time; `Frame`,
`Run`, `RunTicks(n)` and `RunUntil(condition, timeoutSeconds)` never wait on a clock. Script what
the game publishes with `v.Telemetry` / `v.TelemetryScript` (`SimTelemetry`), read program globals
with `v.ReadGlobal<T>("err.x")`, write a 4-byte global as the game's `kfs poke` does with
`v.WriteGlobal<T>(path, value)`, assert stock requests as text with `v.RequestText` and part
effects on `v.Parts`. `LoadKc(text)` builds one kC file; `LoadKasm(dictionary)` assembles a
folder project given as file name → text. An excerpt of `HarnessTests.RunUntil_Uses_Simulated_Time`:

```csharp
namespace purrTTY.Kfs.Tests.Runtime;

[TestFixture]
public sealed class HarnessTests : RuntimeFixture
{
    [Test]
    public void RunUntil_Uses_Simulated_Time()
    {
        using SimVehicle v = Runtime.Vehicle(deployables: 1).LoadKc(Kc.Ticked("if (k == 5u) { x_deploy_set(0u, 1u); }"));

        double took = v.RunUntil(() => v.Parts.Deployable(0).State == DeployState.Deployed, timeoutSeconds: 10, frameDt: 0.1);

        // Issued in tick 5 (0.5 s), applied by the 0.6 s frame's pump, moving from the 0.7 s frame's
        // step (the game's next physics job), 2 s of travel: deployed at 2.6 s.
        Assert.That(took, Is.EqualTo(2.6).Within(1e-9));
    }
}
```

A failed build throws `KfsBuildException` (message and `Diagnostics` carry the compiler's
`file:line:col` lines). The harness does not fly anything: stock steering, staging and burns are
recorded, not simulated (see the `SimVehicle` and `SimTelemetry` XML docs for exactly what is
scriptable and observable). A test that needs the `purr` command uses `SimConsoleHost` (an
in-memory console and workspace; pass its own writer, because offline `KfsIntegration.Print` goes
to the game log); a test of the main-thread pump uses `SimHostSource` (a list of simulated hosts
standing in for the game's `KfsVehicleHost.AllHosts`).

**Language-server tests** (`purrTTY.Kfs.Tools.Tests`). Three levels, each with its helper:

- One server over in-memory pipes: `Lsp/ServerHarness.cs` runs a `KfsLanguageServer` on its own
  thread with an `LspTestClient` on the other end. It sets the build scheduler's debounce to
  infinite and builds only on `Flush`, so no test waits for a timer. `Runtime/Lsp/FeatureSession.cs`
  adds a workspace and the standard features for the hover and completion tests (`HoverTests`,
  `CompletionTests`, `FeatureRobustnessTests`); `DiagnosticsTests`, `SchedulerTests`,
  `LifecycleTests` and `ServerProtocolTests` cover builds, the watchdog, the lifecycle and the
  protocol.
- The in-game host without the game: `InGameHostTests`, `HostCommandTests` and `LiveFeedTests`
  drive `InGameLanguageHost`, the `kfs.*` commands and the live feeds over a **fake game side**
  (`IKfsGameSide`, `ILiveSource`); the test thread plays the game's main thread by calling
  `Frame()`, and the live feed runs without its timer (the test calls `LiveFeed.Poll` and moves a
  fake clock). `ServiceCoreTests` runs the service core (token, status line, server info file, port
  in use, restart) over real loopback sockets with temporary folders.
- Hardening, with **real sockets and real processes**: `Runtime/Lsp/HardeningTests.cs` attacks a
  listener on a loopback port (peers that reset, never authenticate, send oversized messages or
  never read; an HTTP request; a persistent accept error; a stop with a compile in flight) and ends
  every test with `LspThreads` exactly back at its baseline; `CliLspHardeningTests` starts
  `purr-kfs lsp` as a child process to prove it ends on SIGTERM / Ctrl+C while its stdin stays
  open, and that `--listen` never runs without a token. `Lsp/HardeningUnitTests.cs` covers the same
  findings at the level of the pieces, without the game.

**Neovim tests** (`Runtime/Nvim/NeovimTests.cs`, `InGameNeovimTests.cs`). The shipped runtime files
run in a **real headless `nvim`**, started exactly as the launcher plans it (`NeovimLaunchPlan`:
the `--cmd` entry, the environment, the working directory) against real language servers on
loopback port 0 — a scripted one (`Nvim/FakeKfsServer.cs`) or the stack the mod runs
(`Nvim/InGameStack.cs`: the in-game host over a fake game side, the live feed, the output tee, the
service core with its token and info file). The Lua drivers are in `Nvim/lua/`; they and the
runtime files are copied to the test output by the project file.

- **Hermetic.** Every Neovim gets its own temporary `HOME` / `USERPROFILE`, its own four XDG
  directories, a `bin` folder that is the whole `PATH` besides the system folders, and a copy of
  the runtime files in a folder whose path contains spaces (`NvimSandbox` in `Nvim/Neovim.cs`). The
  developer's Neovim configuration is neither read nor written.
- **Silent skip.** Without an `nvim` 0.11 or newer on `PATH` the Neovim tests skip silently
  (`Neovim.RequireOrIgnore`). The CI workflow does not install Neovim, so on CI these tests run
  only if the runner image happens to have a new enough one; treat them as developer-machine
  coverage. `KasmSyntaxTests` does not need Neovim and always runs.
- **What was run.** The runtime files check for Neovim 0.11, but the only version they have been
  run on is 0.12.6.

**Running `purr-kfs` from the build output.** The tool is the assembly `purr-kfs.dll` of
`purrTTY.Kfs.Cli` (the project is also packable as a .NET tool, command name `purr-kfs`; no
package is published). After a build, from the repository root:

```bash
export KSA_DLL_DIR="/path/to/ksa-game-assemblies/current/dll"   # or pass --ksa <dir> to each command
dotnet purrTTY.Kfs.Cli/bin/Debug/net10.0/purr-kfs.dll --help
dotnet purrTTY.Kfs.Cli/bin/Debug/net10.0/purr-kfs.dll check x_blink/blink.c --workspace /path/to/kfs
```

`header`, `kinc` and `services` need no game assemblies; `check`, `run` and `lsp` do. Exit codes:
0 success, 1 build errors or a trapped program, 2 a usage or environment problem. The commands are
described in [how-to.md](how-to.md#check-or-run-a-kfs-program-outside-the-game-with-purr-kfs).

### Tests must be quiet by default (MUST)

A test that passes or skips must produce **zero output** — no stdout/stderr, no recorded test
messages. Test-run output is read by CLIs, CI logs, and AI assistants; per-test chatter bloats
that context on every run while carrying no information (a 2026-06 pass removed all of it).
Concretely:

- **Never `Assert.Pass("message")`.** It records the message as test output on every run. For
  "does not crash" smoke tests, assert the real postcondition (state unchanged, all tasks
  completed) or wrap the act in `Assert.DoesNotThrow(...)` — a test that runs to completion
  passes without any `Assert.Pass`.
- **Skip silently: `Assert.Ignore()` with no message,** with the reason as a code comment at the
  ignore site (the recorded message prints on every skipped run — e.g. the Windows CI runner
  printed the POSIX-skip reason on all of `UnixProcessManagerTests`). The skipped test's *name*
  still appears in results; that plus the comment is enough to diagnose.
- **No `Console.Write*`/`TestContext.WriteLine`/`TestContext.Progress` on success paths.**
  Diagnostic detail belongs in the assertion *failure message* (the optional last argument of
  `Assert.That`), which is printed only when the assertion fails — that is the one place to be
  generous with context (include the observed state, like `WaitForOutput`'s "Output so far").

### No fixed sleeps in tests (MUST)

Never use a bare `Task.Delay`/`Thread.Sleep` to "give async work time to finish" before
asserting. A 2026-06 cleanup removed ~400 such sleeps (≈26 s of pure dead time per run — the
custom-shell suites went from ~31 s to <0.5 s) with zero assertion changes; do not reintroduce
the pattern. It is both slow (the sleep always runs in full) and flaky (a loaded CI runner can
need longer than the guess). Synchronize on the actual completion signal instead:

- **Know what is already synchronous.** `BaseLineBufferedShell.WriteInputAsync` processes input
  fully before returning — line buffer, cursor, history, and command execution are assertable
  immediately, no wait at all. Only **output delivery** is async (queued to a channel, pumped to
  `OutputReceived` on a background task). `BaseChannelOutputShell.StopAsync` drains that pump
  before returning, so post-stop output/`Terminated` assertions also need no wait.
- **Channel-output shells: flush with a sentinel.** The test-shell subclasses in
  `BaseLineBufferedShellTests` and `GameConsoleShellTests` have `FlushOutputAsync()`: queue a
  unique zero-length sentinel via `QueueOutput` and await its `OutputReceived` event — the pump
  is single-reader FIFO, so the sentinel arriving proves every earlier output was delivered.
  Reuse/copy it for new channel-shell fixtures. Two rules: collectors must skip zero-length
  events (so the sentinel can't pollute captured output/`LastOutputType`), and never flush after
  `StopAsync` (channel completed — the sentinel would never arrive; no wait is needed there anyway).
- **Waiting for an event or a count:** block on the signal with a generous timeout —
  `ManualResetEventSlim`/`CountdownEvent`/`TaskCompletionSource` + `Wait(timeout)`/`WaitAsync`
  (see `BaseChannelOutputShellTests`), or a bounded poll-until-condition loop
  (`WaitForCapturedCountAsync` in `BaseChannelOutputShellTypedOutputTests`). The timeout is a
  failure bound, not a wait: tests pass as fast as the code runs and only burn time when broken.
- **Legitimate fixed delays** are only the polling *interval inside* a deadline-bounded
  condition loop — required when no completion signal exists, e.g. waiting on a real external
  process (`UnixProcessManagerTests.Harness.WaitForOutput` polls every 25 ms against live
  `/bin/sh` output, 10 s deadline) — or a test whose *subject* is timeout behavior itself
  (`StopAsync_WithTimeout_CancelsPumpIfNotDrained`). If you believe a new fixed sleep is
  justified, document the reason in a comment at the sleep site.

## Building the native libghostty-vt

P/Invoke needs the **shared library** (`.dylib`/`.dll`/`.so`). Prebuilt binaries for all three
RIDs are **checked in** at `vendor/Ghostty.Vt/native/{osx-arm64,win-x64,linux-x64}/`; the
`Ghostty.Vt` csproj copies all of them flat into every build output (the filenames are
platform-distinct) and `NativeLibraryResolver` loads the one matching the running OS.

Rebuilding is only needed on a ghostty pin bump. All three targets cross-compile from a single
host with zig 0.15 — full commands, strip step, and gotchas live in
`vendor/Ghostty.Vt/README.md`. The short version:

```bash
export PATH="/opt/homebrew/opt/zig@0.15/bin:$PATH"     # zig 0.15 on this machine
cd /path/to/ghostty                                    # at the pinned commit
zig build -Demit-lib-vt -Dtarget=aarch64-macos -Doptimize=ReleaseFast         # → zig-out/lib/libghostty-vt.dylib (symlink: cp -L)
zig build -Demit-lib-vt -Dtarget=x86_64-windows-gnu -Doptimize=ReleaseFast    # → zig-out/bin/ghostty-vt.dll
zig build -Demit-lib-vt -Dtarget=x86_64-linux-gnu.2.31 -Doptimize=ReleaseFast # → zig-out/lib/libghostty-vt.so.0.1.0 (llvm-strip --strip-debug)
```

Gotchas: never `-mcpu native` (non-portable; was observed to AV inside `vt_write` — the SIMD deps
runtime-dispatch to AVX2 etc. anyway); Windows must use the `gnu` ABI (msvc fails to compile the
highway/simdutf C++ deps); vendor **only** the shared library — never `ghostty-vt-static.lib` or
the import lib `ghostty-vt.lib` (not loadable at runtime); after a bump run the test suite
(`RawCellLayout.Validate()` fails loudly if the native cell layout changed). The native lib is
**pinned, not forked** (current pin recorded in `vendor/Ghostty.Vt/README.md`).
