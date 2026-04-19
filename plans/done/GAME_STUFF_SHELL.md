# SUBAGENT INSTRUCTIONS

* whole solution MUST compile with `dotnet build`
* whole solution test suite MUST pass with `.\scripts\dotnet-test.ps1`
* you MUST commit the final version to git with a good subject and markdown bullet point list of what was implemented

# IDEAS

Implement a new custom shell.

It should be called "GameStuffShell".

I want this shell to support some common concepts of bash shell behaviors:



* stdin streams
* seperate emission of stdout/stderr streams
* output piping, e.g. `cmd1 | cmd2` where the stdout of cmd1 is sent as stdin to cmd2, and any stderr is emitted back to the terminal emulator for display
* similar redirection of streams of bash and support the special /dev/null to sent a stream to a empty sink.  for example
    * `2>&1` to combine stderr into stdout
    * `1>/dev/null` to send stdout to a black hole sink
    * `2>/dev/null` to send stderr to a black hole sink
    * `2>&1 1>/dev/null` to send stderr to stdout and black hole the original stdout
* program exit codes. non-zero short circuits followup programs where appropriate
* ; to separate programs to run in sequence
* && to run programs after one is successful
* || to run programs after no matter what happens 
* | the pipe streams to the next program (e.g. stdout of left goes to stdin of right)
* easily implementable "program" handlers so they can be added trivially
    * start with a few simple programs:
        " "echo" program which will send its arguments to stdout like real-world echo
        * "crafts" program which will print each craft name on a line
        * "xargs" program which take each line of stdin and send it as stdin to the specificed program once per inupt line.  for example if "crafts" returns "ship1\nship2" then `crafts | xargs lookat` then lookat would be called twice with `ship1` and `ship2` as input.  xargs should split on whitespace similar to real-world xargs behavior.  however our xargs should default to the same thing as real `xargs -n 1` would, meaning to send each split arg to a fresh invocation of the target program
        * "follow" - program to follow (e.g. the camera) the craft by name
        * "sleep [arg]" - sleeps for [arg] milliseconds before finishing the program execution
        * "sh -c [arg]" - runs the [arg] as a new program, notably ; should work here for multiple programs.  this is run in a subshell way like real world.


# PLAN

## Goals / non-goals

Goal: implement a new custom game shell named **GameStuffShell** that can execute a *bash-like subset* of command strings, where each “program” is a C# handler running in-process (no real OS processes).

Non-goals (at least initially): full bash language, job control/backgrounding, TTY line discipline emulation, globbing, command substitution, arithmetic expansion, full quoting/escaping edge-cases, here-docs, subshell parentheses, signals.

## High-level features (practical bash-like subset)

### 1) Shell language subset (what we parse)

Support a small but coherent grammar that enables pipelines + short-circuit lists:

- **Simple command**: `name arg1 arg2 ...`
- **Pipelines**: `cmd1 | cmd2 | cmd3`
- **Lists**:
    - Sequential: `cmd1 ; cmd2 ; cmd3`
    - AND list: `cmd1 && cmd2` (run next only if exit code == 0)
    - OR list: `cmd1 || cmd2` (run next only if exit code != 0)
- **Redirections** (file-descriptor style):
    - `n>target`, `n>>target`, `n<target` (only `target = /dev/null`; any other target is an error)
    - `n>&m` and `n<&m` (dup fd) including `2>&1`
    - Allow multiple redirections per command; **process left-to-right** (this matters for cases like `2>&1 1>/dev/null`).
    - Redirections are only allowed **after** the command name (not before).
    - **Default FDs**: `>` without a number means `1>`, `<` without a number means `0<`.
    - **Valid FD numbers**: only 0, 1, 2. Any other FD number (e.g., `9>&1`) is an error (exit code `2`).

Notes:
- `||` in bash is *conditional on failure* (not “always run”). If you want “always run”, that’s `;`.
- A newline can be treated like `;` (optional, only if/when you support multi-line input).
- **Semicolon rules**: trailing `;` is allowed (e.g., `echo hi ;`). Empty commands between operators are errors: `;;`, `; ;`, `| |`, etc. should produce a parse error (exit code `2`).

### 2) Tokenization essentials (to make parsing usable)

Minimum quoting/escaping to make commands ergonomic:

