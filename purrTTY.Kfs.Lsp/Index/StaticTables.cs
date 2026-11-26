// RULE FOR THIS FILE: no game type. Transcribed from the decompiled game (Parser.Pragma, Parser
// keywords, Preprocessor directives, Assembler directive switch) as the bundle documents them:
// docs/kfs-info/modding/language-tooling-surface.md, language/kc-preprocessor.md,
// compiler/kasm-assembly-language.md.

namespace purrTTY.Kfs.Lsp.Index;

/// <summary>The fixed vocabularies of kC and kASM that no table holds.</summary>
public static class StaticTables
{
    /// <summary>
    /// The kC keywords the language accepts and gives meaning to. Left out from the parser's reserved
    /// set: <c>union</c> (KC-E641), <c>goto</c> (KC-E640), the C11/C23 words <c>_Alignas</c>,
    /// <c>_Atomic</c>, <c>_Generic</c>, <c>_Noreturn</c>, <c>_Thread_local</c>, <c>typeof</c>,
    /// <c>nullptr</c>, <c>constexpr</c> (KC-E646), <c>_Complex</c> / <c>_Imaginary</c> (no handling), the
    /// kC-internal <c>__kfs_pragma__</c>, <c>__at</c>, <c>__kfs_offsetof</c>, and <c>auto</c>,
    /// <c>register</c>, <c>restrict</c>, which are accepted and ignored.
    /// </summary>
    public static IReadOnlyList<string> Keywords { get; } =
    [
        "break", "case", "char", "const", "continue", "default", "do", "double", "else", "enum", "extern", "float", "for",
        "if", "inline", "int", "long", "return", "short", "signed", "sizeof", "static", "struct", "switch", "typedef",
        "unsigned", "void", "volatile", "while", "_Bool", "_Static_assert", "_Alignof",
    ];

    /// <summary>Preprocessor directives: name, syntax, meaning.</summary>
    public static IReadOnlyList<(string Name, string Syntax, string Doc)> PreprocessorDirectives { get; } =
    [
        ("include", "#include <header> | #include \"file\"", "Includes a system header (`<kfs.h>`, `<vehicle.h>`, `<purrtty.h>`, …) or a project file (`\"x.h\"`, `\"x.kasm.h\"`). Lookups are exact names: no directory search. Nesting 32, 1000 includes per unit."),
        ("define", "#define NAME body | #define NAME(a, b) body", "An object-like or function-like macro (the `(` must touch the name; `...` makes it variadic, `__VA_ARGS__`)."),
        ("undef", "#undef NAME", "Removes a macro; no error when it is not defined."),
        ("if", "#if expression", "Conditional inclusion on an integer constant expression (`defined(NAME)` allowed)."),
        ("ifdef", "#ifdef NAME", "Conditional inclusion when NAME is a macro."),
        ("ifndef", "#ifndef NAME", "Conditional inclusion when NAME is not a macro."),
        ("elif", "#elif expression", "Else-if branch of a conditional group."),
        ("else", "#else", "Else branch of a conditional group."),
        ("endif", "#endif", "Ends a conditional group."),
        ("pragma", "#pragma kfs directive … | #pragma once", "`#pragma kfs …` declares the program's structure (tick rate, tasks, bus, claims); `#pragma once` includes the file at most once per unit; anything else is `KC-W010`."),
        ("error", "#error text", "Stops with `KC-E014` and the text."),
        ("warning", "#warning text", "Reports `KC-W017` with the text."),
        ("line", "#line N [\"file\"]", "Renumbers the following lines (N from 1 to 2147483647, `KC-E727`) and optionally renames the file in diagnostics."),
    ];

