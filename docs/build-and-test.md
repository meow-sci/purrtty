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

A missing/misresolved KSA assemblies dir fails fast with one actionable MSBuild error
(`ValidateKSAAssemblies` in `Directory.Build.props`; projects with KSA refs set
`RequiresKSAAssemblies=true`) instead of a CS0246 avalanche.

## CI / releases (`.github/workflows/release.yml`)

Three jobs. A **test matrix** runs `dotnet test purrtty.slnx -c Release` on ubuntu-latest,
windows-latest, and macos-14 — each runner loads **its own** vendored native libghostty-vt
(linux-x64 / win-x64 / osx-arm64), so `RawCellLayout.Validate()` and the per-OS PTY backends get
real coverage on every platform after a pin bump. The test and build jobs check out
`meow-sci/ksa-game-assemblies` (private repo holding the KSA DLLs under `current/dll/`; access
via the `KSA_GAME_ASSEMBLIES_PAT` secret — a read-only fine-grained PAT, same pattern as flexo).
TRX results are published as per-OS check runs (`dorny/test-reporter`, pinned to the v3 commit
SHA because it holds `checks: write`) and uploaded as `test-results-<os>` artifacts.

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
output being flushed); it runs on the windows-latest CI leg and self-skips elsewhere. `purrTTY.CustomShellContract.Tests` +
`purrTTY.CustomShells.Tests` cover the custom-shell layer (incl. the Brutal-version-sensitive
`OnConsolePrint` capture, exercised against the real `ImColor8`/`ConsoleWindow` color fields).
`purrTTY.Display.Tests` covers the pure-logic display pieces headlessly: theme TOML round-trip,
all 18 bundled schemes parse, theme-override clamping, and `AtomicFile`.

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