- Whitespace separates tokens, except inside quotes.
- Single quotes `'...'` (no escapes inside)
- Double quotes `"..."` (allow `\\` and `\"` at minimum)
- Backslash escaping outside quotes for: space, `|`, `&`, `;`, `>`, `<`, `#`, `\` itself. Other characters after `\` are literal (e.g., `\x` → `x`).
- `#` comments: if `#` appears at start of token position (i.e. after whitespace) and not inside quotes, treat rest of line as comment.
- **Word joining** (bash-compatible): adjacent quoted and unquoted segments form a single word. E.g., `"a"'b'c` → one word `abc`.
- **Empty strings**: `""` and `''` are valid and produce an empty-string argument.

Deliberately skip (initially): `$var` expansion, `$(...)`, backticks, globbing.

### 3) Streams & file descriptors model

We need a small abstraction that mirrors FD semantics enough for piping/redirection:

- Standard FDs: **0=stdin**, **1=stdout**, **2=stderr**.
- Each program runs with a **StreamSet** (stdin reader, stdout writer, stderr writer).
- Default behavior:
    - stdout/stderr go to the terminal
    - stdin is empty unless the shell supports interactive feeding
- Pipeline behavior:
    - stdout of left command connects to stdin of right command
    - stderr stays attached to terminal *unless redirected*.

Important: current `BaseChannelOutputShell` always emits `ShellOutputType.Stdout` from its pump, even though `ShellOutputEventArgs` supports stderr. GameStuffShell will either:

- (A) implement its own two-channel pump (stdout+stderr), or
- (B) enhance/extend the base shell abstraction to support output events tagged as stdout vs stderr (Selected approach: see Task 0).

### 4) Exit codes & propagation

- Each program returns an **int exit code**.
- Unknown command: use conventional `127`.
- Parse errors: use conventional `2`.
- Pipeline exit status: pick a rule and document it:
    - Default (bash-like): pipeline status = exit code of **last** program.
    - (Optional future) `pipefail` semantics (non-zero if any stage fails).
- Short-circuit lists:
    - `&&`: evaluate right side only if left exit code == 0
    - `||`: evaluate right side only if left exit code != 0
    - `;`: always evaluate next

### 5) Program registry + builtins

Provide a clean way to add programs:

- A registry mapping `string name -> handler`.
- Handlers get `argv[]`, StreamSet, CancellationToken, plus a game-context service provider.
- **Context additions**: Handlers also receive `int TerminalWidth`, `int TerminalHeight`, and `IReadOnlyDictionary<string, string> Environment`.
- Support a few initial builtins/programs:
    - `echo ...` writes args + newline to stdout
    - `crafts` writes craft names, one per line
    - `xargs <prog> [args...]` reads stdin, splits, invokes target program repeatedly (default like `xargs -n 1`)
    - `follow <craftName>` switches camera/target (regular command; completes immediately)
    - `sleep <ms>`
    - `sh -c <string>` parses/executes `<string>` as a subshell script (see scoping below)

### 6) Cancellation & long-running programs

- Ctrl+C maps to `RequestCancellation()` and cancels the currently-running “job” (current pipeline/list) via a `CancellationTokenSource`.
- Programs are expected to respect cancellation.

### 7) Error reporting & UX

- Parse errors: print a readable message to **stderr** and return exit code `2`.
- Runtime errors inside a program: print message to stderr and return non-zero (e.g. `1`).
- **Unhandled exceptions** in a program: the executor catches them, prints `"<program>: <exception message>"` to stderr, and returns exit code `1`.
- Prompt behavior: execute command line, then print prompt again when the whole list completes.


# IMPLEMENTATION PLAN

This is a concrete plan that keeps scope tight while still feeling like “bash” for pipelines/redirections.

## 1) Define the execution data model

- **AST nodes** (minimal):
    - `CommandNode` (argv + redirections)
    - `PipelineNode` (list of commands)
    - `ListNode` (sequence of “steps” with operator between them: `;`, `&&`, `||`)

- **Exit status contract**:
    - `Task<int> ExecuteAsync(ExecContext ctx, CancellationToken ct)` for each node.

## 2) Build a tokenizer (lexer)

Input: a single command line string.
Output: token stream with spans for error messages.

Tokens to recognize:

- Words (unquoted, single-quoted, double-quoted)
- Operators: `|`, `&&`, `||`, `;`
- Redirection operators: `>`, `>>`, `<`, `>&`, `<&` plus optional leading FD number

Rules:

