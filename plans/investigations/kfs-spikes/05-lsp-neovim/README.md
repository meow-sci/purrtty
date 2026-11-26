# Spike 05 — a kC language server over the real kFS compiler, attached to headless Neovim

Throw-away feasibility code. It loads the unmodified `KSA.dll` (build 2026.11.25.5949) **outside the
game** exactly as spike 04 does (an `AssemblyLoadContext.Default.Resolving` handler that patches the PE
machine field in memory on arm64), installs two demo services plus a `<purrtty.h>` header, and serves
LSP over **TCP** (`--listen`) and **stdio** (`--stdio`). Neovim 0.12.6 attaches headlessly, receives
diagnostics, and asks for hover and completion. Nothing here was run inside the game.

## Run

```sh
KSA_DLL_DIR=/path/to/ksa-game-assemblies/current/dll ./run.sh
```

Needs `dotnet` (SDK that can target `net10.0`) and `nvim` ≥ 0.12 on `PATH`. `run.sh` builds the server,
runs the non-LSP compiler checks, starts `--listen 127.0.0.1:0`, runs `nvim/attach_tcp.lua`
(twice, as two Neovim processes against one server), runs `nvim/attach_stdio.lua` with the server
forced to `utf-8` and then `utf-32` positions, and finally `nvim/probe_connect.lua`. Neovim runs with
`--clean` and with `XDG_STATE_HOME`/`XDG_CACHE_HOME`/`XDG_DATA_HOME` under `out/` (Neovim writes
`lsp.log` there; the sandbox refused `~/.local/state`).

| Path | What |
| --- | --- |
| `KfsLspSpike/Program.cs` | entry, in-memory resolver, `--stdio` / `--listen` transports (one thread per TCP client) |
| `KfsLspSpike/Kfs.cs` | Harmony postfix on `GameTables.LoadEmbedded` (adds `0x5010`, `0x5012`, and a spike-only `0x5014`), `<purrtty.h>`, `Compile()` that never throws |
| `KfsLspSpike/Session.cs` | hand-rolled JSON-RPC (`Content-Length`), document store, compile-on-worker with supersession, position mapping, hover, completion |
| `KfsLspSpike/SelfTests.cs` | `--bench`, `--stress [--cold]`, `--nasty` (no LSP) |
| `nvim/common.lua` | filetype rule, `FileType kc` autocommand → `vim.lsp.start`, reporting, hover/completion/rapid-edit checks |
| `nvim/attach_tcp.lua`, `nvim/attach_stdio.lua` | the two transports |
| `nvim/probe_connect.lua` | `vim.lsp.rpc.connect` semantics: nothing listening, per-root clients, server death, restart |
| `samples/` | `ok.c`, `error.c`, `unicode.c`, `header_error.c`, `nasty/*.c`, `examples/*.c` (extracted verbatim from `docs/kfs-info/tooling/examples.md`) |

## The Neovim side (0.12 API used)

`vim.lsp.start(config, { bufnr })` inside a `FileType` autocommand; TCP uses
`cmd = vim.lsp.rpc.connect('127.0.0.1', port)`, stdio uses `cmd = { 'dotnet', dll, '--stdio' }`.
kC files are `.c`, so a path rule gives them their own filetype and clangd (filetypes `c`/`cpp`) never
sees them:

```lua
vim.filetype.add({ pattern = {
  ['.*/My Games/Kitten Space Agency/kfs/.*%.[ch]'] = 'kc',   -- the game's workspace
  ['.*/05%-lsp%-neovim/samples/.*%.c'] = 'kc',               -- this spike
}})
vim.api.nvim_create_autocmd('FileType', { pattern = 'kc', callback = function(ev)
  vim.lsp.start({ name = 'kfs', cmd = vim.lsp.rpc.connect('127.0.0.1', port),
                  root_dir = vim.fs.dirname(vim.fs.abspath(ev.file)) }, { bufnr = ev.buf })
end })
```

