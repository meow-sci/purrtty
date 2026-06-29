using Brutal.ImGuiApi;
using purrTTY.Logging;
using PurrTTY.Terminal.Input;
using PurrTTY.Terminal.Sessions;
using TKeyMods = PurrTTY.Terminal.Input.KeyModifiers;

namespace purrTTY.Display.Ghostty;

/// <summary>
/// Encodes a frame's keyboard input — read from the <b>current</b> ImGui context's
/// IO — into PTY bytes for a terminal session: named keys, Ctrl/Alt chords, AltGr
/// text, printable text with surrogate pairing, and the Ctrl+Shift+C/V clipboard
/// chords. Shared by the 2D <see cref="TerminalWindow"/> and the in-world terminal
/// so both encode identically. The mouse path stays window-specific (it needs the
/// window's grid geometry).
/// </summary>
public static class TerminalInputEncoder
{
    private static readonly (ImGuiKey ImguiKey, TerminalKey Key)[] NamedKeys =
    {
        (ImGuiKey.Enter, TerminalKey.Enter),
        (ImGuiKey.KeypadEnter, TerminalKey.Enter),
        (ImGuiKey.Backspace, TerminalKey.Backspace),
        (ImGuiKey.Tab, TerminalKey.Tab),
        (ImGuiKey.Escape, TerminalKey.Escape),
        (ImGuiKey.UpArrow, TerminalKey.ArrowUp),
        (ImGuiKey.DownArrow, TerminalKey.ArrowDown),
        (ImGuiKey.RightArrow, TerminalKey.ArrowRight),
        (ImGuiKey.LeftArrow, TerminalKey.ArrowLeft),
        (ImGuiKey.Home, TerminalKey.Home),
        (ImGuiKey.End, TerminalKey.End),
        (ImGuiKey.Delete, TerminalKey.Delete),
        (ImGuiKey.Insert, TerminalKey.Insert),
        (ImGuiKey.PageUp, TerminalKey.PageUp),
        (ImGuiKey.PageDown, TerminalKey.PageDown),
        (ImGuiKey.F1, TerminalKey.F1), (ImGuiKey.F2, TerminalKey.F2), (ImGuiKey.F3, TerminalKey.F3),
        (ImGuiKey.F4, TerminalKey.F4), (ImGuiKey.F5, TerminalKey.F5), (ImGuiKey.F6, TerminalKey.F6),
        (ImGuiKey.F7, TerminalKey.F7), (ImGuiKey.F8, TerminalKey.F8), (ImGuiKey.F9, TerminalKey.F9),
        (ImGuiKey.F10, TerminalKey.F10), (ImGuiKey.F11, TerminalKey.F11), (ImGuiKey.F12, TerminalKey.F12),
    };

    private static readonly (ImGuiKey ImguiKey, TerminalKey Key)[] LetterKeys =
    {
        (ImGuiKey.A, TerminalKey.A), (ImGuiKey.B, TerminalKey.B), (ImGuiKey.C, TerminalKey.C),
        (ImGuiKey.D, TerminalKey.D), (ImGuiKey.E, TerminalKey.E), (ImGuiKey.F, TerminalKey.F),
        (ImGuiKey.G, TerminalKey.G), (ImGuiKey.H, TerminalKey.H), (ImGuiKey.I, TerminalKey.I),
        (ImGuiKey.J, TerminalKey.J), (ImGuiKey.K, TerminalKey.K), (ImGuiKey.L, TerminalKey.L),
        (ImGuiKey.M, TerminalKey.M), (ImGuiKey.N, TerminalKey.N), (ImGuiKey.O, TerminalKey.O),
        (ImGuiKey.P, TerminalKey.P), (ImGuiKey.Q, TerminalKey.Q), (ImGuiKey.R, TerminalKey.R),
        (ImGuiKey.S, TerminalKey.S), (ImGuiKey.T, TerminalKey.T), (ImGuiKey.U, TerminalKey.U),
        (ImGuiKey.V, TerminalKey.V), (ImGuiKey.W, TerminalKey.W), (ImGuiKey.X, TerminalKey.X),
        (ImGuiKey.Y, TerminalKey.Y), (ImGuiKey.Z, TerminalKey.Z),
    };