    /// <summary>
    /// The <c>#pragma kfs</c> directives (Parser.Pragma): name, syntax, meaning. <c>{tick}</c> in a
    /// meaning stands for the allowed tick rates of the tables.
    /// </summary>
    public static IReadOnlyList<(string Name, string Syntax, string Doc)> Pragmas { get; } =
    [
        ("tick_hz", "#pragma kfs tick_hz N", "The scheduler's tick rate, once per program. N must be one of {tick} Hz (`KC-E406`); without it the program runs at {default} Hz."),
        ("task", "#pragma kfs task NAME rate_hz=N | divisor=N [priority=P]", "Makes NAME a periodic task. NAME must be declared in this unit and defined as `void NAME(void)` (`KC-E401`). One of `rate_hz` (must divide the tick rate) or `divisor` (1–65535) is required (`KC-E407`); `priority` 0–255, higher runs first among equal divisors."),
        ("init", "#pragma kfs init NAME [priority=P]", "Makes NAME the init task, which must finish within the part's init budget (`init_budget_cycles`, else the `INIT_OVERRUN` trap); at most one per program (`KC-E410`). Same option syntax as `task`."),
        ("optimize", "#pragma kfs optimize 0|1", "Turns the optimizer off (0) or on (1) for this translation unit."),
        ("bounds", "#pragma kfs bounds on|off", "Array bounds checks (the `BOUNDS` trap) for this translation unit."),
        ("publish", "#pragma kfs publish MSG_ID buffer", "The program publishes the message from `buffer`, which must be a file-scope object of the message's `kfs_msg_*_t` type. On the Sequencer `MSG_NAV_STATE`, `MSG_ATT_TARGET` and `MSG_WRENCH_CMD` cannot be published (`KC-E520`)."),
        ("subscribe", "#pragma kfs subscribe MSG_ID buffer", "The program receives the message into `buffer`, which must be a file-scope object of the message's `kfs_msg_*_t` type."),
        ("claim", "#pragma kfs claim ALLOCATION", "The program takes actuator allocation and may write the command block. `ALLOCATION` is the only claim; not available on the Sequencer (`KC-E520`). Stock services that steer the game's flight computer then fail with `KC-E501`."),
        ("strict", "#pragma kfs strict", "Accepted by the parser but not implemented in this build: reported as `KC-E000` (\"not implemented until MS13\")."),
    ];

    /// <summary>The task and init options of <c>#pragma kfs task</c> and the kASM <c>.task</c> directive.</summary>
    public static IReadOnlyList<(string Name, string Doc)> TaskOptions { get; } =
    [
        ("rate_hz", "The task's rate in Hz; must divide the tick rate (`KC-E407` / `KA-E407`, with the nearest valid rates in the hint)."),
        ("divisor", "Run every N-th tick (1–65535)."),
        ("priority", "0–255; higher runs first among tasks of equal divisor."),
    ];