Every report line shows `"filetype":"kc"` and `"clients":["kfs"]` (the pattern rule wins over the `.c`
extension). The scripts also set `flags.debounce_text_changes = 0` to provoke overlapping compiles.
(`vim.lsp.config('kfs', { cmd = …, filetypes = { 'kc' } })` + `vim.lsp.enable('kfs')` is the equivalent
declarative form for a user config; not exercised here.)

## Observed output (`./run.sh`, 2026-11-25, macOS arm64, .NET runtime 10.0, nvim 0.12.6)

Trimmed only where marked `…`; the full run is reproduced by `./run.sh`.

TCP (`attach_tcp.lua`), Neovim connected to `--listen 127.0.0.1:0`:

```text
server pid 72849 listening on 60716
DIAGNOSTICS {"lsp_diagnostics":[],"nvim_diagnostics":[],"filetype":"kc","clients":["kfs"],"file":"ok.c","encoding":"utf-16","version":0,"waited_ok":true}
DIAGNOSTICS {"lsp_diagnostics":[{"range":[11,20,11,21],"source":"kfs","severity":2,"message":"the result of 'purr_light_count' is ignored\nhint: Check it, or cast the call to `(void)`.","code":"KC-W207"},{"range":[12,10,12,11],"source":"kfs","severity":2,"message":"assignment in a condition; did you mean `==`?\nhint: Write `==`, or wrap the assignment in parentheses.","code":"KC-W648"},{"range":[13,4,13,17],"source":"kfs","severity":1,"message":"'not_a_service' is not declared\nhint: Declare the function before calling it, or include the header that declares it.","code":"KC-E003"},{"range":[14,12,14,13],"source":"kfs","severity":1,"message":"unexpected ';' in an expression","code":"KC-E012"}],"nvim_diagnostics":[{"col":20,"end_col":21,"msg":"the result of 'purr_light_count' is ignored","code":"KC-W207","severity":2,"lnum":11},{"col":10,"end_col":11,"msg":"assignment in a condition; did you mean `==`?","code":"KC-W648","severity":2,"lnum":12},{"col":4,"end_col":17,"msg":"'not_a_service' is not declared","code":"KC-E003","severity":1,"lnum":13},{"col":12,"end_col":13,"msg":"unexpected ';' in an expression","code":"KC-E012","severity":1,"lnum":14}],"filetype":"kc","clients":["kfs"],"file":"error.c","encoding":"utf-16","version":0,"waited_ok":true}
DIAGNOSTICS {"lsp_diagnostics":[{"range":[7,27,7,42],"source":"kfs","severity":1,"message":"'oops_undeclared' is not declared\nhint: …","code":"KC-E003"}],"nvim_diagnostics":[{"col":31,"end_col":46,"msg":"'oops_undeclared' is not declared","code":"KC-E003","severity":1,"lnum":7}],"filetype":"kc","clients":["kfs"],"file":"unicode.c","encoding":"utf-16","version":0,"waited_ok":true}
UNICODE-CHECK encoding=utf-16 expected lnum=7 col=31 end_col=46 (bytes) got lnum=7 col=31 end_col=46 raw_character=27 -> PASS
DIAGNOSTICS {"lsp_diagnostics":[{"range":[2,0,2,16],"source":"kfs","severity":1,"message":"[in <kfs.h>:102:6] expected a name; found '42'","code":"KC-E012"}],…,"file":"header_error.c",…}
DIAGNOSTICS {"lsp_diagnostics":[{"range":[4,0,4,1],"source":"kfs","severity":1,"message":"unterminated comment","code":"KC-E012"}],…,"file":"unterminated_comment.c",…}
DIAGNOSTICS {"lsp_diagnostics":[{"range":[3,24,3,25],…,"message":"unterminated string literal","code":"KC-E012"},{"range":[2,0,2,1],…,"message":"task 't' must be defined as `void t(void)`\nhint: No function named 't' is defined in 'unterminated_string.c'.","code":"KC-E401"}],…}
DIAGNOSTICS {"lsp_diagnostics":[{"range":[1,0,1,1],…,"message":"can't find 'nope.h'\nhint: …","code":"KC-E015"},{"range":[2,0,2,1],…,"message":"can't find 'nope_local.h'\nhint: …","code":"KC-E015"}],…,"file":"missing_include.c",…}
DIAGNOSTICS {"lsp_diagnostics":[{"range":[0,0,0,0],…,"message":"[empty.c] the program declares no task or init\nhint: …","code":"KC-E401"}],…,"file":"empty.c",…}
DIAGNOSTICS {"lsp_diagnostics":[{"range":[0,0,0,1],"source":"kfs","severity":1,"message":"compiler threw InvalidOperationException: RET 0 with 2 cells on the stack in 't' (builder invariant)","code":"KFS-LSP"}],…,"file":"throws.c",…}
(all files: 115 ms incl. first connect/initialize)
HOVER purr_light_set_on {"contents":{"value":"```c\npurr_light_set_on(light, on)\n```\nSwitches a light on or off.\n\n- **args:** `u32 light`, `u32 on`\n- **results:** —\n- **cost class:** Light\n- **host checks:** light index out of range\n- **service:** `SVC_PURR_LIGHT_SET_ON` 0x5012 (request)","kind":"markdown"},"range":{"end":{"line":12,"character":39},"start":{"line":12,"character":22}}}
HOVER kfs_logf {"contents":{"value":"```c\nkfs_logf(fmt, …)\n```\nPrints a formatted line to the console. kC's kfs_logf builds the argument buffer; in kASM, fmt is a .format index and argp the buffer's address.\n\n- **args:** `u32 fmt`, `u32 argp` (§11)\n- **results:** —\n- **cost class:** Medium\n- **host checks:** —\n- **service:** `SVC_LOG` 0x0001 (request)","kind":"markdown"},…}
COMPLETION error=nil count=23 labels=kfs_burn_abort,kfs_burn_start,kfs_engines_off,kfs_engines_on,kfs_halt,kfs_hold_direction,kfs_hold_heading,kfs_hold_quat,kfs_hold_rates,kfs_logf,kfs_plan_burn,kfs_plan_circularize,kfs_release_attitude,kfs_set_attitude_profile,kfs_set_throttle,kfs_sleep_until,kfs_sleep_until_event,kfs_stage,kfs_translate,purr_crash_vec,purr_light_count,purr_light_set_on,trace
COMPLETION purr items [{"detail":"purr_crash_vec()",…},{"documentation":{"value":"Number of lights on the vehicle.","kind":"markdown"},"label":"purr_light_count","detail":"purr_light_count()"},{"documentation":{"value":"Switches a light on or off.","kind":"markdown"},"label":"purr_light_set_on","detail":"purr_light_set_on(light, on)"}]
RAPID-EDITS final_version=13 publishes_after_edits=[v13:0] last=v13 with 0 diagnostics -> PASS
clients after stop: 0
-- a second Neovim against the same running server (unicode.c only)
UNICODE-CHECK encoding=utf-16 expected lnum=7 col=31 end_col=46 (bytes) got lnum=7 col=31 end_col=46 raw_character=27 -> PASS
```

