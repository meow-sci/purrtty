using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using Brutal.GlfwApi;
using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace ImGuiPlayground;

internal sealed unsafe class GlfwInput : IDisposable
{
    private readonly GlfwWindow _window;
    private readonly ImGuiIOPtr _io;
    private readonly ImGuiPlatformIO.GetClipboardTextFnDelegate _getClipboard;
    private readonly ImGuiPlatformIO.SetClipboardTextFnDelegate _setClipboard;
    private readonly GlfwCharUnmanagedCallback _characterCallback;
    private readonly GlfwCharUnmanagedCallback _previousCharacterCallback;
    private nint _clipboard;
    private ExceptionDispatchInfo? _callbackFailure;
    private double _previousTime;

    internal GlfwInput(GlfwWindow window)
    {
        _window = window;
        _io = ImGui.GetIO();
        _io.ConfigMacOSXBehaviors = OperatingSystem.IsMacOS();
        _window.OnKey += OnKey;
        _window.OnMouseButton += OnMouseButton;
        _window.OnScroll += OnScroll;
        _window.OnCursorPos += OnCursorPos;
        _window.OnCursorEnter += OnCursorEnter;
        _window.OnFocus += OnFocus;
        _getClipboard = GetClipboard;
        _setClipboard = SetClipboard;
        var platform = ImGui.GetPlatformIO();
        platform.Platform_GetClipboardTextFn = Marshal.GetFunctionPointerForDelegate(_getClipboard);
        platform.Platform_SetClipboardTextFn = Marshal.GetFunctionPointerForDelegate(_setClipboard);
        // BRUTAL's OnChar wrapper narrows uint to ushort. Replace ONLY this callback;
        // all window-state callbacks retain BRUTAL's caching and delegate ownership.
        _characterCallback = OnChar;
        _previousCharacterCallback = Glfw.PInvoke.SetCharCallback(window, _characterCallback);
    }

    internal void NewFrame()
    {
        ThrowIfCallbackFailed();
        var size = _window.Size;
        var pixels = _window.FramebufferSize;
        _io.DisplaySize = new float2(size.X, size.Y);
        _io.DisplayFramebufferScale = new float2((float)pixels.X / size.X, (float)pixels.Y / size.Y);
        double now = Glfw.Time;
        _io.DeltaTime = _previousTime == 0 ? 1f / 60 : (float)Math.Max(now - _previousTime, 0.000001);
        _previousTime = now;
    }

    internal void ThrowIfCallbackFailed() => _callbackFailure?.Throw();

    private byte* GetClipboard(ImGuiContextPtr context)
    {
        try
        {
            if (_clipboard != 0) Marshal.FreeCoTaskMem(_clipboard);
            _clipboard = 0;
            _clipboard = Marshal.StringToCoTaskMemUTF8(Glfw.Clipboard ?? string.Empty);
            return (byte*)_clipboard;
        }
        catch (Exception error)
        {
            _callbackFailure = ExceptionDispatchInfo.Capture(error);
            return null;
        }
    }

    private void SetClipboard(ImGuiContextPtr context, byte* text)
    {
        try { Glfw.Clipboard = Marshal.PtrToStringUTF8((nint)text) ?? string.Empty; }
        catch (Exception error) { _callbackFailure = ExceptionDispatchInfo.Capture(error); }
    }

    private void Modifiers()
    {
        bool Down(GlfwKey key) => _window.GetKey(key) == GlfwKeyAction.Press;
        _io.AddKeyEvent(ImGuiKey.Mod_Ctrl, Down(GlfwKey.LeftControl) || Down(GlfwKey.RightControl));
        _io.AddKeyEvent(ImGuiKey.Mod_Shift, Down(GlfwKey.LeftShift) || Down(GlfwKey.RightShift));
        _io.AddKeyEvent(ImGuiKey.Mod_Alt, Down(GlfwKey.LeftAlt) || Down(GlfwKey.RightAlt));
        _io.AddKeyEvent(ImGuiKey.Mod_Super, Down(GlfwKey.LeftSuper) || Down(GlfwKey.RightSuper));
    }

    private void OnKey(GlfwWindow window, GlfwKey key, int scancode, GlfwKeyAction action, GlfwModifier modifiers)
    {
        if (action == GlfwKeyAction.Repeat) return;
        Modifiers();
        // Printable GLFW tokens denote US physical positions, not layout-specific letters.
        string? name = key is >= GlfwKey.Space and <= GlfwKey.World2 ? Glfw.GetKeyName(key, scancode) : null;
        ImGuiKey mapped = MapKey(TranslatePrintableKey(key, name));
        if (mapped == ImGuiKey.None) return;
        _io.AddKeyEvent(mapped, action == GlfwKeyAction.Press);
        _io.SetKeyEventNativeData(mapped, (int)key, scancode);
    }

    private void OnChar(GlfwWindowHandle* window, uint character)
    {
        try { _io.AddInputCharacter(character); }
        catch (Exception error) { _callbackFailure = ExceptionDispatchInfo.Capture(error); }
    }
    private void OnScroll(GlfwWindow window, double2 offset) => _io.AddMouseWheelEvent((float)offset.X, (float)offset.Y);
    private void OnCursorPos(GlfwWindow window, double2 position) => _io.AddMousePosEvent((float)position.X, (float)position.Y);
    private void OnCursorEnter(GlfwWindow window, bool entered)
    {
        if (entered) OnCursorPos(window, window.CursorPos);
        else _io.AddMousePosEvent(-float.MaxValue, -float.MaxValue);
    }
    private void OnFocus(GlfwWindow window, GlfwFocusEvent focus) => _io.AddFocusEvent(focus == GlfwFocusEvent.GainFocus);
    private void OnMouseButton(GlfwWindow window, GlfwMouseButton button, GlfwButtonAction action, GlfwModifier modifiers)
    {
        Modifiers();
        if ((uint)button < 5) _io.AddMouseButtonEvent((int)button, action == GlfwButtonAction.Press);
    }