    /// <summary>kASM directives (the assembler's directive switch): name, syntax, meaning.</summary>
    public static IReadOnlyList<(string Name, string Syntax, string Doc)> KasmDirectives { get; } =
    [
        (".include", ".include <kfs.kinc> | .include <vehicle.kinc> | .include \"file\"", "`<kfs.kinc>` adds the table constants and types (`SVC_*`, `sys.*`, `TRAP_*`, catalogue enums, vector types); `<vehicle.kinc>` the layout's (`commands.*`, `sensors.*`, `*_COUNT`, `MSG_*`, message structs). A built-in include takes effect for the whole assembly. `\"file\"` splices a project file in textually (no include guard)."),
        (".macro", ".macro NAME [p1, p2 …]", "Starts a macro; the body runs to `.endm`, with `\\p1` … substituted at each invocation."),
        (".endm", ".endm", "Ends a `.macro`."),
        (".equ", ".equ name, value", "A constant: an integer expression, or a float literal with a suffix (`1.5f`, `1.5d`). Evaluated when the line is reached."),
        (".string", ".string name \"text\"", "A channel string (for `SVC_TRACE`); `name` evaluates to its string-table index."),
        (".format", ".format name \"fmt\"", "A log format (for `SVC_LOG`); `name` = string index, plus `name.size` (argument-buffer bytes) and `name.arg0`, `name.arg1`, … (offset of each value)."),
        (".global", ".global name, size[, align]", "Zero-initialised RAM: size 0–1,048,576, align 1, 2, 4 or 8 (default 8 when size ≥ 8, else 4)."),
        (".data", ".data name … .enddata", "Initialised RAM, filled with `.byte`/`.half`/`.word`/`.long`/`.float`/`.double`/`.zero` items."),
        (".const", ".const name … .endconst", "Initialised read-only data (`STG` to it is `KA-E209`)."),
        (".enddata", ".enddata", "Ends a `.data` or `.const` block."),
        (".endconst", ".endconst", "Ends a `.data` or `.const` block."),
        (".byte", ".byte v, …", "1-byte data items (−128…255)."),
        (".half", ".half v, …", "2-byte data items (−32768…65535)."),
        (".word", ".word v, …", "4-byte data items, or a global's address (`sym ± n`, emitted as a relocation)."),
        (".long", ".long v, …", "8-byte integer data items."),
        (".float", ".float v, …", "float32 data items (literal with suffix `f`, or a float32 `.equ`)."),
        (".double", ".double v, …", "float64 data items (literal with suffix `d`, or a float64 `.equ`)."),
        (".zero", ".zero n", "n zero bytes (0…1,048,576)."),
        (".proto", ".proto \"C prototype\"", "Attaches a C prototype to the next `.func`: its cells must match `.args` (`KA-E310`) and `RET n` its result; exported to kC through `<file>.kasm.h`."),
        (".extern", ".extern f[, g …]", "Declares functions defined outside this assembly (kC functions of a mixed project) so `CALL` accepts them."),
        (".func", ".func name … .endfunc", "A function (no nesting). `ENTER` is generated from `.args`/`.local`; the last instruction must be `RET` or `JMP` (`KA-E333`)."),
        (".endfunc", ".endfunc", "Ends a `.func`."),
        (".args", ".args a, b, …", "4-byte argument cells in call order; before every `.local` and instruction."),
        (".args64", ".args64 t, …", "8-byte (two-cell) arguments, 8-aligned."),
        (".local", ".local i | .local buf, size, align | .local p : type", "A 4-byte local, or an untyped block (size 0–65,528, align 1/2/4/8); an optional `: type` annotation is for the debugger. In an operand a slot name is its frame offset."),
        (".local64", ".local64 acc", "An 8-byte local, 8-aligned."),
        (".loc", ".loc file:line:col", "Records a source location item (no effect for kASM's own debug lines)."),
        (".scope", ".scope a, b", "Records a scope item; no effect in this build."),
        (".endscope", ".endscope", "Ends a `.scope`."),
        (".loop_bound", ".loop_bound label, N", "The loop headed by `label` runs at most N times, so the task gets a WCET; without it the loop draws `KA-W711`."),
        (".tick_hz", ".tick_hz N", "The scheduler tick rate, at most once (`KA-E406`); N one of {tick} Hz, default {default}."),
        (".program", ".program func [share=S]", "Makes `func` the program task, the only one that may `YIELD` (at most one, `KA-E405`); `share` 1–100 percent."),
        (".init", ".init func", "Makes `func` the init task (at most one)."),
        (".task", ".task func rate_hz=N | divisor=N [priority=P]", "Makes `func` a periodic task; it must take no arguments and return nothing (`KA-E401`)."),
        (".publish", ".publish MSG_NAME buffer", "Defines the global `buffer` (the message's size, align 8) and publishes it."),
        (".subscribe", ".subscribe MSG_NAME buffer", "Defines the global `buffer` (the message's size, align 8) and subscribes it."),
        (".claim", ".claim ALLOCATION", "The program takes actuator allocation and may write the command block; not on the Sequencer (`KA-E520`)."),
        (".strict", ".strict", "Accepted and ignored in this build."),
        (".bounds", ".bounds", "Accepted and ignored in this build."),
    ];

    /// <summary>Operand words of kASM expressions.</summary>
    public static IReadOnlyList<(string Name, string Syntax, string Doc)> KasmOperators { get; } =
    [
        ("sizeof", "sizeof(type)", "The size in bytes of a kASM type (a scalar, a `<kfs.kinc>` vector type or a `<vehicle.kinc>` struct)."),
        ("alignof", "alignof(type)", "The alignment of a kASM type."),
        ("offsetof", "offsetof(type, field)", "The byte offset of a field; the field may be a dotted/indexed path (`offsetof(kfs_commands_t, thruster[0].pulse_s)`)."),
    ];
}