Server stderr for that run (excerpt):

```text
[   204ms] kfs init: 195 ms; tables=embedded:KSA+purrtty services=24 kc-names=23
[   206ms] listening on 127.0.0.1:60716
[   234ms] client #1 connected from 127.0.0.1:60717
[   236ms] tcp#1 client offered positionEncodings [utf-8,utf-16,utf-32] -> using utf-16
[   318ms] tcp#1 compile ok.c v0 gen1: 79.3 ms, ok=True diags=0 (concurrent=1, max=1)
[   321ms] tcp#1 compile error.c v0 gen1: 1.0 ms, ok=False diags=4 (concurrent=1, max=1)
[   333ms] client #2 connected from 127.0.0.1:60718          <- nasty/ files have another root_dir => second client
[   345ms] tcp#2 compile throws.c v0 gen1: 1.8 ms, THREW InvalidOperationException: RET 0 with 2 cells on the stack in 't' (builder invariant) (concurrent=1, max=1)
[   350ms] tcp#1 <- textDocument/didChange                    (x9, within 1 ms)
[   350ms] tcp#1 compile ok.c v5 gen2: 0.5 ms, ok=False diags=1 -> DROPPED (superseded by gen10; concurrent=1, max=6)
[   351ms] tcp#1 compile ok.c v12 gen9: 0.9 ms, ok=False diags=8 -> DROPPED (superseded by gen10; concurrent=6, max=7)
[   351ms] tcp#1 compile ok.c v13 gen10: 1.0 ms, ok=True diags=0 (concurrent=6, max=7)
[   354ms] tcp#1 compile ok.c v6 gen3: 4.1 ms, ok=False diags=2 -> DROPPED (superseded by gen10; concurrent=2, max=7)
[  1858ms] tcp#1 exit (process exit: False)
[  1859ms] client #1 disconnected
[  1884ms] client #3 connected from 127.0.0.1:60722           <- the second Neovim process
```

