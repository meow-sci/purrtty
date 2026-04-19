using caTTY.Core.Types;

namespace caTTY.Core.Types;

/// <summary>
///     Character set designation keys for G0, G1, G2, G3.
/// </summary>
public enum CharacterSetKey
{
    /// <summary>
    ///     G0 character set (primary).
    /// </summary>
    G0,

    /// <summary>
    ///     G1 character set (secondary).
    /// </summary>
    G1,

    /// <summary>
    ///     G2 character set (tertiary).
    /// </summary>
    G2,

    /// <summary>
    ///     G3 character set (quaternary).
    /// </summary>
    G3
}

/// <summary>
///     Character set state for terminal emulation.
/// </summary>
public class CharacterSetState
{
    /// <summary>
    ///     G0 character set identifier.
    /// </summary>
    public string G0 { get; set; } = "B"; // ASCII

    /// <summary>
    ///     G1 character set identifier.
    /// </summary>
    public string G1 { get; set; } = "B"; // ASCII

    /// <summary>
    ///     G2 character set identifier.
    /// </summary>
    public string G2 { get; set; } = "B"; // ASCII

    /// <summary>
    ///     G3 character set identifier.
    /// </summary>
    public string G3 { get; set; } = "B"; // ASCII

    /// <summary>
    ///     Currently active character set.
    /// </summary>
    public CharacterSetKey Current { get; set; } = CharacterSetKey.G0;
}

/// <summary>
///     Window properties for terminal title and icon management.
/// </summary>
public class WindowProperties
{
    /// <summary>
    ///     Current window title.
    /// </summary>
    public string Title { get; set; } = "";

    /// <summary>
    ///     Current icon name.
    /// </summary>
    public string IconName { get; set; } = "";
}

/// <summary>
///     Manages terminal state including cursor position, modes, and attributes.
///     This class tracks all the state needed for terminal emulation including
///     cursor position, terminal modes, SGR attributes, and scroll regions.
/// </summary>
public class TerminalState
{
    /// <summary>
    ///     Creates a new terminal state with the specified dimensions.
    /// </summary>
    /// <param name="cols">Number of columns</param>
    /// <param name="rows">Number of rows</param>
    public TerminalState(int cols, int rows)
    {
        Cols = cols;
        Rows = rows;
        CursorX = 0;
        CursorY = 0;
        PrimaryCursorX = 0;
        PrimaryCursorY = 0;
        PrimaryWrapPending = false;
        AlternateCursorX = 0;
        AlternateCursorY = 0;
        AlternateWrapPending = false;
        ScrollTop = 0;
        ScrollBottom = rows - 1;
        InitializeTabStops(cols);
    }

    /// <summary>
    ///     Current cursor X position (column, 0-based).
    /// </summary>
    public int CursorX { get; set; }

    /// <summary>
    ///     Current cursor Y position (row, 0-based).
    /// </summary>
    public int CursorY { get; set; }

    /// <summary>
    ///     Stored cursor X position for the primary screen buffer.
    ///     Used when switching between primary and alternate screen buffers.
    /// </summary>
    public int PrimaryCursorX { get; set; }

    /// <summary>
    ///     Stored cursor Y position for the primary screen buffer.
    ///     Used when switching between primary and alternate screen buffers.
    /// </summary>
    public int PrimaryCursorY { get; set; }

    /// <summary>
    ///     Stored wrap pending state for the primary screen buffer.
    /// </summary>
    public bool PrimaryWrapPending { get; set; }

    /// <summary>
    ///     Stored cursor X position for the alternate screen buffer.
    ///     Used when switching between primary and alternate screen buffers.
    /// </summary>
    public int AlternateCursorX { get; set; }

    /// <summary>
    ///     Stored cursor Y position for the alternate screen buffer.
    ///     Used when switching between primary and alternate screen buffers.
    /// </summary>
    public int AlternateCursorY { get; set; }

    /// <summary>
    ///     Stored wrap pending state for the alternate screen buffer.
    /// </summary>
    public bool AlternateWrapPending { get; set; }

    /// <summary>
    ///     Saved cursor position for ESC 7 / ESC 8 sequences (DEC style).
    /// </summary>
    public (int X, int Y)? SavedCursor { get; set; }

    /// <summary>
    ///     Saved cursor position for CSI s / CSI u sequences (ANSI style).
    ///     This is separate from DEC cursor save/restore to maintain compatibility.
    /// </summary>
    public (int X, int Y)? AnsiSavedCursor { get; set; }

    /// <summary>
    ///     Cursor style (DECSCUSR values 0-6).
    /// </summary>
    public CursorStyle CursorStyle { get; set; } = CursorStyle.BlinkingBlock;

    /// <summary>
    ///     Whether the cursor is visible.
    /// </summary>
    public bool CursorVisible { get; set; } = true;

    /// <summary>
    ///     Wrap pending state for line overflow handling.
    ///     When true, the next printable character will trigger a line wrap.
    /// </summary>
    public bool WrapPending { get; set; }

