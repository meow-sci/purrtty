using Brutal.ImGuiApi;
using Brutal.Numerics;
using purrTTY.Logging;

namespace purrTTY.Display.Rendering.TerminalTexture;

internal sealed class TerminalTextureDebugPreview : IDisposable
{
    private TerminalRenderTexture? _texture;
    private string? _lastError;

    public TerminalRenderTexture? Texture => _texture;

    public TerminalRenderTexture? EnsureTexture(float width, float height)
    {
        var services = TerminalRenderServices.Current;
        if (services == null)
        {
            if (_lastError != "services-null")
            {
                _lastError = "services-null";
                ModLog.Log.Debug("purrTTY texture rendering unavailable: render services are not installed");
            }

            Dispose();
            return null;
        }

        try
        {
            _texture ??= new TerminalRenderTexture(services);
            _texture.EnsureSize((int)MathF.Ceiling(width), (int)MathF.Ceiling(height));
            return _texture;
        }
        catch (Exception ex)
        {
            if (_lastError != ex.Message)
            {
                _lastError = ex.Message;
                ModLog.Log.Debug($"purrTTY texture allocation failed: {ex.Message}");
            }

            return null;
        }
    }

    public void RenderPreview(float width, float height)
    {
        try
        {
            if (_texture == null)
            {
                return;
            }

            bool open = true;
            ImGui.SetNextWindowSize(new float2(MathF.Min(width + 24f, 900f), MathF.Min(height + 64f, 700f)), ImGuiCond.FirstUseEver);
            if (ImGui.Begin("purrTTY Texture Preview##purrtty_texture_preview", ref open))
            {
                var available = ImGui.GetContentRegionAvail();
                var scale = MathF.Min(available.X / Math.Max(1f, width), available.Y / Math.Max(1f, height));
                scale = MathF.Min(1f, MathF.Max(0.1f, scale));
                var imageSize = new float2(MathF.Max(1f, width * scale), MathF.Max(1f, height * scale));
                ImGui.Image(_texture.ImGuiTexture, in imageSize, new float2(0f, 0f), new float2(1f, 1f));
            }

            ImGui.End();
        }
        catch (Exception ex)
        {
            if (_lastError != ex.Message)
            {
                _lastError = ex.Message;
                ModLog.Log.Debug($"purrTTY texture preview failed: {ex.Message}");
            }
        }
    }

    public void Dispose()
    {
        _texture?.Dispose();
        _texture = null;
    }
}