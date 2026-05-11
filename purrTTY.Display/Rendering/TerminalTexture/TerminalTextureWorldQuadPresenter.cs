namespace purrTTY.Display.Rendering.TerminalTexture;

/// <summary>
/// Publishes the current terminal render texture to KSA's world mesh pass.
/// </summary>
public static class TerminalTextureWorldQuadPresenter
{
    private static TerminalRenderServices? _services;
    private static TerminalRenderTexture? _texture;
    private static TerminalWorldQuad? _quad;
    private static float _textureWidth;
    private static float _textureHeight;

    public static bool Enabled { get; set; }

    internal static void SetSource(TerminalRenderTexture texture, float textureWidth, float textureHeight)
    {
        _texture = texture;
        _textureWidth = textureWidth;
        _textureHeight = textureHeight;
    }

    internal static void ClearSource()
    {
        _texture = null;
        _textureWidth = 0f;
        _textureHeight = 0f;
    }

    public static void DrawCurrent()
    {
        if (!Enabled || _texture == null || TerminalRenderServices.Current is not { } services)
        {
            DisposeQuad();
            return;
        }

        if (!ReferenceEquals(_services, services))
        {
            DisposeQuad();
            _services = services;
        }

        _quad ??= new TerminalWorldQuad(services);
        _quad.Draw(_texture, _textureWidth, _textureHeight);
    }

    public static void Dispose()
    {
        DisposeQuad();
        ClearSource();
        Enabled = false;
    }

    private static void DisposeQuad()
    {
        _quad?.Dispose();
        _quad = null;
        _services = null;
    }
}