stdio (`attach_stdio.lua`, server forced to `utf-8`, then `utf-32`): the same diagnostics, hover and
completion as TCP, plus

```text
UNICODE-CHECK encoding=utf-8 expected lnum=7 col=31 end_col=46 (bytes) got lnum=7 col=31 end_col=46 raw_character=31 -> PASS
(all files: 608 ms incl. first connect/initialize)        <- includes dotnet start + 195 ms kFS init
RAPID-EDITS final_version=13 publishes_after_edits=[v13:0] last=v13 with 0 diagnostics -> PASS
UNICODE-CHECK encoding=utf-32 expected lnum=7 col=31 end_col=46 (bytes) got lnum=7 col=31 end_col=46 raw_character=26 -> PASS
```

Compiler checks (no LSP):

```text
bench ok.c                   lines=  15 ok=True diags=0 first=   80.0 ms  warm min=0.6 median=0.6 max=4.3 ms (n=30)
bench ascent_simple.c        lines=  98 ok=True diags=0 first=   14.5 ms  warm min=1.6 median=1.8 max=7.2 ms (n=30)
bench attitude_hold.c        lines=  74 ok=True diags=0 first=   29.1 ms  warm min=15.5 median=18.5 max=42.2 ms (n=30)
bench nan.c                  lines=  21 ok=True diags=0 first=    8.4 ms  warm min=7.5 median=8.2 max=11.8 ms (n=30)
bench prograde_hold.c        lines=  95 ok=True diags=0 first=   19.0 ms  warm min=14.4 median=18.8 max=46.0 ms (n=30)
(… all_stock/double/main/scope: warm 0.2–0.8 ms)
# separate process, largest example compiled first:
bench ascent_simple.c        lines=  98 ok=True diags=0 first=   94.1 ms  warm min=1.6 median=1.7 max=11.2 ms (n=30)
stress (cold): 400 compiles of 12 files on 8 threads in 1505 ms: mismatches=0 threw=0     (5 runs, all 0/0)
nasty [throws.c] THREW System.InvalidOperationException: RET 0 with 2 cells on the stack in 't' (builder invariant) (9 ms)
nasty [unterminated_comment.c] ok=False diags=1  unterminated_comment.c:5:1: error KC-E012: unterminated comment
nasty [nested parens x200]     ok=False diags=1  deep.c:5:148: error KC-E720: nesting deeper than 128 (…)
nasty [nested parens x100000]  ok=False diags=1  deep.c:0:0: error KC-E724: more than 150,000 tokens after preprocessing
nasty [2000 binary operators]  ok=False diags=1  ops.c:5:4020: error KC-E721: more than 1,000 binary operators in one full expression
nasty [150 undeclared calls]   ok=False diags=101 … many.c:104:5: error KC-E003: too many errors; stopping
nasty [self-include]           ok=False diags=1  self.c:0:0: error KC-E722: #include nested deeper than 32
nasty [NUL and emoji]          ok=False diags=2  bytes.c:4:16: error KC-E012: stray '\0' in the program
nasty [no .c file (buffer is a .h)] ok=False diags=1  lib.h:0:0: error KC-E012: the project has no .c file
```

