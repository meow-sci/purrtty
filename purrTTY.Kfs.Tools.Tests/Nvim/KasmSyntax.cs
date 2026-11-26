using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Text.RegularExpressions;

namespace purrTTY.Kfs.Tools.Tests.Nvim;

/// <summary>
/// Generates <c>purrTTY.GameMod/nvim/syntax/kasm.vim</c> (plan §7.4) from what the game itself
/// knows: the mnemonics of its opcode table and the directive names its assembler compares
/// against. No game types here: the caller extracts the lists (see <c>KasmSyntaxTests</c>).
/// </summary>
internal static partial class KasmSyntax
{
    /// <summary>String literals of the assembler that look like directives but are not: the suffixes it appends to a <c>.format</c> name (<c>fmt.size</c>, <c>fmt.arg0</c>).</summary>
    public static readonly string[] NotDirectives = [".arg", ".size"];

    [GeneratedRegex(@"^\.[a-z][a-z0-9_]*$")]
    private static partial Regex DirectiveLike();

    /// <summary>
    /// Every string literal (<c>ldstr</c>) in the methods of <paramref name="type"/> and its nested
    /// types that looks like a directive (<c>.name</c>), minus <see cref="NotDirectives"/>. The
    /// assembler matches directives with <c>switch</c> statements and <c>==</c> on literals, so its IL
    /// carries every one of them.
    /// </summary>
    public static SortedSet<string> DirectiveLiterals(Type type)
    {
        var found = new SortedSet<string>(StringComparer.Ordinal);
        foreach (Type t in WithNested(type))
        {
            const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
            foreach (MethodBase m in t.GetMethods(all).Cast<MethodBase>().Concat(t.GetConstructors(all)))
            {
                byte[]? il = m.GetMethodBody()?.GetILAsByteArray();
                if (il is null) continue;
                foreach (int token in StringTokens(il))
                {
                    string s = m.Module.ResolveString(token);
                    if (DirectiveLike().IsMatch(s) && Array.IndexOf(NotDirectives, s) < 0) found.Add(s);
                }
            }
        }
        return found;
    }

    private static IEnumerable<Type> WithNested(Type type)
    {
        yield return type;
        foreach (Type n in type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
        {
            foreach (Type x in WithNested(n)) yield return x;
        }
    }

    private static readonly Dictionary<short, OpCode> s_opcodes = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(f => (OpCode)f.GetValue(null)!)
        .ToDictionary(o => o.Value);

    // Decodes the instruction stream (operand sizes from System.Reflection.Emit.OpCodes) and
    // yields the metadata token of every ldstr.
    private static List<int> StringTokens(byte[] il)
    {
        var tokens = new List<int>();
        int i = 0;
        while (i < il.Length)
        {
            short value = il[i] == 0xFE && i + 1 < il.Length ? (short)(0xFE00 | il[i + 1]) : il[i];
            i += value > 0xFF || value < 0 ? 2 : 1;
            if (!s_opcodes.TryGetValue(value, out OpCode op)) throw new InvalidOperationException($"unknown IL opcode 0x{value:X4} at {i}");
            if (op == OpCodes.Ldstr) tokens.Add(BitConverter.ToInt32(il, i));
            i += op.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(il, i),
                _ => 4,
            };
        }
        return tokens;
    }