    private static readonly (ImGuiKey ImguiKey, TerminalKey Key)[] DigitKeys =
    {
        (ImGuiKey._0, TerminalKey.Digit0), (ImGuiKey._1, TerminalKey.Digit1),
        (ImGuiKey._2, TerminalKey.Digit2), (ImGuiKey._3, TerminalKey.Digit3),
        (ImGuiKey._4, TerminalKey.Digit4), (ImGuiKey._5, TerminalKey.Digit5),
        (ImGuiKey._6, TerminalKey.Digit6), (ImGuiKey._7, TerminalKey.Digit7),
        (ImGuiKey._8, TerminalKey.Digit8), (ImGuiKey._9, TerminalKey.Digit9),
    };

    // Ctrl+Space/[/\/] produce the NUL/ESC/FS/GS controls (and ESC-prefixed forms
    // under Alt); the engine's key encoder derives the byte from key + mods.
    private static readonly (ImGuiKey ImguiKey, TerminalKey Key)[] ControlPunctuationKeys =
    {
        (ImGuiKey.Space, TerminalKey.Space),
        (ImGuiKey.LeftBracket, TerminalKey.BracketLeft),
        (ImGuiKey.RightBracket, TerminalKey.BracketRight),
        (ImGuiKey.Backslash, TerminalKey.Backslash),
    };

    // Opt-in keyboard input diagnostics. Set PURRTTY_KEY_DIAG=1 (or =true) before
    // launching KSA, reproduce the issue, then read the "[keydiag]" lines in the
    // log. (Documented at length in the original TerminalWindow.Input.cs.)
    private static readonly bool KeyDiag =
        Environment.GetEnvironmentVariable("PURRTTY_KEY_DIAG") is "1" or "true";

    /// <summary>
    /// Processes the current frame's keyboard input for <paramref name="session"/>,
    /// reading the current ImGui context's IO (<paramref name="io"/>). Optional
    /// <paramref name="suppress"/> skips a frame (e.g. to swallow the open hotkey);
    /// optional <paramref name="onInputSent"/> is raised whenever bytes were sent
    /// (the 2D window uses it to reset the cursor blink phase).
    /// </summary>
    public static void ProcessKeyboard(
        TerminalSession session, ImGuiIOPtr io, Func<bool>? suppress = null, Action? onInputSent = null)
    {
        if (suppress?.Invoke() == true)
        {
            return;
        }

        // Standard terminal clipboard chords; never forwarded to the shell.
        if (io.KeyCtrl && io.KeyShift && ImGui.IsKeyPressed(ImGuiKey.V))
        {
            Paste(session, onInputSent);
            return;
        }

        if (io.KeyCtrl && io.KeyShift && ImGui.IsKeyPressed(ImGuiKey.C))
        {
            Copy(session);
            return;
        }

        var mods = ReadModifiers(io);

        if (KeyDiag)
        {
            LogChordDiagnostics(io);
        }

        foreach (var (imguiKey, key) in NamedKeys)
        {
            if (ImGui.IsKeyPressed(imguiKey))
            {
                EncodeAndSend(session, new TerminalKeyEvent(key, KeyAction.Press, mods), onInputSent);
            }
        }

        // AltGr (Windows international layouts) is delivered as Ctrl+Alt with the
        // produced character in the ImGui character queue. When both are held AND
        // text arrived, the text is what the user typed (@, {, €, ...) — prefer it
        // over a spurious Ctrl+Alt chord (the standard terminal heuristic).
        bool altGrText = io.KeyCtrl && io.KeyAlt && io.InputQueueCharacters.Count > 0;

        // Ctrl/Alt-modified keys never enter the ImGui character queue, so they are
        // encoded from key presses: Ctrl+letter controls, Alt+letter Meta chords,
        // Ctrl/Alt+digit, and the Ctrl punctuation controls (NUL/ESC/FS/GS).
        if ((io.KeyCtrl || io.KeyAlt) && !altGrText)
        {
            foreach (var (imguiKey, key) in LetterKeys)
            {
                if (ImGui.IsKeyPressed(imguiKey))
                {
                    EncodeAndSend(session, new TerminalKeyEvent(key, KeyAction.Press, mods), onInputSent);
                }
            }

            foreach (var (imguiKey, key) in DigitKeys)
            {
                if (ImGui.IsKeyPressed(imguiKey))
                {
                    EncodeAndSend(session, new TerminalKeyEvent(key, KeyAction.Press, mods), onInputSent);
                }
            }

            foreach (var (imguiKey, key) in ControlPunctuationKeys)
            {
                if (ImGui.IsKeyPressed(imguiKey))
                {
                    EncodeAndSend(session, new TerminalKeyEvent(key, KeyAction.Press, mods), onInputSent);
                }
            }
        }

        // Printable text. Skip when Ctrl/Alt are held (command combos handled above)
        // unless it's AltGr input, where the queued characters ARE the typed text.
        if (((!io.KeyCtrl && !io.KeyAlt) || altGrText) && io.InputQueueCharacters.Count > 0)
        {
            int count = io.InputQueueCharacters.Count;
            Span<char> chars = stackalloc char[2];
            Span<byte> utf8 = stackalloc byte[4];
            for (int i = 0; i < count; i++)
            {
                char ch = (char)io.InputQueueCharacters[i];

                // Astral-plane input arrives as two queue entries (a UTF-16 surrogate
                // pair); encode the pair as one code point — a lone half yields U+FFFD.
                if (char.IsHighSurrogate(ch))
                {
                    if (i + 1 < count && char.IsLowSurrogate((char)io.InputQueueCharacters[i + 1]))
                    {
                        chars[0] = ch;
                        chars[1] = (char)io.InputQueueCharacters[++i];
                        Send(session, utf8[..System.Text.Encoding.UTF8.GetBytes(chars, utf8)], onInputSent);
                    }

                    continue; // unpaired high surrogate: drop
                }

                if (char.IsLowSurrogate(ch) || ch < 32 || ch == 127)
                {
                    continue;
                }

                chars[0] = ch;
                Send(session, utf8[..System.Text.Encoding.UTF8.GetBytes(chars[..1], utf8)], onInputSent);
            }
        }
    }