- Tokenize `2>&1` as: FD=2, op=`>&`, word=`1` (or as one RedirToken with parts).
- Preserve order of redirections as written.

Deliverable: good error spans for “unterminated quote”, “unexpected token”, etc.

## 3) Implement a small parser with correct precedence

Precedence (tightest to loosest):

1. Redirections attach to the nearest simple command.
2. Pipeline `|` groups commands.
3. `&&` and `||` group pipelines.
4. `;` separates list items (and optionally newline).

Recommended approach: recursive descent / Pratt parser.

## 4) Implement stream primitives (FD plumbing)

We need something like “pipes” but in-memory:

- `IPipeReader` / `IPipeWriter` based on `Channel<ReadOnlyMemory<byte>>` or `Pipe` (System.IO.Pipelines).
- `NullSink` for `/dev/null`.
- A `TerminalWriter` that writes to the custom shell’s output event with the appropriate `ShellOutputType` (Stdout/Stderr).
 - **Data encoding**: All streams are UTF-8 text only; stdin/stdout/stderr are treated as UTF-8 strings, no binary data support.

FD mapping:

- Start with `fd[0]=EmptyReader`, `fd[1]=TerminalStdoutWriter`, `fd[2]=TerminalStderrWriter`.
- Apply redirections **left-to-right**, mutating the mapping.
- `n>&m` sets `fd[n]` to the *current* `fd[m]` object reference.

Validation:

- If a redirection target is anything other than `/dev/null`, emit an error to stderr and fail the command (exit code `2`).

Important repo integration detail:

- Because `BaseChannelOutputShell` currently pumps only stdout, GameStuffShell will likely need its own output pump or a small framework tweak so stderr can be emitted as `ShellOutputType.Stderr`.

## 5) Execute pipelines

Use a **buffered (non-streaming) pipeline** model:

- Run each command to completion.
- Capture its stdout into an in-memory buffer.
- Feed that buffer as stdin to the next command.
- stderr is emitted to terminal unless redirected; because execution is non-streaming, stdout/stderr interleaving won’t perfectly match bash (acceptable for this shell).

Pipeline status is the exit code of the **last** command (bash default).

## 6) Execute `;`, `&&`, `||` lists

- Evaluate left-to-right.
- Use previous exit code to decide whether to run next node.
- Ensure that when a node is skipped (because of `&&`/`||`), it does **not** run at all.

## 7) Program registry + builtins

- `IProgram` style interface:
    - `string Name`
    - `Task<int> RunAsync(ProgramContext ctx, CancellationToken ct)`

- `ProgramContext` includes:
    - `IReadOnlyList<string> Argv`
    - `StreamSet Streams` (stdin/stdout/stderr)
    - `IGameStuffApi` / services needed to query crafts, follow camera, etc.
    - `IReadOnlyDictionary<string, string> Environment` (Environment variables, CWD, etc.)
    - `int TerminalWidth`, `int TerminalHeight` (Current terminal dimensions)

Implementation notes for initial programs:

- `echo`: join args (after argv[0]) with spaces, write newline. Support `-n` flag to suppress trailing newline.
- `crafts`: write one name per line.
- `xargs`:
    - Read stdin as UTF-8 text
    - Split on whitespace (no quote awareness; raw text split)
    - For each token, invoke target program with token appended to its argv
    - **Edge cases**: if no program argument given, error exit `2`. If stdin is empty (no tokens), error exit `2`.
    - Exit status: return `0` only if all invocations succeed; otherwise return the first non-zero exit code.
- `sh -c`:
    - Parse `<string>` using the same lexer/parser
    - Run in a new “shell scope” (see next section).

## 8) Subshell scoping (`sh -c`)

Decide what is isolated vs shared:

