using caTTY.Core.Managers;
using caTTY.Core.Types;

namespace caTTY.Core.Terminal.EmulatorOps;

/// <summary>
///     Handles character set translation operations for the terminal emulator.
///     Provides methods for shifting between character sets and translating characters.
///     Extracted from TerminalEmulator to reduce file size and improve maintainability.
/// </summary>
internal class TerminalCharsetTranslationOps
{
    private readonly ICharacterSetManager _characterSetManager;

    /// <summary>
    ///     Creates a new charset translation operations handler.
    /// </summary>
    /// <param name="characterSetManager">The character set manager for managing charset state</param>
    public TerminalCharsetTranslationOps(ICharacterSetManager characterSetManager)
    {
        _characterSetManager = characterSetManager;
    }

    /// <summary>
    ///     Handles shift-in (SI) control character.
    ///     Switches active character set to G0.
    /// </summary>
    public void HandleShiftIn()
    {
        _characterSetManager.SwitchCharacterSet(CharacterSetKey.G0);
    }

    /// <summary>
    ///     Handles shift-out (SO) control character.
    ///     Switches active character set to G1.
    /// </summary>
    public void HandleShiftOut()
    {
        _characterSetManager.SwitchCharacterSet(CharacterSetKey.G1);
    }

    /// <summary>
    ///     Translates a character according to the current character set.
    ///     Handles DEC Special Graphics and other character set mappings.
    /// </summary>
    /// <param name="ch">The character to translate</param>
    /// <returns>The translated character string</returns>
    public string TranslateCharacter(char ch)
    {
        return _characterSetManager.TranslateCharacter(ch);
    }

    /// <summary>
    ///     Generates a character set query response.
    /// </summary>
    /// <returns>The character set query response string</returns>
    public string GenerateCharacterSetQueryResponse()
    {
        return _characterSetManager.GenerateCharacterSetQueryResponse();
    }
}
