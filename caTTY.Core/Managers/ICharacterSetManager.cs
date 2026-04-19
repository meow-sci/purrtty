using caTTY.Core.Types;

namespace caTTY.Core.Managers;

/// <summary>
///     Interface for managing character set state and translation.
///     Handles G0/G1/G2/G3 character set designation and switching.
/// </summary>
public interface ICharacterSetManager
{
    /// <summary>
    ///     Gets the current character set state.
    /// </summary>
    CharacterSetState CharacterSets { get; }

    /// <summary>
    ///     Gets whether UTF-8 mode is enabled.
    /// </summary>
    bool Utf8Mode { get; }

    /// <summary>
    ///     Sets UTF-8 mode.
    /// </summary>
    /// <param name="enabled">True to enable UTF-8 mode</param>
    void SetUtf8Mode(bool enabled);

    /// <summary>
    ///     Designates a character set to a specific G slot.
    /// </summary>
    /// <param name="slot">The character set slot (G0, G1, G2, G3)</param>
    /// <param name="charset">The character set identifier</param>
    void DesignateCharacterSet(CharacterSetKey slot, string charset);

    /// <summary>
    ///     Gets the character set identifier for a specific slot.
    /// </summary>
    /// <param name="slot">The character set slot</param>
    /// <returns>The character set identifier</returns>
    string GetCharacterSet(CharacterSetKey slot);

    /// <summary>
    ///     Gets the currently active character set identifier.
    /// </summary>
    /// <returns>The active character set identifier</returns>
    string GetCurrentCharacterSet();

    /// <summary>
    ///     Switches the active character set to the specified slot.
    /// </summary>
    /// <param name="slot">The character set slot to activate</param>
    void SwitchCharacterSet(CharacterSetKey slot);

    /// <summary>
    ///     Translates a character according to the current character set.
    /// </summary>
    /// <param name="ch">The character to translate</param>
    /// <returns>The translated character</returns>
    string TranslateCharacter(char ch);

    /// <summary>
    ///     Generates a character set query response.
    /// </summary>
    /// <returns>The character set query response string</returns>
    string GenerateCharacterSetQueryResponse();
}