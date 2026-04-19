namespace caTTY.Core.Terminal.ParserHandlers;

/// <summary>
///     Handles C0 control characters (ASCII control codes 0x00-0x1F).
/// </summary>
internal class C0Handler
{
    private readonly TerminalEmulator _terminal;

    public C0Handler(TerminalEmulator terminal)
    {
        _terminal = terminal;
    }

    public void HandleBell()
    {
        _terminal.HandleBell();
    }

    public void HandleBackspace()
    {
        _terminal.HandleBackspace();
    }

    public void HandleTab()
    {
        _terminal.HandleTab();
    }

    public void HandleLineFeed()
    {
        _terminal.HandleLineFeed();
    }

    public void HandleFormFeed()
    {
        // Form feed is typically treated as line feed in modern terminals
        _terminal.HandleLineFeed();
    }

    public void HandleCarriageReturn()
    {
        _terminal.HandleCarriageReturn();
    }

    public void HandleShiftIn()
    {
        _terminal.HandleShiftIn();
    }

    public void HandleShiftOut()
    {
        _terminal.HandleShiftOut();
    }

    public void HandleNormalByte(int codePoint)
    {
        // Convert Unicode code point to character and apply character set translation
        if (codePoint <= 0xFFFF)
        {
            // Basic Multilingual Plane - single char
            char character = (char)codePoint;
            string translatedChar = _terminal.TranslateCharacter(character);

            // Write each character in the translated string
            foreach (char c in translatedChar)
            {
                _terminal.WriteCharacterAtCursor(c);
            }
        }
        else
        {
            // Supplementary planes - surrogate pair
            // For supplementary planes, we don't apply character set translation
            // as they are already Unicode and not subject to legacy character set mapping
            string characters = char.ConvertFromUtf32(codePoint);
            foreach (char c in characters)
            {
                _terminal.WriteCharacterAtCursor(c);
            }
        }
    }
}