`vim.lsp.rpc.connect` probe (`probe_connect.lua`):

```text
STEP 1: nothing listening on port 60724
NOTIFY[3] Could not connect to 127.0.0.1:60724, reason: "ECONNREFUSED"
  after 3 s: clients=get_clients=0 []; started ids [#1:NOT-initialized]
  server started on the same port; after 2 s: clients=get_clients=0 []; started ids [#1:NOT-initialized]
  buffer attached to client #1? true
EVENT on_exit client=1 code=0 signal=0
EVENT LspDetach client=1 buf=ok.c
STEP 2: server up; re-edit ok.c, then error.c (same dir), then nasty/empty.c (other dir)
EVENT LspAttach client=2 buf=ok.c
EVENT LspAttach client=2 buf=error.c
  +error.c: clients=get_clients=1 [#2(init=true,root=samples,bufs=2)]; …
EVENT LspAttach client=3 buf=empty.c
  +nasty/empty.c: clients=get_clients=2 [#2(init=true,root=samples,bufs=2) #3(init=true,root=nasty,bufs=1)]; …
STEP 3: kill the server (game quits / mod reload)
EVENT on_exit client=3 code=0 signal=0
EVENT on_exit client=2 code=0 signal=0
EVENT LspDetach client=3 buf=empty.c
EVENT LspDetach client=2 buf=ok.c
EVENT LspDetach client=2 buf=error.c
  after 2 s: clients=get_clients=0 []; …; diagnostics still shown for error.c: 0
STEP 4: restart the server on the same port
  after 3 s, no action: clients=get_clients=0 []; …
EVENT LspAttach client=4 buf=error.c
  after :edit! of error.c: clients=get_clients=1 [#4(init=true,root=samples,bufs=1)]; …
```

## Findings

