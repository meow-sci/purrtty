---
type: Language Reference
title: kC language reference
description: The kC source language as the in-game compiler accepts it — lexical rules, declarations, statements, expressions, what C99 it drops, and the hard translation limits.
resource: ksa-decomp:KSA.Kfs.KC/Parser.cs
tags: [kfs, language, kc, syntax, c99, limits]
sources:
  - id: lexer
    resource: ksa-decomp:KSA.Kfs.KC/Lexer.cs
    title: Lexer (decompiled)
  - id: parser
    resource: ksa-decomp:KSA.Kfs.KC/Parser.cs
    title: Parser (decompiled)
  - id: sema
    resource: ksa-decomp:KSA.Kfs.KC/Sema.cs
    title: Sema (decompiled)
  - id: checks
    resource: ksa-decomp:KSA.Kfs.KC/Checks.cs
    title: Checks (decompiled)
  - id: lowering
    resource: ksa-decomp:KSA.Kfs.KC/Lowering.cs
    title: Lowering (decompiled)
  - id: frontend
    resource: ksa-decomp:KSA.Kfs.KC/KcFrontEnd.cs
    title: KcFrontEnd (decompiled)
  - id: limits
    resource: ksa-decomp:KSA.Kfs.Compiler/TranslationLimits.cs
    title: TranslationLimits (decompiled)
  - id: compiler
    resource: ksa-decomp:KSA.Kfs.Compiler/Compiler.cs
    title: Compiler (decompiled)
  - id: diagbag
    resource: ksa-decomp:KSA.Kfs.Compiler/DiagnosticBag.cs
    title: DiagnosticBag (decompiled)
generated: { by: claude-code/claude-fable-5-2, at: 2026-11-25T21:30:48Z }
status: stable
ksa_build: "2026.11.25.5949"
---

# Scope

kC is the C-like language of kFS. This document describes the language a player types into a `.c`
file, as implemented by the hand-written recursive-descent front end in `KSA.Kfs.KC`. It is a
**C99 subset with flight-software restrictions** (the compiler predefines `__STDC_VERSION__` as
`199901L` and reports C11/C23 constructs as errors). Companion documents:

- types and conversions: [kC types and conversions](/language/kc-types-and-conversions.md)
- `#include`, macros, `#pragma`: [kC preprocessor](/language/kc-preprocessor.md)
- tasks, tick rate, entry points: [kC program structure](/language/kc-program-structure.md)
- `kfs_*` calls, sensors, commands, bus: [kC builtins and intrinsics](/language/kc-builtins-and-intrinsics.md)
- every diagnostic named below: [kC diagnostics](/language/kc-diagnostics.md)

Diagnostic codes are written `KC-E012` / `KC-W006` (prefix, `E`rror or `W`arning, three-digit rule
number).[^diagbag-format]

# Lexical rules

## Source text

- `\r\n` and lone `\r` are normalised to `\n` before lexing.[^lexer-ctor]
- White space is space, tab, vertical tab, form feed and newline.[^lexer-ws]
- A backslash immediately followed by a newline is removed **when it is met between tokens** (the
  lexer checks for it in its skip loop, not inside an identifier or number).[^lexer-ws]
- Comments: `// …` to end of line and `/* … */`. An unterminated block comment is `KC-E012`
  "unterminated comment".[^lexer-comments]
- A character that starts no token is `KC-E012` "stray 'c' in the program".[^lexer-stray]

## Identifiers and keywords

An identifier starts with a letter (`char.IsLetter`, so Unicode letters pass) or `_` and continues
with letters, digits or `_`.[^lexer-ident]

The parser's reserved-word set (an identifier in this set can never be a variable, member or tag
name):[^parser-keywords]

```text
auto break case char const continue default do double else enum extern float for goto if inline
int long register restrict return short signed sizeof static struct switch typedef union unsigned
void volatile while _Bool _Complex _Imaginary _Static_assert _Generic _Atomic _Thread_local
_Alignof _Alignas _Noreturn nullptr constexpr typeof __kfs_pragma__ __at __kfs_offsetof
```

`__kfs_pragma__`, `__at` and `__kfs_offsetof` are kC-internal: the preprocessor rewrites
`#pragma kfs …` into `__kfs_pragma__ … @`, `__at(addr)` places an object at a fixed address (system
headers only), and `<stddef.h>` defines `offsetof(t, m)` as `__kfs_offsetof(t, m)`.

