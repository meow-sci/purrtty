---
type: Subsystem Reference
title: kASM disassembler
description: The text Disassembler.Disassemble produces from a loaded kVM image, what it preserves (the code section, byte for byte) and what it drops, and the tools built on it.
resource: ksa-decomp:KSA.Kfs.KAsm/Disassembler.cs
tags: [kfs, kasm, disassembler, tooling, image]
sources:
  - id: disassembler
    resource: ksa-decomp:KSA.Kfs.KAsm/Disassembler.cs
    title: Disassembler (decompiled)
  - id: image-reader
    resource: ksa-decomp:KSA.Kfs.Vm.Image/ImageReader.cs
    title: ImageReader (decompiled)
  - id: instr
    resource: ksa-decomp:KSA.Kfs.Vm.Image/Instr.cs
    title: Instr and FunctionRange (decompiled)
  - id: assembler
    resource: ksa-decomp:KSA.Kfs.KAsm/Assembler.cs
    title: kASM Assembler (decompiled)
  - id: cli
    resource: ksa-decomp:KSA.Kfs.Cli/Cli.cs
    title: kvm-cli (decompiled)
  - id: workbench
    resource: ksa-decomp:KSA.Kfs.Host.Workbench/Workbench.cs
    title: Workbench (decompiled)
  - id: disassembly-view
    resource: ksa-decomp:KSA.Kfs.Host.Views/DisassemblyView.cs
    title: DisassemblyView (decompiled)
generated: { by: claude-code/claude-fable-5-2, at: 2026-11-25T21:29:02Z }
status: stable
ksa_build: "2026.11.25.5949"
---

# API

```csharp
public static class Disassembler {
    public static string Disassemble(KvmImage image, GameTables tables);
}
```

It takes an already-validated `KvmImage` (the product of `ImageReader.Read`), never raw bytes, and
returns [kASM](/compiler/kasm-assembly-language.md) source text.[^disassembler] It relies on the
decoding the reader has done:

- `image.Instrs` — one `Instr { Op, Pc, A, B, Cost, Soft }` per instruction. For `JMP`/`JZ`/`JNZ` and
  `CALL`/`CALLF` the reader has replaced the operand `A` with the **index of the target instruction**.
- `image.Functions` — `FunctionRange(Start, End, FirstInstr, EndInstr)`, one per `ENTER`.
- `image.Tasks`, `image.TickHz`, `image.ClaimsAllocation`, `image.CoreClass`, and `image.Debug` for
  function names when present.

# Output format

```text
; disassembled by kvm-cli; `kvm-cli assemble --image` reassembles it to a byte-identical code section (kASM §6.3, P-041)
.tick_hz <image.TickHz>
[.claim ALLOCATION]

.init <fn>                         ; one line per distinct task entry, in Tasks[] order
.task <fn> divisor=<rate_divisor>
.program <fn>[ share=<program_share>]

.func <fn>
    [.args a0, a1, …]              ; when ENTER's ncells > 0
    [.local frame_rest, <frame − 4·ncells>, 1]   ; when frame ≠ 4·ncells rounded up to 8
[L_<pc as 6 hex digits>:]          ; before every instruction some jump targets
    <MNEMONIC>[ <operand>]
.endfunc

…one .func block per function, in code order, each followed by a blank line…
```

Details:

- **`.claim ALLOCATION`** is printed when the header's claims bit 0 is set, *or* when the core class is
  not 0 (K8) and some `PUSH32`, `STG` or `STG64` has an operand inside the command window
  (`0x90000000 ≤ a < 0x90010000`).
- **Function names** come from the Debug section's function whose `Start` equals the function's
  address. A name is used only if it matches `^[A-Za-z_][A-Za-z0-9_]*$`, has not been used already and
  is not a mnemonic (compared upper-cased); otherwise — and always when the image has no Debug section
  — the name is `f_<start address as 6 lower-case hex digits>`. So kC statics that were renamed
  `name.N` (a dot is not allowed) fall back to `f_…` names.
- **Entry directives**: `.task` carries only `divisor=`; priorities are not stored in an image. A task
  entry address that repeats is printed once.
- **Frame**: `.args a0 … a(n−1)` reproduces `ncells`; `.local frame_rest, n, 1` is a single untyped
  block sized so the reassembled frame equals the original `ENTER` frame.
- **Operands**: jumps print `L_<target pc>`; calls print the callee's name; a single other immediate
  prints as `0x<UPPERCASE HEX>` for `u32` and `u64` immediates and as a signed decimal otherwise. The
  `ENTER` itself is not printed (the assembler regenerates it).

# What survives a round trip

The header comment states the contract: reassembling with `kvm-cli assemble --image` yields a
**byte-identical code section**. That holds because `ImageMode` keeps function order (`KeepOrder`),
takes `CALL`/`CALLF` literally, skips the structural checks a disassembly cannot satisfy, and the
`.args`/`.local` lines regenerate each `ENTER` exactly. See
[kASM assembly language](/compiler/kasm-assembly-language.md#imagemode).

Everything outside the code section is **not** reproduced by the text:

| image content | in the disassembly? |
| --- | --- |
| code | yes, exactly |
| tick rate, ALLOCATION claim, task kinds/divisors/shares | yes |
| globals and their names | no — accesses appear as absolute addresses (`LDG 0x10`) and no `.global` is emitted |
| initialised data records | no |
| strings (`.format`, `.string`, task names) | no — format and channel indices stay as bare numbers |
| bus messages and links (`.publish` / `.subscribe`) | no |
| task priorities, loop bounds, prototypes | no (not in the image at all) |
| debug section | only function names are used |

So a reassembled image is a code-equivalent skeleton, not a working copy of a program that logs,
traces or uses the bus. `Inferred:` from the disassembler printing none of those sections; the
reassembled image was not built here.

# Uses

| consumer | use |
| --- | --- |
| `kvm-cli` disassemble command | loads an image file for the selected part and prints the text[^cli-disasm] |
| workbench console commands | print the whole disassembly of the loaded image, or one function[^workbench-disasm] |
| `DisassemblyView.CopyAsKasm(image, tables, function)` | extracts one `.func … .endfunc` block, rewrites `LDG`/`STG`/`LDG64`/`STG64 0x…` operands back to global names using the Debug section's globals, and wraps it in a commented `.include <kfs.kinc>` file "as a starting point, not a guaranteed reassembly"[^disassembly-view] |

# Modding notes

- The disassembler is table-driven (`tables.Opcodes[op].Mnemonic`, `.Imm`); it has no knowledge of
  services, so a `SYSCALL` to a mod-added service prints as `SYSCALL <decimal id>`. It never prints
  `SVC_*` symbols.
- It needs a `KvmImage`, so only images that pass `ImageReader.Read` against the current tables, part
  and layout can be disassembled.
- `Disassembler` is `public static`; the output is plain text with a stable, line-oriented shape that
  is easy to post-process (the stock `CopyAsKasm` does exactly that with a regular expression).

[^disassembler]: Disassembler.cs:17-242; ImageReader.cs:447, 462; Instr.cs:3-16; FunctionRange.cs:3
[^cli-disasm]: Cli.cs:573-582
[^workbench-disasm]: Workbench.cs:1385-1394, 2070-2074
[^disassembly-view]: DisassemblyView.cs:108-150