**Attach.** Headless Neovim attached over TCP (`vim.lsp.rpc.connect`) and over stdio, received
`publishDiagnostics` for every sample, and got hover and completion results. Two Neovim processes
against one listening server worked (server clients #1–#3).

**Position model** (`KSA.Kfs.KC.Lexer`, `Span`, `Diagnostic`). A diagnostic is a *point*:
`File`, 1-based `Line`, 1-based `Col`; no end and no length. The lexer first rewrites CRLF and lone CR
to LF, then counts one column per UTF-16 code unit: a tab is 1, `é` is 1, `🐱` is 2. `Line == 0`
(with `Col == 0`) means "no position" (project-level: `KC-E401`, `KC-E722`, `KC-E724`, "no .c file").
Diagnostics from a header carry the header's name, for a system header with angle brackets
(`<kfs.h>:102:6`). Conversion implemented: line = `Line-1`; start = `Col-1` as a UTF-16 index into the
same-normalised line, then re-encoded into the negotiated encoding; end = end of the identifier at that
point, else one character. Header and `Line 0` diagnostics are anchored on the `#include` line naming
that header (else the first `#include`, else line 0) with the original location prefixed to the
message. Encoding: Neovim 0.12 offers `positionEncodings [utf-8, utf-16, utf-32]`; the server chooses
`utf-16` (identity with kFS columns, and the LSP default) and converts when forced to `utf-8`/`utf-32`.
`unicode.c` (tab + `🐱` + `é` + `ü` before the error) mapped to the right byte columns in all three
encodings: raw `character` 27 / 31 / 26, Neovim byte col 31 each time.

**Latency.** Warm compiles: 0.2–2 ms for small files and `ascent_simple.c` (98 lines); 15–23 ms
median (max ~46 ms) for `attitude_hold.c` / `prograde_hold.c` (bus-using programs; the back end
dominates). The first compile in a process costs 80–95 ms (JIT); kFS init (tables + Harmony) ~190 ms.
Compile-per-change with a ~100–150 ms debounce is clearly viable; even debounce 0 kept up with nine
changes in 1 ms.

**Multiple errors.** The front end recovers and reports several errors and warnings in one pass
(`error.c`: E003 on line 14 and E012 on line 15 plus two warnings). The cap is 100 errors, then one
extra "too many errors; stopping" (101 diagnostics); warnings are not capped. Lex errors
(unterminated comment/string, stray character) abort the unit and can cascade into a follow-on
`KC-E401` "task must be defined".

**Thread safety.** No problem observed. Nine back-to-back `didChange`s produced up to 7 concurrent
compiles of the same document; all stale results were dropped and only the final version was
published. `--stress --cold` (400 compiles of 12 files, 8 threads, no serial warm-up, 5 runs) gave
identical diagnostics and image hashes to serial compiles every time. This only covers what these
inputs exercise; the static `SystemHeaders` tables are mutated at init only.

**Exceptions.** Malformed input (unterminated comment/string, missing `<…>`/`"…"` include, empty
file, 100,000 nested parentheses, 5,000 nested blocks, 2,000 operators, self-include, NUL, a 100k-char
line) always came back as diagnostics (limits `KC-E720/721/722/724`). The compiler **does throw** in at
least one case: a service whose result has 3 cells (`samples/nasty/throws.c`, spike-only row
`0x5014`) → `InvalidOperationException … (builder invariant)` rethrown from `Compiler.Compile`. The
server catches it and publishes a `KFS-LSP` diagnostic; the session continues. `Compiler.Compile`
runs on its own 16 MB-stack thread with a 30 s timeout; on timeout that thread is abandoned, not
stopped.

**`vim.lsp.rpc.connect` semantics** (Neovim 0.12.6 `_transport.lua` and the probe):
- Nothing listening: one `vim.notify` WARN "Could not connect … ECONNREFUSED"; the client stays
  attached to the buffer but never initialises, is not returned by `vim.lsp.get_clients()`, never
  retries (starting the server later does nothing), and `on_exit` is not called until it is stopped.
- Server dies: Neovim sees EOF → `on_exit(0, 0)`, `LspDetach` for every buffer, diagnostics are
  cleared. No reconnect when the server comes back; a new client starts only when the `FileType`
  autocommand fires again (`:edit!`, opening another kC buffer).
- One client per `(name, root_dir)`: two buffers in `samples/` shared client #2 (one TCP connection);
  `samples/nasty/` got client #3 and its own connection. Each Neovim process makes its own connections.
- Neovim logs the server's stderr into `lsp.log` at ERROR level (183 lines in one run) — stdio only.

## Pitfalls and design notes for a production (in-game) server

- In TCP mode `exit` must close only the connection, never the game (implemented: `process exit: False`).
- Bound concurrency: this spike starts a compile per change (max 7 observed, each on a fresh 16 MB-stack
  thread). Production should keep at most one compile per document in flight plus one pending "latest"
  slot, and debounce ~100 ms.
- Catch everything around `Compiler.Compile`; it can throw. A compile that hits the 30 s timeout keeps
  burning a thread inside the game.
- Redirect `Console.Out` away from a stdio transport (done here) — game code may print.
- Compile the whole workspace project (the buffer's folder in `kfs/`), overlaying unsaved buffers;
  this spike compiles one file, so `#include "x.h"` and multi-file projects are not resolved, and a
  `.h` buffer yields "the project has no .c file".
- Use the game's real target part/layout for the vehicle being edited; `vehicle.h` depends on the
  layout (here `LayoutGenerator.Vehicle("lsp", 4, 1, 1)` on the Bench part).
- Diagnostics have no ranges; token-width ranges are a heuristic. Header diagnostics have no file a
  client can open; anchoring on the `#include` line is a workaround (or serve generated headers as
  virtual documents).
- The Neovim plugin needs its own reconnect story: probe the port before `vim.lsp.start`, or retry on
  `on_exit` / a timer, because a refused connection leaves a dead, invisible client attached.
- Advertise and honour `positionEncoding`; utf-16 is free because kFS columns are UTF-16 units.
- Hover/completion read the service rows; they reflect mod-added rows only if the rows were installed
  before `KfsIntegration.Tables` was first read.