## Punctuators

Longest match over this fixed list of 48:[^lexer-puncts]

```text
...  <<=  >>=  ->  ++  --  <<  >>  <=  >=  ==  !=  &&  ||  *=  /=  %=  +=  -=  &=  ^=  |=  ##
[  ]  (  )  {  }  .  &  *  +  -  ~  !  /  %  <  >  ^  |  ?  :  ;  =  ,  #
```

Digraphs and trigraphs are not in the list.

## Numeric literals

A number token starts with a digit, or `.` followed by a digit, and runs over letters, digits, `.`
and `_`, taking a sign after `e`/`E`/`p`/`P`.[^lexer-number] It is then classified:

| form | rule |
| --- | --- |
| decimal `123` | base 10 |
| hex `0x1F` | base 16, prefix case-insensitive |
| octal `017` | any literal longer than one character that starts with `0` and is not hex; a digit 8 or 9 makes it invalid |
| suffixes | `u`, `l`, `ll` in any case and order (`10u`, `5UL`, `0x7FFFFFFFFFFFFFFFull`); more than one `u`, or any other letter, makes it invalid[^lexer-parseint] |
| float `1.5`, `1e-3`, `.5` | parsed as `double`; suffix `f` makes it `float`; suffix `l` is accepted and gives `double`[^lexer-parsefloat][^parser-number] |
| hex float `0x1.8p3` | mantissa in hex, mandatory `p` exponent[^lexer-parsefloat] |

There is no binary (`0b…`) literal and no digit separator. A token that is neither a valid integer
nor a valid float is `KC-E101` "'…' is not a valid constant".[^parser-number] The type an integer
literal receives is in [kC types and conversions](/language/kc-types-and-conversions.md).

## Character and string literals

- `'x'` is one byte; its value has type `int` and is the **signed** byte value. Empty and
  multi-character constants are `KC-E101`.[^parser-char]
- `"…"` is a `const char[n]` including the terminating NUL; adjacent string literals concatenate.
  Source characters are encoded as UTF-8 bytes.[^parser-string][^lexer-unescape]
- Wide literals (`L'x'`, `L"…"`) are `KC-E101` "wide character and string literals aren't in
  kC".[^lexer-wide]
