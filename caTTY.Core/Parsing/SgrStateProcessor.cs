using caTTY.Core.Types;

namespace caTTY.Core.Parsing;

/// <summary>
///     SGR State Processor
/// 
///     Processes SGR messages and updates SGR state accordingly.
///     Handles the conversion from SGR escape sequences to styling state.
///     Based on the TypeScript SgrStateProcessor implementation.
/// </summary>
public static class SgrStateProcessor
{
    /// <summary>
    ///     Process SGR messages and update the current SGR state.
    /// </summary>
    /// <param name="currentState">Current SGR state</param>
    /// <param name="messages">Array of SGR messages to process</param>
    /// <returns>Updated SGR state</returns>
    public static SgrState ProcessSgrMessages(SgrState currentState, ReadOnlySpan<SgrMessage> messages)
    {
        // Create a copy of the current state to avoid mutation
        var newState = new SgrState(currentState);
        
        foreach (var message in messages)
        {
            ProcessSgrMessage(newState, message);
        }
        
        return newState;
    }

    /// <summary>
    ///     Process SGR messages and update the current SGR state in-place.
    /// </summary>
    /// <param name="currentState">Current SGR state to modify</param>
    /// <param name="messages">Array of SGR messages to process</param>
    public static void ProcessSgrMessagesInPlace(SgrState currentState, ReadOnlySpan<SgrMessage> messages)
    {
        foreach (var message in messages)
        {
            ProcessSgrMessage(currentState, message);
        }
    }