    /// <summary>
    ///     Application cursor keys mode.
    ///     When true, arrow keys send different escape sequences.
    /// </summary>
    public bool ApplicationCursorKeys { get; set; }

    /// <summary>
    ///     Origin mode - when true, cursor positioning is relative to scroll region.
    /// </summary>
    public bool OriginMode { get; set; }

    /// <summary>
    ///     Auto-wrap mode - when true, cursor wraps to next line at right edge.
    /// </summary>
    public bool AutoWrapMode { get; set; } = true;

    /// <summary>
    ///     Bracketed paste mode - when true, paste content is wrapped with escape sequences.
    /// </summary>
    public bool BracketedPasteMode { get; set; } = false;

    /// <summary>
    ///     Top of scroll region (0-based, inclusive).
    /// </summary>
    public int ScrollTop { get; set; }

    /// <summary>
    ///     Bottom of scroll region (0-based, inclusive).
    /// </summary>
    public int ScrollBottom { get; set; }

    /// <summary>
    ///     Tab stops array. True indicates a tab stop at that column.
    /// </summary>
    public bool[] TabStops { get; set; } = Array.Empty<bool>();

    /// <summary>
    ///     Window properties (title, icon name).
    /// </summary>
    public WindowProperties WindowProperties { get; set; } = new();

    /// <summary>
    ///     Title stack for push/pop operations.
    /// </summary>
    public List<string> TitleStack { get; set; } = new();

    /// <summary>
    ///     Icon name stack for push/pop operations.
    /// </summary>
    public List<string> IconNameStack { get; set; } = new();

    /// <summary>
    ///     Character set state.
    /// </summary>
    public CharacterSetState CharacterSets { get; set; } = new();

    /// <summary>
    ///     UTF-8 mode enabled.
    /// </summary>
    public bool Utf8Mode { get; set; } = true;

    /// <summary>
    ///     Saved private modes for XTSAVE/XTRESTORE.
    /// </summary>
    public Dictionary<int, bool> SavedPrivateModes { get; set; } = new();

    /// <summary>
    ///     Current SGR attributes for new characters.
    /// </summary>
    public SgrAttributes CurrentSgrState { get; set; } = SgrAttributes.Default;

    /// <summary>
    ///     Current character protection attribute.
    /// </summary>
    public bool CurrentCharacterProtection { get; set; }

    /// <summary>
    ///     Current hyperlink URL for new characters (OSC 8 sequences).
    ///     Null means no hyperlink is active.
    /// </summary>
    public string? CurrentHyperlinkUrl { get; set; }

    /// <summary>
    ///     Whether the alternate screen buffer is currently active.
    /// </summary>
    public bool IsAlternateScreenActive { get; set; }

    /// <summary>
    ///     Bitmask tracking DEC mouse tracking modes (1000/1002/1003).
    /// </summary>
    public int MouseTrackingModeBits { get; private set; }

    /// <summary>
    ///     Whether SGR mouse encoding (DECSET 1006) is enabled.
    /// </summary>
    public bool MouseSgrEncodingEnabled { get; set; }

    /// <summary>
    ///     True when any mouse tracking mode is enabled.
    /// </summary>
    public bool IsMouseReportingEnabled => MouseTrackingModeBits != 0;

    public void SetMouseTrackingMode(int mode, bool enabled)
    {
        int bit = mode switch
        {
            1000 => 1,
            1002 => 2,
            1003 => 4,
            _ => 0
        };

        if (bit == 0)
        {
            return;
        }

        if (enabled)
        {
            MouseTrackingModeBits |= bit;
        }
        else
        {
            MouseTrackingModeBits &= ~bit;
        }
    }

    /// <summary>
    ///     Terminal dimensions (columns).
    /// </summary>
    public int Cols { get; set; }

    /// <summary>
    ///     Terminal dimensions (rows).
    /// </summary>
    public int Rows { get; set; }

    /// <summary>
    ///     Initializes tab stops at every 8th column.
    /// </summary>
    /// <param name="cols">Number of columns</param>
    public void InitializeTabStops(int cols)
    {
        TabStops = new bool[cols];
        for (int i = 8; i < cols; i += 8)
        {
            TabStops[i] = true;
        }
    }

    /// <summary>
    ///     Clamps the cursor position to stay within terminal bounds.
    ///     Respects origin mode and scroll region boundaries.
    /// </summary>
    public void ClampCursor()
    {
        CursorX = Math.Max(0, Math.Min(Cols - 1, CursorX));

        if (OriginMode)
        {
            CursorY = Math.Max(ScrollTop, Math.Min(ScrollBottom, CursorY));
        }
        else
        {
            CursorY = Math.Max(0, Math.Min(Rows - 1, CursorY));
        }

        WrapPending = false;
    }

