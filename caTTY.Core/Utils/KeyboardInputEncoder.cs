using System;
using System.Text;

namespace caTTY.Core.Utils;

/// <summary>
/// Encodes keyboard input events to terminal byte sequences.
/// Matches the TypeScript implementation's encodeKeyDownToTerminalBytes function.
/// </summary>
public static class KeyboardInputEncoder
{
    /// <summary>
    /// Encodes a keyboard event to terminal bytes.
    /// </summary>
    /// <param name="key">The key that was pressed</param>
    /// <param name="modifiers">The modifier keys that were held</param>
    /// <param name="applicationCursorKeys">Whether application cursor keys mode is enabled</param>
    /// <returns>The encoded terminal sequence, or null if the key should not be processed</returns>
    public static string? EncodeKeyEvent(string key, KeyModifiers modifiers, bool applicationCursorKeys)
    {
        // Ignore input when Meta key is held (let browser/OS shortcuts work)
        if (modifiers.Meta)
        {
            return null;
        }

        // Handle Ctrl combinations first (highest priority)
        if (modifiers.Ctrl)
        {
            var ctrlSequence = EncodeCtrlKey(key);
            if (ctrlSequence != null)
            {
                return ctrlSequence;
            }
        }

        // Handle special keys
        switch (key)
        {
            case "Enter":
                return "\r";
            case "Backspace":
                // Most shells in raw mode expect DEL (0x7f) for backspace
                return "\x7f";
            case "Tab":
                return "\t";
            case "Escape":
                return "\x1b";
            
            // Arrow keys - application cursor keys mode changes sequences
            case "ArrowUp":
                return applicationCursorKeys ? "\x1bOA" : "\x1b[A";
            case "ArrowDown":
                return applicationCursorKeys ? "\x1bOB" : "\x1b[B";
            case "ArrowRight":
                return applicationCursorKeys ? "\x1bOC" : "\x1b[C";
            case "ArrowLeft":
                return applicationCursorKeys ? "\x1bOD" : "\x1b[D";
            
            // Navigation keys
            case "Home":
                return "\x1b[H";
            case "End":
                return "\x1b[F";
            case "Delete":
                return "\x1b[3~";
            case "Insert":
                return "\x1b[2~";
            case "PageUp":
                return "\x1b[5~";
            case "PageDown":
                return "\x1b[6~";
            
            // Function keys F1-F4 (SS3 format or CSI with modifiers)
            case "F1":
            case "F2":
            case "F3":
            case "F4":
                return EncodeFunctionKey1To4(key, modifiers);
            
            // Function keys F5-F12 (CSI format)
            case "F5":
            case "F6":
            case "F7":
            case "F8":
            case "F9":
            case "F10":
            case "F11":
            case "F12":
                return EncodeFunctionKey5To12(key, modifiers);
        }

        // Handle single character keys
        if (key.Length == 1)
        {
            char ch = key[0];
            
            // Alt as ESC prefix (for plain ASCII characters)
            if (modifiers.Alt)
            {
                if (ch >= 0x20 && ch <= 0x7e) // Printable ASCII
                {
                    return "\x1b" + key;
                }
            }
            
            // Return the character as-is for normal typing
            return key;
        }

        // Ignore non-text keys (Shift, Alt, etc)
        return null;
    }

    /// <summary>
    /// Encodes Ctrl+key combinations to control characters.
    /// </summary>
    private static string? EncodeCtrlKey(string key)
    {
        if (key.Length != 1)
        {
            return null;
        }

        char ch = key.ToUpperInvariant()[0];
        
        // Special cases for common control characters
        switch (ch)
        {
            case 'C':
                return "\x03"; // ETX (End of Text)
            case 'D':
                return "\x04"; // EOT (End of Transmission)
            case 'Z':
                return "\x1a"; // SUB (Substitute)
            case 'H':
                return "\x08"; // BS (Backspace)
            case 'I':
                return "\t";   // HT (Horizontal Tab)
            case 'J':
                return "\n";   // LF (Line Feed)
            case 'M':
                return "\r";   // CR (Carriage Return)
            case '[':
                return "\x1b"; // ESC (Escape)
            case '\\':
                return "\x1c"; // FS (File Separator)
            case ']':
                return "\x1d"; // GS (Group Separator)
            case '^':
                return "\x1e"; // RS (Record Separator)
            case '_':
                return "\x1f"; // US (Unit Separator)
        }

        // Generic Ctrl+letter mapping (A-Z)
        if (ch >= 'A' && ch <= 'Z')
        {
            return ((char)(ch - 'A' + 1)).ToString();
        }

        return null;
    }

    /// <summary>
    /// Encodes function keys F1-F4 with modifier support.
    /// </summary>
    private static string EncodeFunctionKey1To4(string key, KeyModifiers modifiers)
    {
        char final = key switch
        {
            "F1" => 'P',
            "F2" => 'Q',
            "F3" => 'R',
            "F4" => 'S',
            _ => 'P' // Fallback
        };

        int mod = CalculateXtermModifierParam(modifiers);
        
        if (mod == 1)
        {
            // No modifiers: SS3 format
            return $"\x1bO{final}";
        }
        else
        {
            // With modifiers: CSI format
            return $"\x1b[1;{mod}{final}";
        }
    }

    /// <summary>
    /// Encodes function keys F5-F12 with modifier support.
    /// </summary>
    private static string EncodeFunctionKey5To12(string key, KeyModifiers modifiers)
    {
        int code = key switch
        {
            "F5" => 15,
            "F6" => 17,
            "F7" => 18,
            "F8" => 19,
            "F9" => 20,
            "F10" => 21,
            "F11" => 23,
            "F12" => 24,
            _ => 15 // Fallback
        };

        int mod = CalculateXtermModifierParam(modifiers);
        
        if (mod == 1)
        {
            // No modifiers
            return $"\x1b[{code}~";
        }
        else
        {
            // With modifiers
            return $"\x1b[{code};{mod}~";
        }
    }

    /// <summary>
    /// Calculates the xterm modifier parameter.
    /// xterm modifier encoding: 1 + (shift?1) + (alt?2) + (ctrl?4)
    /// </summary>
    private static int CalculateXtermModifierParam(KeyModifiers modifiers)
    {
        int mod = 1;
        if (modifiers.Shift) mod += 1;
        if (modifiers.Alt) mod += 2;
        if (modifiers.Ctrl) mod += 4;
        return mod;
    }
}

/// <summary>
/// Represents the modifier keys that can be held during a keyboard event.
/// </summary>
public struct KeyModifiers
{
    public bool Shift { get; init; }
    public bool Alt { get; init; }
    public bool Ctrl { get; init; }
    public bool Meta { get; init; }

    public KeyModifiers(bool shift = false, bool alt = false, bool ctrl = false, bool meta = false)
    {
        Shift = shift;
        Alt = alt;
        Ctrl = ctrl;
        Meta = meta;
    }
}