    /// <summary>
    ///     Process a single SGR message and update the state.
    /// </summary>
    /// <param name="state">The SGR state to modify</param>
    /// <param name="message">The SGR message to process</param>
    public static void ProcessSgrMessage(SgrState state, SgrMessage message)
    {
        if (message == null) return;

        switch (message.Type)
        {
            case "sgr.reset":
                // Reset all attributes to default
                state.Reset();
                break;
                
            case "sgr.bold":
                state.Bold = true;
                break;
                
            case "sgr.faint":
                state.Faint = true;
                break;
                
            case "sgr.italic":
                state.Italic = true;
                break;
                
            case "sgr.underline":
                state.Underline = true;
                if (message.Data is UnderlineStyle style)
                {
                    state.UnderlineStyle = style;
                }
                else
                {
                    state.UnderlineStyle = Types.UnderlineStyle.Single;
                }
                break;
                
            case "sgr.slowBlink":
            case "sgr.rapidBlink":
                state.Blink = true;
                break;
                
            case "sgr.inverse":
                state.Inverse = true;
                break;
                
            case "sgr.hidden":
                state.Hidden = true;
                break;
                
            case "sgr.strikethrough":
                state.Strikethrough = true;
                break;
                
            case "sgr.font":
                if (message.Data is int font)
                {
                    state.Font = font;
                }
                break;
                
            case "sgr.normalIntensity":
                state.Bold = false;
                state.Faint = false;
                break;
                
            case "sgr.notItalic":
                state.Italic = false;
                break;
                
            case "sgr.notUnderlined":
                state.Underline = false;
                state.UnderlineStyle = null;
                break;
                
            case "sgr.notBlinking":
                state.Blink = false;
                break;
                
            case "sgr.notInverse":
                state.Inverse = false;
                break;
                
            case "sgr.notHidden":
                state.Hidden = false;
                break;
                
            case "sgr.notStrikethrough":
                state.Strikethrough = false;
                break;
                
            case "sgr.foregroundColor":
                if (message.Data is Color fgColor)
                {
                    state.ForegroundColor = fgColor;
                }
                break;
                
            case "sgr.defaultForeground":
                state.ForegroundColor = null;
                break;
                
            case "sgr.backgroundColor":
                if (message.Data is Color bgColor)
                {
                    state.BackgroundColor = bgColor;
                }
                break;
                
            case "sgr.defaultBackground":
                state.BackgroundColor = null;
                break;
                
            case "sgr.underlineColor":
                if (message.Data is Color ulColor)
                {
                    state.UnderlineColor = ulColor;
                }
                break;
                
            case "sgr.defaultUnderlineColor":
                state.UnderlineColor = null;
                break;
                
            // Handle other SGR types that don't affect basic styling
            case "sgr.doubleUnderline":
                state.Underline = true;
                state.UnderlineStyle = Types.UnderlineStyle.Double;
                break;
                
            case "sgr.fraktur":
            case "sgr.proportionalSpacing":
            case "sgr.disableProportionalSpacing":
            case "sgr.framed":
            case "sgr.encircled":
            case "sgr.overlined":
            case "sgr.notFramed":
            case "sgr.notOverlined":
            case "sgr.ideogram":
            case "sgr.superscript":
            case "sgr.subscript":
            case "sgr.notSuperscriptSubscript":
                // These are not commonly supported or implemented
                // Ignore for now
                break;
                
            case "sgr.enhancedMode":
                // Enhanced SGR mode with > prefix (e.g., CSI > 4 ; 2 m)
                if (message.Data is int[] enhancedParams && enhancedParams.Length >= 2 && enhancedParams[0] == 4)
                {
                    // Enhanced underline mode: CSI > 4 ; n m
                    var underlineType = enhancedParams[1];
                    switch (underlineType)
                    {
                        case 0:
                            // No underline
                            state.Underline = false;
                            state.UnderlineStyle = null;
                            break;
                        case 1:
                            // Single underline
                            state.Underline = true;
                            state.UnderlineStyle = Types.UnderlineStyle.Single;
                            break;
                        case 2:
                            // Double underline
                            state.Underline = true;
                            state.UnderlineStyle = Types.UnderlineStyle.Double;
                            break;
                        case 3:
                            // Curly underline
                            state.Underline = true;
                            state.UnderlineStyle = Types.UnderlineStyle.Curly;
                            break;
                        case 4:
                            // Dotted underline
                            state.Underline = true;
                            state.UnderlineStyle = Types.UnderlineStyle.Dotted;
                            break;
                        case 5:
                            // Dashed underline
                            state.Underline = true;
                            state.UnderlineStyle = Types.UnderlineStyle.Dashed;
                            break;
                    }
                }
                // For other enhanced modes or invalid parameters, gracefully ignore
                break;
                
            case "sgr.privateMode":
                // Private SGR mode with ? prefix (e.g., CSI ? 4 m)
                if (message.Data is int[] privateParams && privateParams.Length == 1 && privateParams[0] == 4)
                {
                    // Private underline mode (?4m) - enable a special underline style
                    state.Underline = true;
                    state.UnderlineStyle = Types.UnderlineStyle.Single; // Use single underline for private mode
                    // Could be extended to use a different style if needed
                }
                // For other private modes, gracefully ignore
                break;
                
            case "sgr.withIntermediate":
                // SGR with intermediate characters (e.g., CSI 0 % m)
                // Handle specific cases like reset with %
                if (message.Data is { } intermediateData)
                {
                    // Try to extract intermediate and params from the data
                    var dataStr = intermediateData.ToString();
                    if (dataStr?.Contains("%") == true && dataStr.Contains("0"))
                    {
                        // Reset all attributes (similar to SGR 0)
                        state.Reset();
                    }
                }
                // For other intermediate sequences, gracefully ignore
                break;
                
            case "sgr.unknown":
                // Unknown SGR parameters - ignore gracefully
                break;
        }
    }