    /// <summary>
    ///     Sets origin mode and homes the cursor.
    /// </summary>
    /// <param name="enable">True to enable origin mode</param>
    public void SetOriginMode(bool enable)
    {
        OriginMode = enable;
        // VT100/xterm behavior: home the cursor when toggling origin mode
        CursorX = 0;
        CursorY = enable ? ScrollTop : 0;
        WrapPending = false;
        ClampCursor();
    }

    /// <summary>
    ///     Sets auto-wrap mode.
    /// </summary>
    /// <param name="enable">True to enable auto-wrap mode</param>
    public void SetAutoWrapMode(bool enable)
    {
        AutoWrapMode = enable;
        if (!enable)
        {
            WrapPending = false;
        }
    }

    /// <summary>
    ///     Handles cursor positioning when writing beyond the right edge.
    ///     Implements wrap pending semantics.
    /// </summary>
    /// <returns>True if a line wrap occurred</returns>
    public bool HandleRightEdgeWrite()
    {
        if (CursorX >= Cols - 1)
        {
            if (AutoWrapMode)
            {
                WrapPending = true;
                return false; // Don't wrap yet, wait for next character
            }

            // Stay at right edge
            CursorX = Cols - 1;
            return false;
        }

        return false;
    }

    /// <summary>
    ///     Handles wrap pending state when writing a printable character.
    /// </summary>
    /// <returns>True if a line wrap was performed</returns>
    public bool HandleWrapPending()
    {
        if (WrapPending)
        {
            WrapPending = false;
            // Move to beginning of next line
            CursorX = 0;
            if (CursorY < Rows - 1)
            {
                CursorY++;
                return true;
            }

            // At bottom - need to scroll (will be handled by caller)
            return true;
        }

        return false;
    }

    /// <summary>
    ///     Advances the cursor after writing a character.
    /// </summary>
    public void AdvanceCursor()
    {
        CursorX++;
        if (CursorX >= Cols)
        {
            HandleRightEdgeWrite();
        }
    }

    /// <summary>
    ///     Resets the terminal state to initial values.
    /// </summary>
    public void Reset()
    {
        CursorX = 0;
        CursorY = 0;
        PrimaryCursorX = 0;
        PrimaryCursorY = 0;
        PrimaryWrapPending = false;
        AlternateCursorX = 0;
        AlternateCursorY = 0;
        AlternateWrapPending = false;
        SavedCursor = null;
        AnsiSavedCursor = null;
        CursorStyle = CursorStyle.BlinkingBlock;
        CursorVisible = true;
        WrapPending = false;
        ApplicationCursorKeys = false;
        OriginMode = false;
        AutoWrapMode = true;
        BracketedPasteMode = false;
        ScrollTop = 0;
        ScrollBottom = Rows - 1;
        InitializeTabStops(Cols);
        WindowProperties = new WindowProperties();
        TitleStack.Clear();
        IconNameStack.Clear();
        CharacterSets = new CharacterSetState();
        Utf8Mode = true;
        SavedPrivateModes.Clear();
        CurrentSgrState = SgrAttributes.Default;
        CurrentCharacterProtection = false;
        CurrentHyperlinkUrl = null;

        IsAlternateScreenActive = false;
        MouseTrackingModeBits = 0;
        MouseSgrEncodingEnabled = false;
    }

    /// <summary>
    ///     Resizes the terminal state to the specified dimensions.
    ///     Updates internal dimensions and adjusts scroll region if needed.
    /// </summary>
    /// <param name="newCols">New number of columns</param>
    /// <param name="newRows">New number of rows</param>
    public void Resize(int newCols, int newRows)
    {
        Cols = newCols;
        Rows = newRows;

        // Update scroll region if it was full-screen
        if (ScrollTop == 0 && ScrollBottom == Rows - 1)
        {
            ScrollBottom = newRows - 1;
        }
        else
        {
            // Clamp existing scroll region to new dimensions
            ScrollTop = Math.Min(ScrollTop, newRows - 1);
            ScrollBottom = Math.Min(ScrollBottom, newRows - 1);
            
            // Ensure scroll region is still valid
            if (ScrollTop >= ScrollBottom)
            {
                ScrollTop = 0;
                ScrollBottom = newRows - 1;
            }
        }

        // Update tab stops for new width
        ResizeTabStops(newCols);
    }

    /// <summary>
    ///     Resizes the tab stops array to match the new terminal width.
    ///     Preserves existing tab stops where possible and initializes new ones.
    /// </summary>
    /// <param name="newCols">New number of columns</param>
    public void ResizeTabStops(int newCols)
    {
        var newTabStops = new bool[newCols];
        
        // Copy existing tab stops
        int copyCount = Math.Min(TabStops.Length, newCols);
        for (int i = 0; i < copyCount; i++)
        {
            newTabStops[i] = TabStops[i];
        }

        // Initialize new tab stops at 8-column intervals if width increased
        if (newCols > TabStops.Length)
        {
            for (int i = TabStops.Length; i < newCols; i += 8)
            {
                if (i < newCols)
                {
                    newTabStops[i] = true;
                }
            }
        }

        TabStops = newTabStops;
    }
}