- A literal may not span lines (`KC-E012` "unterminated string literal" / "unterminated character
  constant").[^lexer-wide]

Escape sequences:[^lexer-unescape]

| escape | value |
| --- | --- |
| `\n \t \r \a \b \f \v` | 10, 9, 13, 7, 8, 12, 11 |
| `\0` … `\777` | up to three octal digits, value ≤ 255 |
| `\xHH…` | hex digits, value ≤ 255, at least one digit |
| `\" \' \? \\` | the character itself |

Any other escape is an error (`KC-E101`, text such as "unknown escape '\q'").

# Translation units and scope

Every `.c` file of the project is one translation unit; `.h` files exist only through `#include`.
Names have file scope or block scope; each `{ }` block, each function body and each `for` header
opens a scope for both ordinary names and struct/enum tags.[^parser-scopes] Using an undeclared name
is `KC-E003`, with a "Did you mean …?" hint when a declared name is within edit distance
2.[^parser-undeclared]

A function must be declared before it is called — there is no implicit declaration
(`KC-E003` "'f' is not declared").[^parser-undeclared]

# Declarations

## Specifiers

| specifier | effect |
| --- | --- |
| `typedef` | declares a type name[^parser-declspecs] |
| `static` | internal linkage at file scope; static storage duration in a block |
| `extern` | declaration without storage |
| `inline` | accepted and recorded; nothing else in the front end reads the flag |
| `const`, `volatile` | type qualifiers (see below) |
| `register`, `restrict`, `auto` | accepted and **ignored**[^parser-ignored] |
| `signed`, `unsigned`, `short`, `long`, `char`, `int`, `float`, `double`, `void`, `_Bool` | arithmetic types, combinable as in C |
| `struct tag {…}`, `enum tag {…}`, typedef names | user types |

A declaration with no type specifier is `KC-E012` "expected a type" (hint: "An implicit `int` isn't
C99").[^parser-notype] `long double` is accepted and is plain `double`.[^parser-longdouble]

## Storage and linkage

| where | form | meaning |
| --- | --- | --- |
| file scope | `int x;` / `int x = 3;` | global with external linkage; visible to other `.c` files of the project through `extern` |
| file scope | `static int x;` | global private to the file; `KC-W006` if never used[^frontend-unused] |
| file scope | `extern int x;` | declaration; must be defined in some unit if used (`KC-E304`)[^frontend-link] |
| block | `int x;` | automatic local, one frame slot |
| block | `static int x = 3;` | static-duration local; emitted as a global named `function.x`[^parser-staticlocal] |
| block | `extern int x;` | refers to a global; may not have an initialiser[^parser-blockextern] |

There is no heap: `malloc`, `calloc`, `realloc` and `free` are declared by `<stdlib.h>` only so the
compiler can reject them (`KC-E302` "flight code can't allocate memory at run time").[^parser-malloc]

`const` objects are read-only: assigning to one, or to a member/element of one, is `KC-E209`.
A struct that has a `const` member cannot be assigned as a whole.[^sema-const]

**A `const` variable is not a constant expression.** Array sizes, `case` labels, enumerator values
and static initialisers need literals, enumerators, `sizeof`, or macros (`KC-E105`, with a hint
naming `enum { N = … }` or `#define`).[^parser-constexpr]

## Declarators

- Pointers: `T *p`, `T *const p`, `const T *p`; any depth up to the nesting limit.
- Arrays: `T a[N]`, multi-dimensional `T a[N][M]`; `N` must be an integer constant expression,
  1 … 1,048,576, and the whole array at most 1 MiB (`KC-E101` for a non-positive size, `KC-E726` for
  "larger than the largest RAM").[^parser-arraysize] An unsized `T a[]` is allowed only with an
  initialiser, or as `extern`.[^parser-incomplete]
- Functions: `R f(void)`, `R f(T a, U b)`, and the unprototyped `R f()`. Array parameters decay to
  pointers.[^parser-params]
- Parenthesised declarators such as `int (*p)[3]` (pointer to array) parse; anything that yields a
  **pointer to function** is rejected (see "Unsupported").[^parser-declarator]

`__at(address)` after a declarator fixes the object's address. It is accepted **only in system
headers** (`KC-E012` "`__at` is for the system headers" elsewhere) and only on `extern` file-scope
objects.[^parser-at] This is how `sys`, `commands` and (bench layout only) `sensors` are mapped; see
[kC generated headers](/language/kc-generated-headers.md).

## Initialisers

| target | accepted forms |
| --- | --- |
| scalar | `= expr`, `= { expr }` |
| array | `= { a, b, c }`, with optional index designators `[i] = v`; fewer initialisers than elements leaves the rest zero; an unsized array takes its length from the list[^parser-braced] |
| char array | `= "text"` (the NUL is dropped silently if the array is exactly the text length; longer text is `KC-E101`)[^parser-stringinit] |
| struct | `= { a, b }`, with optional member designators `.m = v`; or `= other_struct` of a compatible type (locals only)[^parser-braced] |
| nested aggregates | nested braces, or brace elision (a flat run of values fills the leaves in order)[^parser-initelement] |

Designators are single-level: `[i] =` inside an array list and `.m =` inside a struct list. A member
designator in an array list, an index designator in a struct list, and chained designators
(`.a.b =`, `[i][j] =`) are syntax errors.[^parser-braced]

Globals and `static` locals must be initialised by constants: arithmetic constants, string literals
for char arrays, and **address constants** (`&global`, `&global.member`, `&array[const]`, array
names, with constant offsets), which become relocations. Anything else is `KC-E105` "the initialiser
of a global or `static` object must be a constant expression" (hint: "Assign it in `init` or the task
instead").[^parser-staticinit]

Automatic locals may be initialised by any expression. An aggregate local with an initialiser is
zero-filled first, then the listed members are stored.[^lowering-localinit] A local with no
initialiser is not zeroed; reading it before a store on some path is `KC-W206` "may be used before it
is set" (scalars only — aggregates and address-taken locals are exempt).[^checks-uninit]

# Structs, enums, unions

**Structs.** `struct tag { members };`, anonymous `struct { … }`, forward declaration `struct tag;`,
self-reference through pointers, nesting, arrays of structs, struct members of struct type. Members
are laid out in declaration order with natural alignment (see the types document). A struct needs at
least one member; a member cannot be a function, cannot have an incomplete type (so no flexible array
member), and cannot carry a storage class.[^parser-struct] Structs are assigned, passed and returned
**by value** (size limit 65,535 bytes, `KC-E716`).[^sema-structsize]

**Enums.** `enum tag { A, B = 5, C };`. Enumerators are `int` constants; each value must fit
`int`/`unsigned int` range (`KC-E101`). The enum type is 4 bytes, unsigned when every enumerator is
non-negative. `enum tag` may be named before it is defined.[^parser-enum] A `switch` over an enum
value with no `default` that misses an enumerator draws `KC-W208`.[^parser-switch]

**Unions and bitfields** do not exist: `union` is `KC-E641` "`union` is not in kC v1" and
`int x : 3;` is `KC-E641` "bitfields are not in kC v1".[^parser-union]

# Functions

- Definitions use prototype syntax. A K&R-style definition is `KC-E650`.[^parser-knr]
- `main` may not be defined (`KC-E409` "kC programs have no `main`"); entry points are declared with
  `#pragma kfs task` / `#pragma kfs init`.[^parser-main]
- User-defined variadic functions are `KC-E643` ("Only `kfs_logf` is variadic in kC").[^parser-main]
- Every parameter of a definition needs a name.[^parser-fnbody]
- Calls are **direct only**: the callee must be a function name. `(*p)(…)`, `table[i](…)` and using
  a function name as a value are `KC-E303`.[^parser-call][^sema-fnvalue]
- Argument count must match the prototype (`KC-E305`); arguments convert as by
  assignment.[^sema-call]
- **Recursion is forbidden**, direct or mutual (`KC-E301`, reported once per cycle with the call
  chain).[^checks-recursion]
- Falling off the end of a non-`void` function is `KC-W307`; the compiler returns zero
  there.[^lowering-fallthrough]
- Calling a non-`void` function and discarding the result is `KC-W207` unless the call is cast to
  `(void)` or parenthesised; `memcpy`, `memmove` and `memset` are exempt.[^parser-ignored-result]

# Statements

| statement | notes |
| --- | --- |
| `{ … }` | block; declarations and statements may be mixed (C99)[^parser-stmt] |
| `expr;` and `;` | expression statement, null statement |
| `if (c) s` / `else s` | condition must be scalar[^sema-condition] |
| `while (c) s`, `do s while (c);` | |
| `for (init; c; step) s` | `init` may be a declaration, scoped to the loop[^parser-for] |
| `switch (e) { case K: … default: … }` | `e` must be an integer; labels are integer constant expressions; duplicates are `KC-E106`[^parser-switch] |
| `break;`, `continue;` | outside a loop/switch: `KC-E013`[^parser-break] |
| `return;` / `return e;` | must match the function's return type (`KC-E211`)[^parser-return] |
| `_Static_assert(c, "msg");` | allowed at file scope, in blocks and inside struct bodies; failure is `KC-E107`[^parser-staticassert] |

`goto`, statement labels (`name:`), `setjmp` and `longjmp` are `KC-E640`.[^parser-goto]

An assignment used directly as a condition draws `KC-W648` "did you mean `==`?" unless it is
parenthesised.[^sema-condition]

**Loop bounds.** For every `while`, `do` and `for` in player code the compiler tries to prove an
iteration bound and warns `KC-W711` "can't prove this loop ends" when it cannot. A loop is provable
when its condition compares one integer counter (at most 32 bits, not `_Bool`, address never taken)
with a constant using `<`, `<=`, `>`, `>=` or `!=`, and the counter has exactly one write in the loop
— an unconditional `++`, `--`, `+= const` or `-= const`. When the counter is a global or a `static`
local, every function call inside the loop counts as another write to it, so the proof fails; use an
automatic local. A constant-false condition is also accepted; `while (1)` and `for (;;)` are
not.[^checks-loopbound] Library code is analysed the same way but never warned about. The bound feeds worst-case-cycle analysis; see
[program analysis and WCET](/compiler/program-analysis-and-wcet.md).

# Expressions

## Precedence and associativity

Highest to lowest. Binary levels are left-associative.[^parser-levels][^parser-exprs]

| level | operators | notes |
| --- | --- | --- |
| primary | literal, name, `( expr )` | |
| postfix | `a[i]`  `f(args)`  `s.m`  `p->m`  `x++`  `x--` | |
| unary | `++x`  `--x`  `&x`  `*p`  `+x`  `-x`  `~x`  `!x`  `sizeof`  `_Alignof(T)`  `offsetof(T, m)` | right-to-left |
| cast | `(T) x`, compound literal `(T){ … }` | |
| multiplicative | `*`  `/`  `%` | `%` integers only |
| additive | `+`  `-` | pointer arithmetic allowed |
| shift | `<<`  `>>` | integers only |
| relational | `<`  `>`  `<=`  `>=` | result `int` |
| equality | `==`  `!=` | result `int` |
| bitwise | `&`, then `^`, then `\|` | three separate levels, integers only |
| logical | `&&`, then `\|\|` | short-circuit, result `int` |
| conditional | `c ? a : b` | right-associative |
| assignment | `=`  `*=`  `/=`  `%=`  `+=`  `-=`  `<<=`  `>>=`  `&=`  `^=`  `\|=` | right-associative |
| comma | `a, b` | restricted, see below |

## Notes per construct

- **Lvalues.** Assignment, `++`, `--` and `&` need a modifiable lvalue: a variable, element, member
  or dereference (`KC-E212`). Arrays are not assignable ("copy with memcpy").[^sema-modifiable]
- **`sizeof`** takes a type in parentheses or an expression (not evaluated) and has type
  `unsigned int`; **`_Alignof(type)`** is accepted although it is C11.[^parser-sizeof]
- **`offsetof(type, member-path)`** (from `<stddef.h>`) accepts `a.b[2].c` paths with constant
  indices.[^parser-offsetof]
- **Casts** target scalar types or `void` only (`KC-E211` "Only scalar types are cast targets in
  C"). Casting between an integer and a pointer is `KC-E644`, except the null constant
  `0`.[^parser-cast][^sema-intptr]
- **Compound literals** `(T){ … }` are allowed inside functions only (`KC-E105` at file scope); an
  aggregate one is an anonymous local in the enclosing function's frame.[^parser-compound]
- **Array indexing** is bounds-checked at run time when the array's length is known (a `BOUNDS`
  instruction, on by default; `#pragma kfs bounds off` or the build option removes it). Taking
  `&a[i]` skips the check.[^lowering-bounds][^sema-addrof]
- **Pointer arithmetic**: pointer ± integer, pointer − pointer (same pointee type), pointer
  comparison, comparison with null. The pointee must be a complete type no larger than 65,535
  bytes.[^sema-pointer]
- **Comma operator.** A comma expression inside parentheses is `KC-E649` "the comma operator outside
  a `for` header". The parser accepts an unparenthesised comma expression wherever a full expression
  statement, a `for` init/step clause or a subscript is parsed.[^parser-comma]
- **Float equality.** `==` / `!=` on `float` or `double` operands draws `KC-W250`.[^sema-floateq]
- **Division by zero** in a constant expression is not folded; the expression then fails as "not a
  constant" where one is required. At run time it traps.[^sema-divzero]

# Unsupported C

| construct | result |
| --- | --- |
| `union`, bitfields | `KC-E641`[^parser-union] |
| `goto`, labels, `setjmp`/`longjmp` | `KC-E640`[^parser-goto] |
| function pointers (declaring, taking a function's address, calling through one, function-typed members/parameters) | `KC-E303`[^parser-declarator] |
| recursion | `KC-E301`[^checks-recursion] |
| variable-length arrays | `KC-E642`[^parser-arraysize] |
| user variadic functions | `KC-E643`[^parser-main] |
| integer ↔ pointer casts | `KC-E644`[^sema-intptr] |
| dynamic allocation | `KC-E302`[^parser-malloc] |
| `printf`, `puts` | `KC-E645` "kC has no standard output" (use `kfs_logf` or `trace`)[^parser-printf] |
| `_Alignas`, `_Atomic`, `_Generic`, `_Noreturn`, `_Thread_local`, `typeof`, `nullptr`, `constexpr` | `KC-E646` "C11/C23 construct"[^parser-c11] |
| `__attribute__`, `__asm__`/`asm`, `__extension__`, `__typeof__`, `__builtin_expect` | `KC-E647` "GNU extension"[^parser-gnu] |
| K&R function definitions | `KC-E650`[^parser-knr] |
| `main` | `KC-E409`[^parser-main] |
| wide character/string literals | `KC-E101`[^lexer-wide] |
| `_Complex`, `_Imaginary` | reserved words with no handling: a declaration using one fails with a generic `KC-E012`[^parser-keywords] |
| flexible array members, empty structs | `KC-E213`, `KC-E012`[^parser-struct] |
| a parenthesised comma expression | `KC-E649`[^parser-comma] |

# Hard limits

| limit | value | on violation |
| --- | --- | --- |
| nesting of blocks, parentheses, brackets, initialisers, declarators, casts, postfix operators | 128 | `KC-E720`[^parser-nest][^limits-consts] |
| type nesting (pointers, arrays, functions, struct members) | 128 | `KC-E720`[^parser-nest] |
| binary operators in one full expression | 1,000 | `KC-E721`[^parser-countop] |
| `#include` depth | 32 | `KC-E722` |
| `#include`s per translation unit | 1,000 | `KC-E723` |
| tokens after preprocessing, per unit | 150,000 | `KC-E724` |
| tokens produced by macro expansion, per unit | 200,000 | `KC-E725` |
| size of one object or array | 1,048,576 bytes | `KC-E726`[^frontend-objsize] |
| aggregate copied, passed or returned by value; array element size | 65,535 bytes | `KC-E716`[^sema-structsize] |
| one local variable; one function's frame | 65,528 bytes | `KC-E717`[^parser-localsize][^lowering-frame] |
| argument cells of one function (a cell is 4 bytes; 64-bit arguments take two, aligned to an even cell) | 255 | `KC-E719`[^parser-fnbody] |
| errors reported before the compiler stops | 100 | "too many errors; stopping"[^diagbag-max] |
| compile wall time | 30 s (`CompileOptions.TimeLimitMs`) | `KC-E900`[^compiler-thread] |

The preprocessor limits are detailed in [kC preprocessor](/language/kc-preprocessor.md). Limits on
program size, RAM, stack and task count belong to the back end; see
[compile pipeline](/compiler/compile-pipeline.md) and
[catalogue and processors](/tables/catalogue-processors.md).

# Modding notes

- The grammar is hard-coded in `Parser` (a `public sealed` class with only private parsing methods).
  There is no extension hook for new syntax; a mod that wants new *callable* features should add
  functions and headers, not syntax — see [kC generated headers](/language/kc-generated-headers.md).
- The reserved-word set is a `private static readonly HashSet<string>`; `__at` being restricted to
  system headers is enforced by checking the token's file against the set of files that were included
  with `<…>`.[^parser-at] A mod-supplied *system* header may therefore use `__at` to map objects at
  fixed addresses, exactly as `<kfs.h>` and `<vehicle.h>` do.
- Compilation runs on a dedicated background thread named "kFS compile" with a 16 MiB stack, and the
  caller blocks for at most the time limit.[^compiler-thread]

[^diagbag-format]: DiagnosticBag.cs:19-35
[^diagbag-max]: DiagnosticBag.cs:7, 17-29
[^lexer-ctor]: Lexer.cs:36-40
[^lexer-ws]: Lexer.cs:53-76
[^lexer-comments]: Lexer.cs:77-103
[^lexer-stray]: Lexer.cs:219
[^lexer-ident]: Lexer.cs:113-123
[^lexer-puncts]: Lexer.cs:25-32, 222-233
[^lexer-number]: Lexer.cs:124-166
[^lexer-parseint]: Lexer.cs:268-337
[^lexer-parsefloat]: Lexer.cs:346-389
[^lexer-wide]: Lexer.cs:167-201
[^lexer-unescape]: Lexer.cs:391-501
[^parser-keywords]: Parser.cs:81-88
[^parser-number]: Parser.cs:3653-3738
[^parser-char]: Parser.cs:3436-3451
[^parser-string]: Parser.cs:3560-3610
[^parser-scopes]: Parser.cs:256-318, 2193-2238
[^parser-undeclared]: Parser.cs:3516-3547, 3612-3651
[^parser-declspecs]: Parser.cs:681-864
[^parser-ignored]: Parser.cs:791-795
[^parser-notype]: Parser.cs:853-857
[^parser-longdouble]: Parser.cs:824-827
[^parser-staticlocal]: Parser.cs:2513-2534
[^parser-blockextern]: Parser.cs:2476-2502
[^parser-malloc]: Parser.cs:3401-3406
[^parser-constexpr]: Parser.cs:591-619
[^parser-arraysize]: Parser.cs:1229-1277, 1317-1322
[^parser-incomplete]: Parser.cs:1632-1636
[^parser-params]: Parser.cs:1435-1516
[^parser-declarator]: Parser.cs:1170-1349, 1409-1418
[^parser-at]: Parser.cs:1351-1374, 1648-1651, 2440-2444
[^parser-braced]: Parser.cs:1806-1883
[^parser-stringinit]: Parser.cs:1784-1804
[^parser-initelement]: Parser.cs:1885-1961
[^parser-staticinit]: Parser.cs:1668-1733
[^parser-struct]: Parser.cs:880-993
[^parser-enum]: Parser.cs:995-1064
[^parser-union]: Parser.cs:783-785, 953-957
[^parser-knr]: Parser.cs:1458-1481
[^parser-main]: Parser.cs:1589-1597
[^parser-fnbody]: Parser.cs:1988-2042
[^parser-call]: Parser.cs:3026-3061
[^parser-ignored-result]: Parser.cs:2388-2421
[^parser-stmt]: Parser.cs:2044-2134, 2378-2386
[^parser-for]: Parser.cs:2193-2238
[^parser-switch]: Parser.cs:2239-2323
[^parser-break]: Parser.cs:2324-2339
[^parser-return]: Parser.cs:2340-2363
[^parser-staticassert]: Parser.cs:331-334, 566-589, 934-938, 2125-2129
[^parser-goto]: Parser.cs:2130-2134, 2364-2375
[^parser-levels]: Parser.cs:90-139
[^parser-exprs]: Parser.cs:2562-2698, 2857-2943, 3011-3083
[^parser-sizeof]: Parser.cs:2889-2937
[^parser-offsetof]: Parser.cs:2945-2996
[^parser-cast]: Parser.cs:2663-2698
[^parser-compound]: Parser.cs:2700-2747
[^parser-comma]: Parser.cs:2568-2581, 3455-3468
[^parser-printf]: Parser.cs:3407-3413
[^parser-c11]: Parser.cs:761-770, 3474-3494
[^parser-gnu]: Parser.cs:771-777, 1375-1400, 2367-2371, 3495-3511
[^parser-nest]: Parser.cs:228-244
[^parser-countop]: Parser.cs:2583-2589
[^parser-localsize]: Parser.cs:2548-2552, 2737-2741
[^sema-const]: Sema.cs:972-1029
[^sema-structsize]: Sema.cs:1055-1058, 1442-1445, 1457-1460
[^sema-fnvalue]: Sema.cs:227-231, 1343-1349
[^sema-call]: Sema.cs:1397-1462
[^sema-condition]: Sema.cs:487-500
[^sema-modifiable]: Sema.cs:972-1024
[^sema-intptr]: Sema.cs:263-276
[^sema-addrof]: Sema.cs:1362-1365
[^sema-pointer]: Sema.cs:690-740, 800-820
[^sema-floateq]: Sema.cs:775-784
[^sema-divzero]: Sema.cs:881-887, 939-944
[^checks-recursion]: Checks.cs:20-124
[^checks-uninit]: Checks.cs:220-232, 387-400
[^checks-loopbound]: Checks.cs:497-657
[^lowering-localinit]: Lowering.cs:460-490
[^lowering-fallthrough]: Lowering.cs:130-146
[^lowering-bounds]: Lowering.cs:767-783
[^lowering-frame]: Lowering.cs:148-151
[^frontend-unused]: KcFrontEnd.cs:181-195
[^frontend-link]: KcFrontEnd.cs:488-506
[^frontend-objsize]: KcFrontEnd.cs:738-745
[^limits-consts]: TranslationLimits.cs:7-21
[^compiler-thread]: Compiler.cs:20-59