    /// <summary>
    ///     Convert ANSI color code to named color.
    /// </summary>
    /// <param name="colorCode">ANSI color code (30-37, 40-47, 90-97, 100-107)</param>
    /// <returns>Named color or null if invalid</returns>
    public static NamedColor? AnsiCodeToNamedColor(int colorCode)
    {
        // Standard foreground colors (30-37)
        if (colorCode >= 30 && colorCode <= 37)
        {
            var colors = new[]
            {
                NamedColor.Black, NamedColor.Red, NamedColor.Green, NamedColor.Yellow,
                NamedColor.Blue, NamedColor.Magenta, NamedColor.Cyan, NamedColor.White
            };
            return colors[colorCode - 30];
        }
        
        // Bright foreground colors (90-97)
        if (colorCode >= 90 && colorCode <= 97)
        {
            var colors = new[]
            {
                NamedColor.BrightBlack, NamedColor.BrightRed, NamedColor.BrightGreen, NamedColor.BrightYellow,
                NamedColor.BrightBlue, NamedColor.BrightMagenta, NamedColor.BrightCyan, NamedColor.BrightWhite
            };
            return colors[colorCode - 90];
        }
        
        // Standard background colors (40-47)
        if (colorCode >= 40 && colorCode <= 47)
        {
            var colors = new[]
            {
                NamedColor.Black, NamedColor.Red, NamedColor.Green, NamedColor.Yellow,
                NamedColor.Blue, NamedColor.Magenta, NamedColor.Cyan, NamedColor.White
            };
            return colors[colorCode - 40];
        }
        
        // Bright background colors (100-107)
        if (colorCode >= 100 && colorCode <= 107)
        {
            var colors = new[]
            {
                NamedColor.BrightBlack, NamedColor.BrightRed, NamedColor.BrightGreen, NamedColor.BrightYellow,
                NamedColor.BrightBlue, NamedColor.BrightMagenta, NamedColor.BrightCyan, NamedColor.BrightWhite
            };
            return colors[colorCode - 100];
        }
        
        return null;
    }

    /// <summary>
    ///     Apply inverse video effect to SGR state.
    ///     Swaps foreground and background colors.
    /// </summary>
    /// <param name="state">The SGR state to process</param>
    /// <returns>A new SGR state with inverse video applied</returns>
    public static SgrState ApplyInverseVideo(SgrState state)
    {
        if (!state.Inverse)
        {
            return new SgrState(state);
        }
        
        var newState = new SgrState(state);
        
        // Swap foreground and background colors
        var tempFg = newState.ForegroundColor;
        newState.ForegroundColor = newState.BackgroundColor;
        newState.BackgroundColor = tempFg;
        
        // If no colors are set, use default terminal colors
        if (!newState.ForegroundColor.HasValue && !newState.BackgroundColor.HasValue)
        {
            newState.ForegroundColor = new Color(NamedColor.Black);
            newState.BackgroundColor = new Color(NamedColor.White);
        }
        else if (!newState.ForegroundColor.HasValue)
        {
            newState.ForegroundColor = new Color(NamedColor.Black);
        }
        else if (!newState.BackgroundColor.HasValue)
        {
            newState.BackgroundColor = new Color(NamedColor.White);
        }
        
        return newState;
    }

    /// <summary>
    ///     Apply inverse video effect to SGR state in-place.
    ///     Swaps foreground and background colors.
    /// </summary>
    /// <param name="state">The SGR state to modify</param>
    public static void ApplyInverseVideoInPlace(SgrState state)
    {
        if (!state.Inverse) return;
        
        // Swap foreground and background colors
        var tempFg = state.ForegroundColor;
        state.ForegroundColor = state.BackgroundColor;
        state.BackgroundColor = tempFg;
        
        // If no colors are set, use default terminal colors
        if (!state.ForegroundColor.HasValue && !state.BackgroundColor.HasValue)
        {
            state.ForegroundColor = new Color(NamedColor.Black);
            state.BackgroundColor = new Color(NamedColor.White);
        }
        else if (!state.ForegroundColor.HasValue)
        {
            state.ForegroundColor = new Color(NamedColor.Black);
        }
        else if (!state.BackgroundColor.HasValue)
        {
            state.BackgroundColor = new Color(NamedColor.White);
        }
    }

    /// <summary>
    ///     Check if SGR state is the default (no styling).
    /// </summary>
    /// <param name="state">The SGR state to check</param>
    /// <returns>True if the state represents default styling</returns>
    public static bool IsDefaultState(SgrState state)
    {
        var defaultState = SgrState.CreateDefault();
        return state.Equals(defaultState);
    }
}