    private static void EncodeAndSend(TerminalSession session, in TerminalKeyEvent keyEvent, Action? onInputSent)
    {
        Span<byte> buf = stackalloc byte[64];
        int n = session.Surface.EncodeKey(keyEvent, buf);
        if (KeyDiag)
        {
            string text = string.IsNullOrEmpty(keyEvent.Text) ? "-" : keyEvent.Text;
            string bytes = n > 0 ? Convert.ToHexString(buf[..n]) : "";
            ModLog.Log.Info(
                $"[keydiag] send key={keyEvent.Key} action={keyEvent.Action} mods={keyEvent.Modifiers} " +
                $"text={text} bytes=[{bytes}] ({n})");
        }

        if (n > 0)
        {
            Send(session, buf[..n], onInputSent);
        }
    }

    private static void Send(TerminalSession session, ReadOnlySpan<byte> bytes, Action? onInputSent)
    {
        session.SendInput(bytes);
        onInputSent?.Invoke();
    }

    private static void Copy(TerminalSession session)
    {
        var text = session.Surface.GetSelectionText();
        if (!string.IsNullOrEmpty(text))
        {
            ImGui.SetClipboardText(text);
        }
    }

    private static void Paste(TerminalSession session, Action? onInputSent)
    {
        var text = ImGui.GetClipboardText();
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var encoded = session.Surface.EncodePaste(System.Text.Encoding.UTF8.GetBytes(text));
        Send(session, encoded, onInputSent);
    }

    /// <summary>Reads the current ImGui modifier levels into terminal key modifiers.</summary>
    public static TKeyMods ReadModifiers(ImGuiIOPtr io)
    {
        var mods = TKeyMods.None;
        if (io.KeyShift) mods |= TKeyMods.Shift;
        if (io.KeyCtrl) mods |= TKeyMods.Ctrl;
        if (io.KeyAlt) mods |= TKeyMods.Alt;
        return mods;
    }

    private static void LogChordDiagnostics(ImGuiIOPtr io)
    {
        LogKeyGroup(io, LetterKeys);
        LogKeyGroup(io, DigitKeys);
        LogKeyGroup(io, ControlPunctuationKeys);
    }

    private static void LogKeyGroup(ImGuiIOPtr io, (ImGuiKey ImguiKey, TerminalKey Key)[] group)
    {
        foreach (var (imguiKey, key) in group)
        {
            if (!ImGui.IsKeyPressed(imguiKey, repeat: true))
            {
                continue;
            }

            // Plain text input is handled by the character queue, not the chord path.
            bool printable = !io.KeyCtrl && !io.KeyAlt && io.InputQueueCharacters.Count > 0;
            if (printable)
            {
                continue;
            }

            bool edge = ImGui.IsKeyPressed(imguiKey, repeat: false);
            ModLog.Log.Info(
                $"[keydiag] press key={key} {(edge ? "edge" : "repeat")} " +
                $"ctrl={io.KeyCtrl} alt={io.KeyAlt} shift={io.KeyShift} " +
                $"chars={io.InputQueueCharacters.Count}");
        }
    }
}