    internal static GlfwKey TranslatePrintableKey(GlfwKey key, string? name)
    {
        if (key is >= GlfwKey.Kp0 and <= GlfwKey.KpEqual || name is not { Length: 1 }) return key;
        char character = name[0];
        if (character is >= 'a' and <= 'z') return GlfwKey.A + (character - 'a');
        if (character is >= 'A' and <= 'Z') return GlfwKey.A + (character - 'A');
        if (character is >= '0' and <= '9') return GlfwKey.Number0 + (character - '0');
        return "`-=[]\\,;'./".Contains(character) ? (GlfwKey)character : key;
    }

    internal static ImGuiKey MapKey(GlfwKey key)
    {
        if (key >= GlfwKey.A && key <= GlfwKey.Z) return ImGuiKey.A + (key - GlfwKey.A);
        if (key >= GlfwKey.Number0 && key <= GlfwKey.Number9) return ImGuiKey._0 + (key - GlfwKey.Number0);
        if (key >= GlfwKey.F1 && key <= GlfwKey.F24) return ImGuiKey.F1 + (key - GlfwKey.F1);
        if (key >= GlfwKey.Kp0 && key <= GlfwKey.Kp9) return ImGuiKey.Keypad0 + (key - GlfwKey.Kp0);
        return key switch
        {
            GlfwKey.Tab => ImGuiKey.Tab, GlfwKey.Left => ImGuiKey.LeftArrow,
            GlfwKey.Right => ImGuiKey.RightArrow, GlfwKey.Up => ImGuiKey.UpArrow,
            GlfwKey.Down => ImGuiKey.DownArrow, GlfwKey.PageUp => ImGuiKey.PageUp,
            GlfwKey.PageDown => ImGuiKey.PageDown, GlfwKey.Home => ImGuiKey.Home,
            GlfwKey.End => ImGuiKey.End, GlfwKey.Insert => ImGuiKey.Insert,
            GlfwKey.Delete => ImGuiKey.Delete, GlfwKey.Backspace => ImGuiKey.Backspace,
            GlfwKey.Space => ImGuiKey.Space, GlfwKey.Enter => ImGuiKey.Enter,
            GlfwKey.Escape => ImGuiKey.Escape, GlfwKey.Apostrophe => ImGuiKey.Apostrophe,
            GlfwKey.Comma => ImGuiKey.Comma, GlfwKey.Minus => ImGuiKey.Minus,
            GlfwKey.Period => ImGuiKey.Period, GlfwKey.Slash => ImGuiKey.Slash,
            GlfwKey.Semicolon => ImGuiKey.Semicolon, GlfwKey.Equal => ImGuiKey.Equal,
            GlfwKey.LeftBracket => ImGuiKey.LeftBracket, GlfwKey.Backslash => ImGuiKey.Backslash,
            GlfwKey.RightBracket => ImGuiKey.RightBracket, GlfwKey.GraveAccent => ImGuiKey.GraveAccent,
            GlfwKey.CapsLock => ImGuiKey.CapsLock, GlfwKey.ScrollLock => ImGuiKey.ScrollLock,
            GlfwKey.NumLock => ImGuiKey.NumLock, GlfwKey.PrintScreen => ImGuiKey.PrintScreen,
            GlfwKey.Pause => ImGuiKey.Pause, GlfwKey.KpDecimal => ImGuiKey.KeypadDecimal,
            GlfwKey.KpDivide => ImGuiKey.KeypadDivide, GlfwKey.KpMultiply => ImGuiKey.KeypadMultiply,
            GlfwKey.KpSubtract => ImGuiKey.KeypadSubtract, GlfwKey.KpAdd => ImGuiKey.KeypadAdd,
            GlfwKey.KpEnter => ImGuiKey.KeypadEnter, GlfwKey.KpEqual => ImGuiKey.KeypadEqual,
            GlfwKey.LeftShift => ImGuiKey.LeftShift, GlfwKey.RightShift => ImGuiKey.RightShift,
            GlfwKey.LeftControl => ImGuiKey.LeftCtrl, GlfwKey.RightControl => ImGuiKey.RightCtrl,
            GlfwKey.LeftAlt => ImGuiKey.LeftAlt, GlfwKey.RightAlt => ImGuiKey.RightAlt,
            GlfwKey.LeftSuper => ImGuiKey.LeftSuper, GlfwKey.RightSuper => ImGuiKey.RightSuper,
            GlfwKey.Menu => ImGuiKey.Menu, GlfwKey.World1 or GlfwKey.World2 => ImGuiKey.Oem102,
            _ => ImGuiKey.None
        };
    }

    public void Dispose()
    {
        Glfw.PInvoke.SetCharCallback(_window, _previousCharacterCallback);
        GC.KeepAlive(_characterCallback);
        _window.OnKey -= OnKey;
        _window.OnMouseButton -= OnMouseButton;
        _window.OnScroll -= OnScroll;
        _window.OnCursorPos -= OnCursorPos;
        _window.OnCursorEnter -= OnCursorEnter;
        _window.OnFocus -= OnFocus;
        var platform = ImGui.GetPlatformIO();
        platform.Platform_GetClipboardTextFn = platform.Platform_SetClipboardTextFn = 0;
        if (_clipboard != 0) Marshal.FreeCoTaskMem(_clipboard);
        _clipboard = 0;
    }
}