- Shared (recommended initially): program registry, game services.
- Isolated: cancellation token source for that invocation.
- Copy-on-Write: Environment variables (if modified in subshell, shouldn't affect parent).

## 9) Diagnostics and tracing

- Print parse errors to stderr with a caret span.
- Add lightweight trace hooks (optional): “executing command”, “exit code”, “redirections applied”, “pipeline connected”.


# DETAILED PLAN

This section is written to be handed to a “non-thinking” coding sub-agent. Each task is incremental and should be implementable largely in isolation.

Repository layout decision:

- All GameStuffShell-related code lives under `caTTY.CustomShells/GameStuffShell/` to keep the feature cleanly demarcated.
- Only the project files remain at the project root; do not add new unrelated top-level folders.

## Task 0 — Enable typed output (stdout vs stderr) for all custom shells

Goal: make it possible for a custom shell to emit true stderr (so the PTY bridge can mark it as error), without requiring every shell to reinvent output pumping.

Why: `ShellOutputEventArgs` already supports `ShellOutputType.Stderr`, and [caTTY.Core/Terminal/CustomShellPtyBridge.cs](caTTY.Core/Terminal/CustomShellPtyBridge.cs) uses it to set `isError`, but the current output pump in [caTTY.CustomShellContract/Base/BaseChannelOutputShell.cs](caTTY.CustomShellContract/Base/BaseChannelOutputShell.cs) always raises stdout.

Files to edit:

- [caTTY.CustomShellContract/Base/BaseChannelOutputShell.cs](caTTY.CustomShellContract/Base/BaseChannelOutputShell.cs)
- [caTTY.CustomShellContract/Base/BaseLineBufferedShell.cs](caTTY.CustomShellContract/Base/BaseLineBufferedShell.cs)
- (Optional improvement) [caTTY.CustomShells/GameConsoleShell.cs](caTTY.CustomShells/GameConsoleShell.cs)

Implementation steps:

1. Change the internal channel in `BaseChannelOutputShell` to carry both bytes and output type, e.g. `Channel<(byte[] Data, ShellOutputType Type)>`.
2. Keep the existing `QueueOutput(byte[])` and `QueueOutput(string)` methods as convenience methods that default to `ShellOutputType.Stdout`.
3. Add overloads:
     - `QueueOutput(byte[] data, ShellOutputType type)`
     - `QueueOutput(string text, ShellOutputType type)`
4. Update the pump loop to call `RaiseOutputReceived(data, type)`.
5. In `BaseLineBufferedShell`, keep `SendOutput(...)` as stdout and add a new protected method `SendError(string text)` (and optionally `SendError(byte[] data)`) that calls the typed queue overload with `ShellOutputType.Stderr`.
6. (Optional) Update `GameConsoleShell` to emit errors as stderr (currently it colors text red but likely still emits stdout).

Tests to add/update:

- Add unit tests in a new file [caTTY.CustomShells.Tests/Unit/BaseChannelOutputShellTypedOutputTests.cs](caTTY.CustomShells.Tests/Unit/BaseChannelOutputShellTypedOutputTests.cs).
    - Create a tiny test shell derived from `BaseChannelOutputShell` that exposes methods to enqueue stdout/stderr.
    - Subscribe to `OutputReceived` and assert the `ShellOutputEventArgs.OutputType` matches what was queued.
- Reference pattern: [caTTY.CustomShells.Tests/Unit/GameConsoleShellTests.cs](caTTY.CustomShells.Tests/Unit/GameConsoleShellTests.cs).

Acceptance criteria:

- A shell can emit stderr via a first-class `ShellOutputType.Stderr` event.
- Existing shells and tests still pass (no behavior regressions besides improved typing).


## Task 1 — Add `GameStuffShell` skeleton (prompt + command dispatch)

Goal: introduce the new shell class wired into the existing custom shell infrastructure, without implementing parsing/execution yet.

Files to create/edit:

- Create: [caTTY.CustomShells/GameStuffShell/GameStuffShell.cs](caTTY.CustomShells/GameStuffShell/GameStuffShell.cs)
- Create: [caTTY.CustomShells.Tests/Unit/GameStuffShellLifecycleTests.cs](caTTY.CustomShells.Tests/Unit/GameStuffShellLifecycleTests.cs)

Implementation steps:

1. Implement `GameStuffShell` deriving from `BaseLineBufferedShell`.
2. Provide `Metadata` with a unique name (e.g. “Game Stuff”), description, version, author, and supported features.
3. Implement:
     - `OnStartingAsync` / `OnStoppingAsync` hooks (similar to `GameConsoleShell`).
     - `SendInitialOutput()` to print a banner + prompt.
     - `GetPrompt()`.
     - `HandleClearScreen()`.
     - `ExecuteCommandLine(string commandLine)` should, for now, just write a placeholder response like “not implemented” to stderr and then prompt again.
4. Add cancellation behavior: override `RequestCancellation()` to print `^C` and return to prompt (even before full pipeline cancellation exists).

Tests:

- Lifecycle: Start/Stop toggles `IsRunning`.
- Banner + prompt show up via output pump.

Reference files:

- Shell interface contract: [caTTY.CustomShellContract/ICustomShell.cs](caTTY.CustomShellContract/ICustomShell.cs)
- Output event types: [caTTY.CustomShellContract/ShellEventArgs.cs](caTTY.CustomShellContract/ShellEventArgs.cs)
- Base shell patterns: [caTTY.CustomShells/GameConsoleShell.cs](caTTY.CustomShells/GameConsoleShell.cs)
- Line-buffered input behavior: [caTTY.CustomShellContract/Base/BaseLineBufferedShell.cs](caTTY.CustomShellContract/Base/BaseLineBufferedShell.cs)


## Task 2 — Add a lexer for the shell language (tokenizer)

Goal: convert a command string into tokens needed for parsing.

Files to create:

- Create: [caTTY.CustomShells/GameStuffShell/Lexing/Token.cs](caTTY.CustomShells/GameStuffShell/Lexing/Token.cs)
- Create: [caTTY.CustomShells/GameStuffShell/Lexing/Lexer.cs](caTTY.CustomShells/GameStuffShell/Lexing/Lexer.cs)
- Create: [caTTY.CustomShells.Tests/Unit/GameStuffShellLexerTests.cs](caTTY.CustomShells.Tests/Unit/GameStuffShellLexerTests.cs)

Implementation details:

- Token kinds: Word, Pipe, AndIf (`&&`), OrIf (`||`), Semicolon, RedirectOut (`>`), RedirectOutAppend (`>>`), RedirectIn (`<`), RedirectDupOut (`>&`), RedirectDupIn (`<&`), IoNumber (a digit sequence immediately before a redirection operator), End.
- **FD number handling**: when a digit sequence (e.g., `2`) appears immediately before a redirection operator with no whitespace, emit it as an `IoNumber` token. Otherwise digits are part of a Word.
- Words:
    - Support unquoted words.
    - Support single quotes `'...'`.
    - Support double quotes `"..."` with minimal escapes `\\` and `\"`.
    - Support minimal backslash escapes outside quotes for: space, `|`, `&`, `;`, `>`, `<`, `#`, `\`.
    - **Word joining**: adjacent quoted/unquoted segments with no whitespace form a single Word token. E.g., `a"b"'c'` → Word `abc`.
    - **Empty strings**: `""` and `''` produce a Word token with empty text.
- Comments: if `#` is encountered when currently between tokens (after whitespace) and not inside quotes, ignore rest of line.
- Return token spans (start index, length) for error messages.

Tests (table-driven):

- `echo a b` => Word Word Word
- `crafts|xargs lookat` => Word Pipe Word Word
- `a && b || c ; d` => Word AndIf Word OrIf Word Semicolon Word
- Quoting: `echo "a b" 'c d' e\\ f` should produce Word tokens with the expected text.
- Word joining: `"a"'b'c` => single Word `abc`.
- Empty string: `echo "" ''` => Word Word Word (second and third are empty).
- Redirection lexing: `2>&1 1>/dev/null` => IoNumber(2) RedirectDupOut Word(1) IoNumber(1) RedirectOut Word(/dev/null).
- Unterminated quote should produce a lexer error (exception or error result type).


## Task 3 — Add a parser that builds a small AST (with precedence)

Goal: parse tokens into an AST representing list/pipeline/command nodes.

Files to create:

- Create: [caTTY.CustomShells/GameStuffShell/Parsing/Ast.cs](caTTY.CustomShells/GameStuffShell/Parsing/Ast.cs)
- Create: [caTTY.CustomShells/GameStuffShell/Parsing/Parser.cs](caTTY.CustomShells/GameStuffShell/Parsing/Parser.cs)
- Create: [caTTY.CustomShells.Tests/Unit/GameStuffShellParserTests.cs](caTTY.CustomShells.Tests/Unit/GameStuffShellParserTests.cs)

AST requirements:

- `CommandNode`: argv list + redirections list (in original order).
- `PipelineNode`: list of `CommandNode`.
- `ListNode`: sequence of `(PipelineNode pipeline, Operator opToNext)` where operator is `;`, `&&`, `||`.

Parsing rules:

- Correct precedence: redirections bind to command; `|` binds tighter than `&&`/`||`; `;` is lowest.
- Allow multiple redirections in any position after the command name (bash-like); preserve order.
- Provide good parse errors: unexpected token, missing command after pipe, etc. Exit code for parse errors should later be `2`.

Tests:

- Parse structure checks (not just “no throw”). Example: `a | b && c ; d` should group as `((a|b) && c) ; d`.
- Redirections attach to correct command: `a 2>&1 | b` should attach the redirection to `a`.


## Task 4 — Implement execution core (no real programs yet)

Goal: execute an AST with correct list and pipeline semantics, using stubs for programs.

Files to create:

- Create: [caTTY.CustomShells/GameStuffShell/Execution/ExecContext.cs](caTTY.CustomShells/GameStuffShell/Execution/ExecContext.cs)
- Create: [caTTY.CustomShells/GameStuffShell/Execution/Executor.cs](caTTY.CustomShells/GameStuffShell/Execution/Executor.cs)
- Create: [caTTY.CustomShells/GameStuffShell/Execution/IProgramResolver.cs](caTTY.CustomShells/GameStuffShell/Execution/IProgramResolver.cs)
- Create: [caTTY.CustomShells/GameStuffShell/Execution/ProgramContext.cs](caTTY.CustomShells/GameStuffShell/Execution/ProgramContext.cs)
- Create: [caTTY.CustomShells.Tests/Unit/GameStuffShellExecutorListTests.cs](caTTY.CustomShells.Tests/Unit/GameStuffShellExecutorListTests.cs)

Execution requirements:

- Implement list semantics:
    - `;` always runs next.
    - `&&` runs next only when previous exit code is `0`.
    - `||` runs next only when previous exit code is non-zero.
- Implement pipeline semantics (buffered):
    - Run each command to completion.
    - Capture its stdout as bytes.
    - Pass captured stdout as stdin bytes for the next command.
    - Pipeline exit code is the exit code of the last command.

Abstractions:

- `IProgramResolver`: interface with `bool TryResolve(string name, out IProgram program)`. The `Executor` depends on this; Task 7's `ProgramRegistry` implements it.
- `ProgramContext`: includes `Argv`, `StreamSet`, `IProgramResolver` (so programs like `xargs` can invoke other programs), `IGameStuffApi`, `CancellationToken`, `Environment`, `TerminalDimensions`.

Error handling:

- If a program throws an unhandled exception, catch it, print `"<program>: <exception.Message>"` to stderr, and return exit code `1`.

Implementation constraints:

- No concurrency required.
- Capture stderr separately and emit to terminal unless redirected.

Tests:

- Use a fake `IProgramResolver` where program names map to handlers returning known exit codes and known stdout/stderr.
- Verify skip behavior for `&&`/`||`.
- Verify pipeline stdout forwarding: program A outputs `"x\n"` to stdout; program B receives that as stdin.
- Verify exception handling: a program that throws produces stderr message and exit code `1`.


## Task 5 — Implement FD mapping + redirections (`/dev/null` and fd duplication)

Goal: apply per-command redirections with left-to-right semantics.

Files to create:

- Create: [caTTY.CustomShells/GameStuffShell/Execution/Streams.cs](caTTY.CustomShells/GameStuffShell/Execution/Streams.cs)
- Create: [caTTY.CustomShells/GameStuffShell/Execution/RedirectionApplier.cs](caTTY.CustomShells/GameStuffShell/Execution/RedirectionApplier.cs)
- Create: [caTTY.CustomShells.Tests/Unit/GameStuffShellRedirectionTests.cs](caTTY.CustomShells.Tests/Unit/GameStuffShellRedirectionTests.cs)

Implementation requirements:

- Model FDs 0/1/2 as an array or dictionary of stream endpoints.
- Support:
    - `2>&1` (dup stderr to current stdout)
    - `1>/dev/null`, `2>/dev/null` (send to null sink)
    - Combination ordering: `2>&1 1>/dev/null` must behave as bash: stderr goes to the *original* stdout, then stdout is nulled.
- Validation: if redirect target is not exactly `/dev/null`, emit error to stderr and fail command with exit code `2`.

Tests:

- `echo hi 1>/dev/null` should produce no stdout.
- A command that writes to stderr with `2>&1` should end up writing to stdout.
- Ordering test: verify `2>&1 1>/dev/null` still prints former stderr to terminal stdout.
- Invalid target: `1>foo` returns exit code `2` and prints an error to stderr.


## Task 6 — Wire parsing + execution into `GameStuffShell.ExecuteCommandLine`

Goal: the shell runs real command lines end-to-end and shows a prompt again.

Files to edit:

- [caTTY.CustomShells/GameStuffShell/GameStuffShell.cs](caTTY.CustomShells/GameStuffShell/GameStuffShell.cs)
- Create: [caTTY.CustomShells.Tests/Unit/GameStuffShellIntegrationTests.cs](caTTY.CustomShells.Tests/Unit/GameStuffShellIntegrationTests.cs)

Implementation steps:

1. In `ExecuteCommandLine`, call lexer -> parser -> executor.
2. On lexer/parser errors:
     - Print a human-readable message to stderr (use the span to show a caret if practical).
     - Return exit code `2`.
3. Ensure prompt is printed after execution completes.
4. Ensure output uses typed stdout/stderr emission (Task 0).

Tests:

- Given input bytes for a full command line ending in Enter, verify output contains prompt and expected results.
- Parse error test: `echo "unterminated` prints error to stderr.


## Task 7 — Add the program registry and implement `echo` and `sleep`

Goal: create the “easy to add programs” mechanism and first two programs.

Files to create:

- Create: [caTTY.CustomShells/GameStuffShell/Programs/IProgram.cs](caTTY.CustomShells/GameStuffShell/Programs/IProgram.cs)
- Create: [caTTY.CustomShells/GameStuffShell/Programs/ProgramRegistry.cs](caTTY.CustomShells/GameStuffShell/Programs/ProgramRegistry.cs)
- Create: [caTTY.CustomShells/GameStuffShell/Programs/EchoProgram.cs](caTTY.CustomShells/GameStuffShell/Programs/EchoProgram.cs)
- Create: [caTTY.CustomShells/GameStuffShell/Programs/SleepProgram.cs](caTTY.CustomShells/GameStuffShell/Programs/SleepProgram.cs)
- Create: [caTTY.CustomShells.Tests/Unit/GameStuffShellProgramsBasicTests.cs](caTTY.CustomShells.Tests/Unit/GameStuffShellProgramsBasicTests.cs)

Implementation details:

- `ProgramRegistry` implements `IProgramResolver` (from Task 4).
- Registry API:
    - `bool TryResolve(string name, out IProgram program)` (from interface)
    - `void Register(IProgram program)`
- Unknown command returns exit code `127` and prints `"<name>: command not found"` to stderr.

**Argument parsing strategy**: the shell passes `argv[]` to programs via `ProgramContext.Argv`. Each program is responsible for its own argument parsing:
- Simple programs can hand-roll checks (e.g., `if (argv[1] == "-n")`)
- Complex programs can use `System.CommandLine` internally: create a `RootCommand`, configure options, call `command.Invoke(argv.Skip(1).ToArray())`
- The shell has no knowledge of program-specific flags; it just tokenizes and forwards.

- `echo`:
    - Join args (after argv[0]) with spaces and write to stdout.
    - Support `-n` flag: if first arg is `-n`, suppress trailing newline.
    - Exit `0`.
- `sleep <ms>`:
    - Parse int milliseconds from first arg; on invalid/missing number, exit `2` with stderr message.
    - On success, `await Task.Delay(ms, ct)` then exit `0`.
    - Respect cancellation token.


## Task 8 — Implement `crafts` and `follow` using a small game API abstraction

Goal: keep programs testable without requiring the full KSA runtime.

Files to create/edit:

- Create: [caTTY.CustomShells/GameStuffShell/GameApi/IGameStuffApi.cs](caTTY.CustomShells/GameStuffShell/GameApi/IGameStuffApi.cs)
- Create: [caTTY.CustomShells/GameStuffShell/Programs/CraftsProgram.cs](caTTY.CustomShells/GameStuffShell/Programs/CraftsProgram.cs)
- Create: [caTTY.CustomShells/GameStuffShell/Programs/FollowProgram.cs](caTTY.CustomShells/GameStuffShell/Programs/FollowProgram.cs)
- Edit: [caTTY.CustomShells/GameStuffShell/GameStuffShell.cs](caTTY.CustomShells/GameStuffShell/GameStuffShell.cs) to provide a real implementation or adapter at runtime.
- Create: [caTTY.CustomShells.Tests/Unit/GameStuffShellProgramsGameApiTests.cs](caTTY.CustomShells.Tests/Unit/GameStuffShellProgramsGameApiTests.cs)

Implementation details:

- `IGameStuffApi` should provide only what’s needed:
    - `IReadOnlyList<string> GetCraftNames()`
    - `bool TryFollowCraft(string craftName, out string? error)`
- Thread safety: The implementation of `IGameStuffApi` is responsible for dispatching to the main game thread if required by the game engine. The shell runs on a background thread.
- `crafts` prints one craft name per line.
- `follow <name>` calls `TryFollowCraft`; exit `0` on success; exit `1` on failure (stderr message).

Tests:

- Use a fake `IGameStuffApi` to drive deterministic results.


## Task 9 — Implement `xargs` (whitespace split, default -n 1 behavior)

Goal: `crafts | xargs follow` works.

Files to create:

- Create: [caTTY.CustomShells/GameStuffShell/Programs/XargsProgram.cs](caTTY.CustomShells/GameStuffShell/Programs/XargsProgram.cs)
- Create: [caTTY.CustomShells.Tests/Unit/GameStuffShellXargsTests.cs](caTTY.CustomShells.Tests/Unit/GameStuffShellXargsTests.cs)

Implementation details:

- Syntax: `xargs <prog> [fixedArgs...]`
- Read stdin as UTF-8 text.
- Split on whitespace (no quote parsing).
- For each token `t`, invoke `<prog>` with argv = `[<prog>, fixedArgs..., t]`.
- Use `ProgramContext.ProgramResolver` to look up the target program.
- **Edge cases**:
    - If `<prog>` argument is missing, print usage error to stderr and exit `2`.
    - If stdin is empty (produces zero tokens), print error to stderr and exit `2`.
- Exit status rule:
    - Return `0` only if all invocations return `0`.
    - Otherwise return the first non-zero exit code encountered.
- Ensure stderr from invoked commands still reaches terminal unless redirected.

Tests:

- Normal case: `echo "a b c" | xargs echo` invokes echo three times.
- Missing program: `xargs` with no args exits `2`.
- Empty stdin: `echo -n "" | xargs echo` exits `2`.
- Partial failure: if second invocation fails, returns that exit code.


## Task 10 — Implement `sh -c` (subshell parsing/execution)

Goal: allow nested execution, including `;`, `&&`, `||` inside the string.

Files to create:

- Create: [caTTY.CustomShells/GameStuffShell/Programs/ShProgram.cs](caTTY.CustomShells/GameStuffShell/Programs/ShProgram.cs)
- Create: [caTTY.CustomShells.Tests/Unit/GameStuffShellShTests.cs](caTTY.CustomShells.Tests/Unit/GameStuffShellShTests.cs)

Implementation details:

- Syntax: `sh -c <string>` (require `-c`; otherwise error exit `2`).
- Execute `<string>` using the same lexer/parser/executor.
- Subshell “scope”:
    - Share: program registry, `IGameStuffApi`.
    - Inherit: stdin/stdout/stderr mapping from the parent command.
    - Use a new cancellation token linked to the parent token.
    - Create a copy of Environment variables to isolate changes (if variables are added later).
- Exit code: return the exit code of the subshell list’s final executed pipeline.


## Task 11 — Add “golden” end-to-end tests

Goal: prove the full semantics work together and remain stable.

Files to create:

- Create: [caTTY.CustomShells.Tests/Unit/GameStuffShellGoldenTests.cs](caTTY.CustomShells.Tests/Unit/GameStuffShellGoldenTests.cs)

Suggested tests:

- `echo a b | xargs echo` produces two lines `a` and `b` (depending on exact xargs behavior).
- `echo hi 1>/dev/null ; echo ok` prints only `ok`.
- `badcmd || echo recovered` prints “command not found” (stderr) and then `recovered`.
- Redirection ordering: a program that writes both stdout and stderr with `2>&1 1>/dev/null` leaves only former stderr visible.
- `echo -n hello` produces `hello` with no trailing newline.
- Word joining: `echo "a"'b'c` produces `abc`.
- Empty string: `echo ""` produces a blank line (just newline).
- Trailing semicolon: `echo hi ;` works without error.
- **Cancellation test**: start `sleep 100000`, send Ctrl+C (call `RequestCancellation()`), verify shell returns to prompt without waiting.

Test harness guidance:

- Use the approach from [caTTY.CustomShells.Tests/Unit/GameConsoleShellTests.cs](caTTY.CustomShells.Tests/Unit/GameConsoleShellTests.cs):
    - Start shell
    - Subscribe to `OutputReceived` and accumulate into buffers for stdout/stderr separately
    - Drive input via `WriteInputAsync` with bytes (including Enter)