    /// <summary>The syntax file's text (<c>\n</c> line ends).</summary>
    /// <param name="mnemonics">The opcode table's mnemonics.</param>
    /// <param name="directives">Directive names with their dot (<c>.func</c>).</param>
    /// <param name="irOnly">Directives the assembler rejects as IR-only (highlighted as errors).</param>
    /// <param name="builtinIncludes">The names <c>.include &lt;…&gt;</c> accepts.</param>
    public static string Generate(IEnumerable<string> mnemonics, IEnumerable<string> directives, IEnumerable<string> irOnly, IEnumerable<string> builtinIncludes)
    {
        var ir = new SortedSet<string>(irOnly, StringComparer.Ordinal);
        var dirs = new SortedSet<string>(directives.Where(d => !ir.Contains(d)), StringComparer.Ordinal);
        var ops = new SortedSet<string>(mnemonics.Select(m => m.ToUpperInvariant()), StringComparer.Ordinal);
        var includes = new SortedSet<string>(builtinIncludes, StringComparer.Ordinal);

        var b = new StringBuilder();
        b.Append("""
            " Vim syntax file
            " Language:   kASM, the kFS assembler of Kitten Space Agency (*.kasm, *.kinc)
            " Shipped by: purrTTY (nvim/syntax/kasm.vim in the mod folder)
            "
            " GENERATED by purrTTY.Kfs.Tools.Tests (KasmSyntaxTests) from the game itself: the
            " mnemonics of its opcode table (GameTables.OpcodesByMnemonic) and the directive names
            " its assembler (KSA.Kfs.KAsm.Assembler) recognises. Do not edit by hand; after a game
            " update run the tests with PURRTTY_UPDATE_SNAPSHOTS=1 and commit the result.

            if exists('b:current_syntax')
              finish
            endif

            let s:cpo_save = &cpo
            set cpo&vim

            " Mnemonics and directives are case-insensitive (the assembler compares them upper- and
            " lower-cased); everything else is case-sensitive.
            syn case ignore

            """);
        b.Append('\n');
        b.Append("\" A mnemonic is the first word of a statement (Assembler.Split: an optional label 'name:'\n");
        b.Append("\" followed by white space, then the word), ending at white space, ';' or the end of the\n");
        b.Append("\" line: a label, an operand or a directive's argument spelled like one (copy:, index,\n");
        b.Append("\" .func enter) is not one.\n");
        foreach (string group in Groups(ops, 12))
        {
            b.Append(@"syn match kasmMnemonic /\%(^\s*\%(\h\w*\s*:\s\+\)\=\)\@<=\%(").Append(group).Append(@"\)\%(\s\|;\|$\)\@=/").Append('\n');
        }
        b.Append('\n');
        b.Append("\" A directive is a word starting with '.', first on the line or after a label.\n");
        b.Append(@"syn match kasmDirective /\%(^\|\s\)\zs\.\%(").Append(Alternation(dirs)).Append(@"\)\>/").Append('\n');
        b.Append("\" IR-only directives: the assembler rejects them (KA-E002).\n");
        b.Append(@"syn match kasmError /\%(^\|\s\)\zs\.\%(").Append(Alternation(ir)).Append(@"\)\>/").Append('\n');
        b.Append('\n');
        b.Append("""
            syn case match

            syn keyword kasmBuiltin sizeof alignof offsetof
            syn match kasmConstant /\<\%(SVC\|TRAP\|MSG\|KFS\|X\)_\w\+\>/
            syn match kasmMacroParam /\\\h\w*/
            syn match kasmLabel /^\s*\h\w*\ze\s*:\%(\S\)\@!/
            syn match kasmNumber /\<\d[0-9_]*\>/
            syn match kasmNumber /\<0[xX][0-9A-Fa-f_]\+\>/
            syn match kasmNumber /\<0[bB][01_]\+\>/
            syn match kasmFloat /\<\d[0-9_]*\.\d[0-9_]*\%([eE][+-]\=\d\+\)\=[fFdD]\=\>/
            syn match kasmFloat /\<\d[0-9_]*[eE][+-]\=\d\+[fFdD]\=\>/

            """);
        b.Append(@"syn match kasmInclude /<\%(").Append(Alternation(includes.Select(n => n.Replace(".", @"\.", StringComparison.Ordinal)), escape: false)).Append(@"\)>/").Append('\n');
        b.Append("""
            syn region kasmString start=/"/ skip=/\\[\\"]/ end=/"/ oneline contains=kasmEscape
            syn match kasmEscape contained /\\./
            syn keyword kasmTodo contained TODO FIXME XXX NOTE
            " ';' starts a comment except inside a string (the string starts first and wins). kASM has no
            " character literals: the assembler (StripComment) cuts a line at a ';' in quotes '...' too.
            syn match kasmComment /;.*$/ contains=kasmTodo,@Spell

            hi def link kasmMnemonic   Statement
            hi def link kasmDirective  PreProc
            hi def link kasmError      Error
            hi def link kasmBuiltin    Keyword
            hi def link kasmConstant   Constant
            hi def link kasmMacroParam Identifier
            hi def link kasmLabel      Label
            hi def link kasmNumber     Number
            hi def link kasmFloat      Float
            hi def link kasmInclude    Include
            hi def link kasmString     String
            hi def link kasmEscape     SpecialChar
            hi def link kasmTodo       Todo
            hi def link kasmComment    Comment

            let b:current_syntax = 'kasm'

            let &cpo = s:cpo_save
            unlet s:cpo_save

            """);
        return b.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    // The words as \| alternations of at most `size` words each (one syn match per group, short lines).
    private static IEnumerable<string> Groups(IEnumerable<string> words, int size) =>
        words.Chunk(size).Select(chunk => string.Join(@"\|", chunk));

    // The names without their leading dot, as a Vim \| alternation.
    private static string Alternation(IEnumerable<string> names, bool escape = true) =>
        string.Join(@"\|", names.Select(n => escape ? n.TrimStart('.') : n));
}
