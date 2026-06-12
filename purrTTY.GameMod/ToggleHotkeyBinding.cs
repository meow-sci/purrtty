using System.Reflection;
using Brutal.ImGuiApi;
using purrTTY.Display.Configuration;

namespace purrTTY.GameMod;

internal readonly struct ToggleHotkeyBinding
{
    public static ToggleHotkeyBinding Default => new(ImGuiKey.F12, shift: false, ctrl: false, alt: false, super: false);

    public static IReadOnlyList<ImGuiKey> CapturableKeys { get; } =
    [
        ImGuiKey.Enter,
        ImGuiKey.Backspace,
        ImGuiKey.Tab,
        ImGuiKey.Escape,
        ImGuiKey.Space,
        ImGuiKey.UpArrow,
        ImGuiKey.DownArrow,
        ImGuiKey.RightArrow,
        ImGuiKey.LeftArrow,
        ImGuiKey.Home,
        ImGuiKey.End,
        ImGuiKey.Delete,
        ImGuiKey.Insert,
        ImGuiKey.PageUp,
        ImGuiKey.PageDown,
        ImGuiKey.F1,
        ImGuiKey.F2,
        ImGuiKey.F3,
        ImGuiKey.F4,
        ImGuiKey.F5,
        ImGuiKey.F6,
        ImGuiKey.F7,
        ImGuiKey.F8,
        ImGuiKey.F9,
        ImGuiKey.F10,
        ImGuiKey.F11,
        ImGuiKey.F12,
        ImGuiKey._0,
        ImGuiKey._1,
        ImGuiKey._2,
        ImGuiKey._3,
        ImGuiKey._4,
        ImGuiKey._5,
        ImGuiKey._6,
        ImGuiKey._7,
        ImGuiKey._8,
        ImGuiKey._9,
        ImGuiKey.GraveAccent,
        ImGuiKey.Minus,
        ImGuiKey.Equal,
        ImGuiKey.LeftBracket,
        ImGuiKey.RightBracket,
        ImGuiKey.Backslash,
        ImGuiKey.Semicolon,
        ImGuiKey.Apostrophe,
        ImGuiKey.Comma,
        ImGuiKey.Period,
        ImGuiKey.Slash,
        ImGuiKey.A,
        ImGuiKey.B,
        ImGuiKey.C,
        ImGuiKey.D,
        ImGuiKey.E,
        ImGuiKey.F,
        ImGuiKey.G,
        ImGuiKey.H,
        ImGuiKey.I,
        ImGuiKey.J,
        ImGuiKey.K,
        ImGuiKey.L,
        ImGuiKey.M,
        ImGuiKey.N,
        ImGuiKey.O,
        ImGuiKey.P,
        ImGuiKey.Q,
        ImGuiKey.R,
        ImGuiKey.S,
        ImGuiKey.T,
        ImGuiKey.U,
        ImGuiKey.V,
        ImGuiKey.W,
        ImGuiKey.X,
        ImGuiKey.Y,
        ImGuiKey.Z,
        ImGuiKey.Keypad0,
        ImGuiKey.Keypad1,
        ImGuiKey.Keypad2,
        ImGuiKey.Keypad3,
        ImGuiKey.Keypad4,
        ImGuiKey.Keypad5,
        ImGuiKey.Keypad6,
        ImGuiKey.Keypad7,
        ImGuiKey.Keypad8,
        ImGuiKey.Keypad9,
        ImGuiKey.KeypadDecimal,
        ImGuiKey.KeypadDivide,
        ImGuiKey.KeypadMultiply,
        ImGuiKey.KeypadSubtract,
        ImGuiKey.KeypadAdd,
        ImGuiKey.KeypadEqual,
        ImGuiKey.KeypadEnter
    ];

    public ToggleHotkeyBinding(ImGuiKey key, bool shift, bool ctrl, bool alt, bool super)
    {
        Key = key;
        Shift = shift;
        Ctrl = ctrl;
        Alt = alt;
        Super = super;
    }

    public ImGuiKey Key { get; }

    public bool Shift { get; }

    public bool Ctrl { get; }

    public bool Alt { get; }

    public bool Super { get; }

    public bool IsDefault =>
        Key == Default.Key && Shift == Default.Shift && Ctrl == Default.Ctrl && Alt == Default.Alt && Super == Default.Super;

    public static ToggleHotkeyBinding FromConfiguration(ThemeConfiguration configuration)
    {
        if (string.IsNullOrWhiteSpace(configuration.ToggleHotkeyKey))
        {
            return Default;
        }

        if (!Enum.TryParse(configuration.ToggleHotkeyKey, ignoreCase: true, out ImGuiKey key) || !IsCapturableKey(key))
        {
            return Default;
        }

        return new ToggleHotkeyBinding(
            key,
            configuration.ToggleHotkeyShift,
            configuration.ToggleHotkeyCtrl,
            configuration.ToggleHotkeyAlt,
            configuration.ToggleHotkeySuper);
    }

    public void WriteToConfiguration(ThemeConfiguration configuration)
    {
        if (IsDefault)
        {
            configuration.ToggleHotkeyKey = null;
            configuration.ToggleHotkeyShift = false;
            configuration.ToggleHotkeyCtrl = false;
            configuration.ToggleHotkeyAlt = false;
            configuration.ToggleHotkeySuper = false;
            return;
        }

        configuration.ToggleHotkeyKey = Key.ToString();
        configuration.ToggleHotkeyShift = Shift;
        configuration.ToggleHotkeyCtrl = Ctrl;
        configuration.ToggleHotkeyAlt = Alt;
        configuration.ToggleHotkeySuper = Super;
    }

    public bool MatchesPress(ImGuiIOPtr io)
    {
        // Don't fire while an ImGui text field has focus: a toggle bound to a
        // printable key (e.g. 'T') must not toggle the terminal when that key is
        // typed into the game console or any mod text box. repeat:false stops a
        // held hotkey from rapid-toggling every frame.
        if (io.WantTextInput || !ImGui.IsKeyPressed(Key, repeat: false))
        {
            return false;
        }

        bool super = ImGuiHotkeyHelpers.GetSuperModifier(io);

        return io.KeyShift == Shift && io.KeyCtrl == Ctrl && io.KeyAlt == Alt && super == Super;
    }

    public string ToDisplayString()
    {
        var parts = new List<string>(5);

        if (Ctrl)
        {
            parts.Add("Ctrl");
        }

        if (Shift)
        {
            parts.Add("Shift");
        }

        if (Alt)
        {
            parts.Add("Alt");
        }

        if (Super)
        {
            parts.Add("Super");
        }

        parts.Add(GetKeyLabel(Key));
        return string.Join(" + ", parts);
    }

    public string ToShortcutString()
    {
        var parts = new List<string>(5);

        if (Ctrl)
        {
            parts.Add("Ctrl");
        }

        if (Shift)
        {
            parts.Add("Shift");
        }

        if (Alt)
        {
            parts.Add("Alt");
        }

        if (Super)
        {
            parts.Add("Super");
        }

        parts.Add(GetKeyLabel(Key));
        return string.Join("+", parts);
    }

    public static bool IsCapturableKey(ImGuiKey key)
    {
        return CapturableKeys.Contains(key);
    }

    private static string GetKeyLabel(ImGuiKey key)
    {
        return key switch
        {
            ImGuiKey.Enter => "Enter",
            ImGuiKey.Backspace => "Backspace",
            ImGuiKey.Tab => "Tab",
            ImGuiKey.Escape => "Escape",
            ImGuiKey.UpArrow => "Up",
            ImGuiKey.DownArrow => "Down",
            ImGuiKey.LeftArrow => "Left",
            ImGuiKey.RightArrow => "Right",
            ImGuiKey.PageUp => "Page Up",
            ImGuiKey.PageDown => "Page Down",
            ImGuiKey._0 => "0",
            ImGuiKey._1 => "1",
            ImGuiKey._2 => "2",
            ImGuiKey._3 => "3",
            ImGuiKey._4 => "4",
            ImGuiKey._5 => "5",
            ImGuiKey._6 => "6",
            ImGuiKey._7 => "7",
            ImGuiKey._8 => "8",
            ImGuiKey._9 => "9",
            ImGuiKey.GraveAccent => "`",
            ImGuiKey.Minus => "-",
            ImGuiKey.Equal => "=",
            ImGuiKey.LeftBracket => "[",
            ImGuiKey.RightBracket => "]",
            ImGuiKey.Backslash => "\\",
            ImGuiKey.Semicolon => ";",
            ImGuiKey.Apostrophe => "'",
            ImGuiKey.Comma => ",",
            ImGuiKey.Period => ".",
            ImGuiKey.Slash => "/",
            ImGuiKey.Keypad0 => "Numpad 0",
            ImGuiKey.Keypad1 => "Numpad 1",
            ImGuiKey.Keypad2 => "Numpad 2",
            ImGuiKey.Keypad3 => "Numpad 3",
            ImGuiKey.Keypad4 => "Numpad 4",
            ImGuiKey.Keypad5 => "Numpad 5",
            ImGuiKey.Keypad6 => "Numpad 6",
            ImGuiKey.Keypad7 => "Numpad 7",
            ImGuiKey.Keypad8 => "Numpad 8",
            ImGuiKey.Keypad9 => "Numpad 9",
            ImGuiKey.KeypadDecimal => "Numpad .",
            ImGuiKey.KeypadDivide => "Numpad /",
            ImGuiKey.KeypadMultiply => "Numpad *",
            ImGuiKey.KeypadSubtract => "Numpad -",
            ImGuiKey.KeypadAdd => "Numpad +",
            ImGuiKey.KeypadEqual => "Numpad =",
            ImGuiKey.KeypadEnter => "Numpad Enter",
            _ => key.ToString()
        };
    }
}

internal static class ImGuiHotkeyHelpers
{
    private static readonly PropertyInfo? KeySuperProperty = typeof(ImGuiIOPtr).GetProperty("KeySuper", BindingFlags.Public | BindingFlags.Instance);

    public static bool GetSuperModifier(ImGuiIOPtr io)
    {
        if (KeySuperProperty is null)
        {
            return false;
        }

        if (KeySuperProperty.GetValue(io) is bool keySuper)
        {
            return keySuper;
        }

        return false;
    }
}
