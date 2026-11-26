# Compiler, assembler and image format

The middle and back end of the kFS toolchain for KSA build 2026.11.25.5949: everything between the kC
front end and a loaded kVM image, plus the kASM assembler and disassembler. The kC language itself is
under [/language/](/language/index.md); the machine that runs images is under [/vm/](/vm/index.md).

## Pipeline

- [kFS compile pipeline (Compiler.cs end to end)](/compiler/compile-pipeline.md) — The stages that turn a kC or kASM project into a kVM image — inputs, options, the front-end seam, validation, peephole, emission, results, how kC and kASM mix, how the standard library is pulled in, and every limit.
- [kFS IR reference (IrModule, instructions, validator, printer, IrBuilder)](/compiler/ir-reference.md) — The compiler's intermediate representation — a symbolic form of kVM stack code — with every model type, the instruction set and operand kinds, the validator's rules, the printer's text form and the IrBuilder API.
- [Backend emitter and peephole (IR to kVM image)](/compiler/backend-emitter.md) — How Emitter turns an IrModule into image bytes — task ordering, function placement and far calls, the calling convention and frames, RAM planning, how service calls, memory-mapped I/O and bus buffers appear in code, every emitter diagnostic, and the five peephole rewrites.

## Static analysis

- [Program analysis, stack figures and WCET](/compiler/program-analysis-and-wcet.md) — What ProgramAnalysis proves about a program before an image is built — operand-stack balance, no recursion, per-task stack and call-depth figures, YIELD placement, worst-case execution time — what is rejected at compile time versus only reported, and how the figures relate to the processor catalogue.
- [Bus analysis (static publish/subscribe attribution)](/compiler/bus-analysis.md) — How BusAnalysis decides which task reads and writes which message buffer, the rules it enforces (read-only subscriptions, single publisher, ALLOCATION for the command block), the per-task start and completion costs, and how the result becomes the image's Messages and Links tables.

## Build outputs

- [Compile report (CompileReport and the Report* records)](/compiler/compile-report.md) — Every field of the build report the emitter produces — program memory, RAM, stacks, VM region, bus, calls, WCET and per-line estimates — with its JSON key, how it is computed, and the fixed text rendering.
- [Diagnostics infrastructure (Diagnostic, DiagnosticBag, DiagnosticRegistry, Severity, Span)](/compiler/diagnostics-infrastructure.md) — How compiler and assembler diagnostics are created, numbered, prefixed, capped and rendered, how they relate to the embedded diagnostics table, and every diagnostic number the middle end, back end and assembler emit.

## kASM

- [kASM assembly language](/compiler/kasm-assembly-language.md) — The complete kASM language as implemented by the assembler — line syntax, macros and includes, every directive, instruction operands, the expression grammar, types, every symbol predefined from the game tables and the vehicle layout, limits, diagnostics and verified example programs.
- [kASM disassembler](/compiler/kasm-disassembler.md) — The text Disassembler.Disassemble produces from a loaded kVM image, what it preserves (the code section, byte for byte) and what it drops, and the tools built on it.

## Image format

- [kVM image format (KVM1) and load-time validation](/compiler/kvm-image-format.md) — The binary kVM image byte by byte — header, section directory, messages, tasks, links, strings, enums, code, data and debug sections, the trailing CRC — plus every version and fingerprint baked into an image and every check ImageReader performs against the running tables, part and layout, with each LoadResult rule.
- [Debug info (the image's Debug section and the DebugInfo model)](/compiler/debug-info.md) — The optional Debug section of a kVM image — its record model, exact byte layout, how the emitter fills it, the consistency checks applied at load, and which tools consume it.

## Extending

- [Toolchain extension points (what a mod must touch so programs can use new services, messages, enums or I/O fields)](/compiler/toolchain-extension-points.md) — A consolidated view, from the compiler, assembler and image loader's side, of which symbol tables and checks are derived from GameTables and which are hard-coded, where ids and fingerprints are verified, and the hooks and risks for a mod that adds game features to kFS.
