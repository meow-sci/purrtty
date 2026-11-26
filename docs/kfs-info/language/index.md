# Language

The kC language of the kFS flight computer, as implemented by the `KSA.Kfs.KC` front end in KSA build
2026.11.25.5949: what a player can write, what the compiler does with it, and where a mod can extend
it. Start with the language reference; mod authors should read the generated-headers document first.

## Writing kC

- [kC language reference](/language/kc-language-reference.md) — The kC source language as the in-game compiler accepts it — lexical rules, declarations, statements, expressions, what C99 it drops, and the hard translation limits.
- [kC types and conversions](/language/kc-types-and-conversions.md) — The kC type system as implemented by CType and Sema — sizes and alignment, struct layout, compatibility, implicit conversions, literal typing, constant folding, and how core class and double_bits affect it.
- [kC preprocessor](/language/kc-preprocessor.md) — The kC preprocessor — directives, macro expansion rules, #if evaluation, predefined macros, #pragma handling, and exactly where an #include name is resolved.
- [kC program structure](/language/kc-program-structure.md) — How a kC program is assembled from files and declares its tick rate, init and periodic tasks, bus buffers and ownership through #pragma kfs, plus globals, linkage, symbol naming and per-program limits.
- [kC worked examples](/language/kc-examples.md) — The idioms of real kC programs, drawn from the examples embedded in the game, plus five small complete programs constructed from constructs the front end is known to accept.

## The programming interface

- [kC builtins and intrinsics](/language/kc-builtins-and-intrinsics.md) — Every compiler-recognised function in kC — intrinsics, the kfs_* service bindings and how a call lowers to SYSCALL, kfs_logf and trace, and how kC code reads sensors, writes commands and uses bus messages.
- [kC system and generated headers](/language/kc-generated-headers.md) — Exactly which header text kC sees, where each piece comes from (hard-coded strings versus the services, messages and layout tables), when it is generated and cached, and what a mod must change for new functions, messages, enums or I/O fields to appear.
- [kC standard library](/language/kc-standard-library.md) — Every function of the bundled kC library — math.c, vecmat.c and string.kasm — with signatures and behaviour, and how library source is pulled into a program and pruned.

## Compiler behaviour

- [kC front end internals](/language/kc-front-end-internals.md) — How KSA.Kfs.KC turns source into IR — the Lexer, Preprocessor, Parser, Sema, Checks and Lowering phases, their data structures, where names and builtins are resolved, and which IrBuilder calls lowering makes.
- [kC diagnostics](/language/kc-diagnostics.md) — Catalogue of the diagnostics a kC programmer can hit — code, severity, message text and cause for everything the kC front end emits, plus the table rules that are listed for kC but never raised in this build.

## Other languages

- [kScript status](/language/kscript-status.md) — Everything the decompiled build reveals about kScript, the planned third kFS language — table columns, enum values, diagnostics and back-end plumbing — and the finding that no kScript front end exists in this build.
