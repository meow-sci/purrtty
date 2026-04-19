using caTTY.Core.Types;

namespace caTTY.Core.Parsing;

/// <summary>
///     Interface for handling parsed terminal sequences and control characters.
///     Based on the TypeScript ParserHandlers interface.
/// </summary>
public interface IParserHandlers
{
    /// <summary>
    ///     Handles a bell character (BEL, 0x07).
    /// </summary>
    void HandleBell();

    /// <summary>
    ///     Handles a backspace character (BS, 0x08).
    /// </summary>
    void HandleBackspace();

    /// <summary>
    ///     Handles a tab character (HT, 0x09).
    /// </summary>
    void HandleTab();

    /// <summary>
    ///     Handles a line feed character (LF, 0x0A).
    /// </summary>
    void HandleLineFeed();

    /// <summary>
    ///     Handles a form feed character (FF, 0x0C).
    /// </summary>
    void HandleFormFeed();

    /// <summary>
    ///     Handles a carriage return character (CR, 0x0D).
    /// </summary>
    void HandleCarriageReturn();

    /// <summary>
    ///     Handles a shift-in character (SI, 0x0F).
    /// </summary>
    void HandleShiftIn();

    /// <summary>
    ///     Handles a shift-out character (SO, 0x0E).
    /// </summary>
    void HandleShiftOut();

    /// <summary>
    ///     Handles a normal printable byte or UTF-8 code point.
    /// </summary>
    /// <param name="codePoint">The Unicode code point to handle</param>
    void HandleNormalByte(int codePoint);

    /// <summary>
    ///     Handles a non-CSI ESC sequence (e.g., ESC 7/8 for save/restore cursor).
    /// </summary>
    /// <param name="message">The parsed ESC message</param>
    void HandleEsc(EscMessage message);

    /// <summary>
    ///     Handles a non-SGR CSI sequence.
    /// </summary>
    /// <param name="message">The parsed CSI message</param>
    void HandleCsi(CsiMessage message);

    /// <summary>
    ///     Handles an opaque OSC sequence (buffered, not parsed).
    /// </summary>
    /// <param name="message">The raw OSC message</param>
    void HandleOsc(OscMessage message);

    /// <summary>
    ///     Handles a Device Control String (DCS) sequence.
    /// </summary>
    /// <param name="message">The parsed DCS message</param>
    void HandleDcs(DcsMessage message);

    /// <summary>
    ///     Handles parsed SGR messages wrapped with raw sequence.
    /// </summary>
    /// <param name="sequence">The parsed SGR sequence</param>
    void HandleSgr(SgrSequence sequence);

    /// <summary>
    ///     Handles parsed xterm OSC extension sequences.
    /// </summary>
    /// <param name="message">The parsed xterm OSC message</param>
    void HandleXtermOsc(XtermOscMessage message);
}
