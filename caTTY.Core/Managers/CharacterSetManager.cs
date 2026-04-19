using System.Collections.Frozen;
using caTTY.Core.Types;

namespace caTTY.Core.Managers;

/// <summary>
///     Manages character set state and translation for terminal emulation.
///     Handles G0/G1/G2/G3 character set designation, switching, and DEC Special Graphics mapping.
///     Based on the TypeScript charset.ts implementation.
/// </summary>
public class CharacterSetManager : ICharacterSetManager
{
    /// <summary>
    ///     DEC Special Graphics character set mapping.
    ///     Maps bytes to Unicode characters for line-drawing glyphs used by TUIs.
    /// </summary>
    private static readonly FrozenDictionary<int, string> DecSpecialGraphicsMap = new Dictionary<int, string>
    {
        { 0x60, "\u25C6" }, // ` -> ◆ (diamond)
        { 0x61, "\u2592" }, // a -> ▒ (checkerboard)
        { 0x62, "\u2409" }, // b -> ␉ (HT symbol)
        { 0x63, "\u240C" }, // c -> ␌ (FF symbol)
        { 0x64, "\u240D" }, // d -> ␍ (CR symbol)
        { 0x65, "\u240A" }, // e -> ␊ (LF symbol)
        { 0x66, "\u00B0" }, // f -> ° (degree)
        { 0x67, "\u00B1" }, // g -> ± (plus-minus)
        { 0x68, "\u2424" }, // h -> ␤ (NL symbol)
        { 0x69, "\u240B" }, // i -> ␋ (VT symbol)
        { 0x6a, "\u2518" }, // j -> ┘ (lower right corner)
        { 0x6b, "\u2510" }, // k -> ┐ (upper right corner)
        { 0x6c, "\u250c" }, // l -> ┌ (upper left corner)
        { 0x6d, "\u2514" }, // m -> └ (lower left corner)
        { 0x6e, "\u253c" }, // n -> ┼ (crossing lines)
        { 0x6f, "\u23BA" }, // o -> ⎺ (scan line 1)
        { 0x70, "\u23BB" }, // p -> ⎻ (scan line 3)
        { 0x71, "\u2500" }, // q -> ─ (horizontal line)
        { 0x72, "\u23BC" }, // r -> ⎼ (scan line 7)
        { 0x73, "\u23BD" }, // s -> ⎽ (scan line 9)
        { 0x74, "\u251C" }, // t -> ├ (left tee)
        { 0x75, "\u2524" }, // u -> ┤ (right tee)
        { 0x76, "\u2534" }, // v -> ┴ (bottom tee)
        { 0x77, "\u252C" }, // w -> ┬ (top tee)
        { 0x78, "\u2502" }, // x -> │ (vertical line)
        { 0x79, "\u2264" }, // y -> ≤ (less than or equal)
        { 0x7a, "\u2265" }, // z -> ≥ (greater than or equal)
        { 0x7b, "\u03C0" }, // { -> π (pi)
        { 0x7c, "\u2260" }, // | -> ≠ (not equal)
        { 0x7d, "\u00A3" }, // } -> £ (pound sterling)
        { 0x7e, "\u00B7" }, // ~ -> · (middle dot)
    }.ToFrozenDictionary();

    private readonly TerminalState _terminalState;

    /// <summary>
    ///     Creates a new character set manager with default state.
    /// </summary>
    public CharacterSetManager() : this(new TerminalState(80, 24))
    {
    }

    /// <summary>
    ///     Creates a new character set manager with the specified terminal state.
    /// </summary>
    /// <param name="terminalState">The terminal state to use</param>
    public CharacterSetManager(TerminalState terminalState)
    {
        _terminalState = terminalState ?? throw new ArgumentNullException(nameof(terminalState));
    }

    /// <summary>
    ///     Creates a new character set manager with the specified state (for backward compatibility).
    /// </summary>
    /// <param name="characterSets">The character set state to use</param>
    /// <param name="utf8Mode">Whether UTF-8 mode is enabled</param>
    public CharacterSetManager(CharacterSetState characterSets, bool utf8Mode) : this(new TerminalState(80, 24))
    {
        _terminalState.CharacterSets = characterSets;
        _terminalState.Utf8Mode = utf8Mode;
    }

    /// <inheritdoc />
    public CharacterSetState CharacterSets => _terminalState.CharacterSets;

    /// <inheritdoc />
    public bool Utf8Mode => _terminalState.Utf8Mode;

    /// <inheritdoc />
    public void SetUtf8Mode(bool enabled)
    {
        _terminalState.Utf8Mode = enabled;
    }

    /// <inheritdoc />
    public void DesignateCharacterSet(CharacterSetKey slot, string charset)
    {
        switch (slot)
        {
            case CharacterSetKey.G0:
                _terminalState.CharacterSets.G0 = charset;
                break;
            case CharacterSetKey.G1:
                _terminalState.CharacterSets.G1 = charset;
                break;
            case CharacterSetKey.G2:
                _terminalState.CharacterSets.G2 = charset;
                break;
            case CharacterSetKey.G3:
                _terminalState.CharacterSets.G3 = charset;
                break;
        }
    }

    /// <inheritdoc />
    public string GetCharacterSet(CharacterSetKey slot)
    {
        return slot switch
        {
            CharacterSetKey.G0 => _terminalState.CharacterSets.G0,
            CharacterSetKey.G1 => _terminalState.CharacterSets.G1,
            CharacterSetKey.G2 => _terminalState.CharacterSets.G2,
            CharacterSetKey.G3 => _terminalState.CharacterSets.G3,
            _ => _terminalState.CharacterSets.G0
        };
    }

    /// <inheritdoc />
    public string GetCurrentCharacterSet()
    {
        return GetCharacterSet(_terminalState.CharacterSets.Current);
    }

    /// <inheritdoc />
    public void SwitchCharacterSet(CharacterSetKey slot)
    {
        _terminalState.CharacterSets.Current = slot;
    }

    /// <inheritdoc />
    public string TranslateCharacter(char ch)
    {
        // If UTF-8 mode is enabled, no translation is performed
        if (_terminalState.Utf8Mode)
        {
            return ch.ToString();
        }

        string currentCharset = GetCurrentCharacterSet();

        // DEC Special Graphics character set (charset "0")
        if (currentCharset == "0")
        {
            int code = ch;
            if (DecSpecialGraphicsMap.TryGetValue(code, out string? mapped))
            {
                return mapped;
            }
        }

        // For all other character sets (including ASCII "B"), return the character as-is
        return ch.ToString();
    }

    /// <inheritdoc />
    public string GenerateCharacterSetQueryResponse()
    {
        string charset = _terminalState.Utf8Mode ? "utf-8" : GetCurrentCharacterSet();
        return $"\x1b[?26;{charset}\x1b\\";
    }
}