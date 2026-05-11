using Brutal.ImGuiApi;
using Brutal.Numerics;
using purrTTY.Core.Terminal;
using purrTTY.Display.Controllers.TerminalUi;
using purrTTY.Display.Types;

namespace purrTTY.Display.Rendering.TerminalTexture;

internal sealed class TerminalTextureSoftwareRenderer
{
    private byte[] _pixels = [];

    public void RenderToTexture(
        TerminalRenderTexture texture,
        TerminalSession session,
        TerminalGridRenderer gridRenderer,
        float characterWidth,
        float lineHeight)
    {
        int width = texture.Width;
        int height = texture.Height;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        int byteCount = width * height * 4;
        if (_pixels.Length != byteCount)
        {
            _pixels = new byte[byteCount];
        }

        var background = OpacityManager.ApplyBackgroundOpacity(ThemeManager.GetDefaultBackground());
        Fill(ImGui.ColorConvertFloat4ToU32(background));

        var target = new PixelTerminalDrawTarget(_pixels, width, height, characterWidth, lineHeight);
        gridRenderer.Render(session, target, float2.Zero, characterWidth, lineHeight, default(TextSelection));
        texture.UploadRgba(_pixels);
    }

    private void Fill(uint color)
    {
        DecodeColor(color, out byte sourceR, out byte sourceG, out byte sourceB, out byte sourceA);
        for (int i = 0; i < _pixels.Length; i += 4)
        {
            _pixels[i] = sourceR;
            _pixels[i + 1] = sourceG;
            _pixels[i + 2] = sourceB;
            _pixels[i + 3] = sourceA;
        }
    }

    private static void DecodeColor(uint color, out byte r, out byte g, out byte b, out byte a)
    {
        r = (byte)(color & 0xFF);
        g = (byte)((color >> 8) & 0xFF);
        b = (byte)((color >> 16) & 0xFF);
        a = (byte)((color >> 24) & 0xFF);
    }

    private sealed class PixelTerminalDrawTarget : ITerminalDrawTarget
    {
        private readonly byte[] _pixels;
        private readonly int _width;
        private readonly int _height;
        private readonly float _characterWidth;
        private readonly float _lineHeight;

        public PixelTerminalDrawTarget(byte[] pixels, int width, int height, float characterWidth, float lineHeight)
        {
            _pixels = pixels;
            _width = width;
            _height = height;
            _characterWidth = MathF.Max(1f, characterWidth);
            _lineHeight = MathF.Max(1f, lineHeight);
        }

        public void AddRectFilled(float2 pMin, float2 pMax, uint col)
        {
            int minX = ClampToWidth((int)MathF.Floor(pMin.X));
            int minY = ClampToHeight((int)MathF.Floor(pMin.Y));
            int maxX = ClampToWidth((int)MathF.Ceiling(pMax.X));
            int maxY = ClampToHeight((int)MathF.Ceiling(pMax.Y));
            FillRect(minX, minY, maxX, maxY, col);
        }

        public void AddText(float2 pos, uint col, string text, ImFontPtr font, float fontSize)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            float cursorX = pos.X;
            foreach (char character in text)
            {
                DrawGlyph(character, cursorX, pos.Y, col);
                cursorX += _characterWidth;
            }
        }

        public void DrawLine(float2 p1, float2 p2, uint col, float thickness)
        {
            int minX = ClampToWidth((int)MathF.Floor(MathF.Min(p1.X, p2.X) - thickness));
            int maxX = ClampToWidth((int)MathF.Ceiling(MathF.Max(p1.X, p2.X) + thickness));
            int minY = ClampToHeight((int)MathF.Floor(MathF.Min(p1.Y, p2.Y) - thickness));
            int maxY = ClampToHeight((int)MathF.Ceiling(MathF.Max(p1.Y, p2.Y) + thickness));
            FillRect(minX, minY, maxX, maxY, col);
        }

        public void DrawCurlyUnderline(float2 pos, float4 color, float thickness, float width, float height)
        {
            DrawLine(new float2(pos.X, pos.Y + height - 2f), new float2(pos.X + width, pos.Y + height - 2f), ImGui.ColorConvertFloat4ToU32(color), thickness);
        }

        public void DrawDottedUnderline(float2 pos, float4 color, float thickness, float width, float height)
        {
            uint col = ImGui.ColorConvertFloat4ToU32(color);
            float y = pos.Y + height - 2f;
            for (float x = pos.X; x < pos.X + width; x += MathF.Max(2f, thickness * 2f))
            {
                FillRect(ClampToWidth((int)x), ClampToHeight((int)y), ClampToWidth((int)(x + thickness)), ClampToHeight((int)(y + thickness)), col);
            }
        }

        public void DrawDashedUnderline(float2 pos, float4 color, float thickness, float width, float height)
        {
            uint col = ImGui.ColorConvertFloat4ToU32(color);
            float y = pos.Y + height - 2f;
            for (float x = pos.X; x < pos.X + width; x += MathF.Max(6f, thickness * 4f))
            {
                DrawLine(new float2(x, y), new float2(MathF.Min(pos.X + width, x + 4f), y), col, thickness);
            }
        }

        private void DrawGlyph(char character, float x, float y, uint color)
        {
            ReadOnlySpan<byte> rows = TerminalBitmapFont.GetRows(character);
            if (rows.IsEmpty)
            {
                return;
            }

            int scale = Math.Max(1, (int)MathF.Floor(MathF.Min(_characterWidth / 6f, _lineHeight / 8f)));
            int glyphWidth = 5 * scale;
            int glyphHeight = 7 * scale;
            int startX = (int)MathF.Round(x + MathF.Max(0f, (_characterWidth - glyphWidth) / 2f));
            int startY = (int)MathF.Round(y + MathF.Max(0f, (_lineHeight - glyphHeight) / 2f));

            for (int row = 0; row < rows.Length; row++)
            {
                byte bits = rows[row];
                for (int col = 0; col < 5; col++)
                {
                    if ((bits & (1 << (4 - col))) == 0)
                    {
                        continue;
                    }

                    int px = startX + col * scale;
                    int py = startY + row * scale;
                    FillRect(px, py, px + scale, py + scale, color);
                }
            }
        }

        private void FillRect(int minX, int minY, int maxX, int maxY, uint color)
        {
            DecodeColor(color, out byte sourceR, out byte sourceG, out byte sourceB, out byte sourceA);
            if (sourceA == 0)
            {
                return;
            }

            minX = ClampToWidth(minX);
            maxX = ClampToWidth(maxX);
            minY = ClampToHeight(minY);
            maxY = ClampToHeight(maxY);

            for (int y = minY; y < maxY; y++)
            {
                int rowOffset = y * _width * 4;
                for (int x = minX; x < maxX; x++)
                {
                    int index = rowOffset + x * 4;
                    BlendPixel(index, sourceR, sourceG, sourceB, sourceA);
                }
            }
        }

        private void BlendPixel(int index, byte sourceR, byte sourceG, byte sourceB, byte sourceA)
        {
            int inverseAlpha = 255 - sourceA;
            _pixels[index] = (byte)((sourceR * sourceA + _pixels[index] * inverseAlpha) / 255);
            _pixels[index + 1] = (byte)((sourceG * sourceA + _pixels[index + 1] * inverseAlpha) / 255);
            _pixels[index + 2] = (byte)((sourceB * sourceA + _pixels[index + 2] * inverseAlpha) / 255);
            _pixels[index + 3] = (byte)Math.Min(255, sourceA + (_pixels[index + 3] * inverseAlpha) / 255);
        }

        private int ClampToWidth(int value) => Math.Clamp(value, 0, _width);

        private int ClampToHeight(int value) => Math.Clamp(value, 0, _height);
    }

    private static class TerminalBitmapFont
    {
        private static readonly byte[] Space = [0, 0, 0, 0, 0, 0, 0];
        private static readonly byte[] Unknown = [14, 17, 1, 2, 4, 0, 4];

        public static ReadOnlySpan<byte> GetRows(char character)
        {
            return char.ToUpperInvariant(character) switch
            {
                ' ' => Space,
                'A' => [14, 17, 17, 31, 17, 17, 17],
                'B' => [30, 17, 17, 30, 17, 17, 30],
                'C' => [14, 17, 16, 16, 16, 17, 14],
                'D' => [30, 17, 17, 17, 17, 17, 30],
                'E' => [31, 16, 16, 30, 16, 16, 31],
                'F' => [31, 16, 16, 30, 16, 16, 16],
                'G' => [14, 17, 16, 23, 17, 17, 14],
                'H' => [17, 17, 17, 31, 17, 17, 17],
                'I' => [14, 4, 4, 4, 4, 4, 14],
                'J' => [7, 2, 2, 2, 18, 18, 12],
                'K' => [17, 18, 20, 24, 20, 18, 17],
                'L' => [16, 16, 16, 16, 16, 16, 31],
                'M' => [17, 27, 21, 21, 17, 17, 17],
                'N' => [17, 25, 21, 19, 17, 17, 17],
                'O' => [14, 17, 17, 17, 17, 17, 14],
                'P' => [30, 17, 17, 30, 16, 16, 16],
                'Q' => [14, 17, 17, 17, 21, 18, 13],
                'R' => [30, 17, 17, 30, 20, 18, 17],
                'S' => [15, 16, 16, 14, 1, 1, 30],
                'T' => [31, 4, 4, 4, 4, 4, 4],
                'U' => [17, 17, 17, 17, 17, 17, 14],
                'V' => [17, 17, 17, 17, 17, 10, 4],
                'W' => [17, 17, 17, 21, 21, 21, 10],
                'X' => [17, 17, 10, 4, 10, 17, 17],
                'Y' => [17, 17, 10, 4, 4, 4, 4],
                'Z' => [31, 1, 2, 4, 8, 16, 31],
                '0' => [14, 17, 19, 21, 25, 17, 14],
                '1' => [4, 12, 4, 4, 4, 4, 14],
                '2' => [14, 17, 1, 2, 4, 8, 31],
                '3' => [30, 1, 1, 14, 1, 1, 30],
                '4' => [2, 6, 10, 18, 31, 2, 2],
                '5' => [31, 16, 16, 30, 1, 1, 30],
                '6' => [14, 16, 16, 30, 17, 17, 14],
                '7' => [31, 1, 2, 4, 8, 8, 8],
                '8' => [14, 17, 17, 14, 17, 17, 14],
                '9' => [14, 17, 17, 15, 1, 1, 14],
                '.' => [0, 0, 0, 0, 0, 12, 12],
                ',' => [0, 0, 0, 0, 12, 4, 8],
                ':' => [0, 12, 12, 0, 12, 12, 0],
                ';' => [0, 12, 12, 0, 12, 4, 8],
                '-' => [0, 0, 0, 31, 0, 0, 0],
                '_' => [0, 0, 0, 0, 0, 0, 31],
                '+' => [0, 4, 4, 31, 4, 4, 0],
                '/' => [1, 1, 2, 4, 8, 16, 16],
                '\\' => [16, 16, 8, 4, 2, 1, 1],
                '|' => [4, 4, 4, 4, 4, 4, 4],
                '(' => [2, 4, 8, 8, 8, 4, 2],
                ')' => [8, 4, 2, 2, 2, 4, 8],
                '[' => [14, 8, 8, 8, 8, 8, 14],
                ']' => [14, 2, 2, 2, 2, 2, 14],
                '<' => [2, 4, 8, 16, 8, 4, 2],
                '>' => [8, 4, 2, 1, 2, 4, 8],
                '=' => [0, 0, 31, 0, 31, 0, 0],
                '*' => [0, 21, 14, 31, 14, 21, 0],
                '!' => [4, 4, 4, 4, 4, 0, 4],
                '?' => Unknown,
                '~' => [0, 0, 8, 21, 2, 0, 0],
                '^' => [4, 10, 17, 0, 0, 0, 0],
                '@' => [14, 17, 23, 21, 23, 16, 14],
                '#' => [10, 10, 31, 10, 31, 10, 10],
                '$' => [4, 15, 20, 14, 5, 30, 4],
                '%' => [24, 25, 2, 4, 8, 19, 3],
                '&' => [12, 18, 20, 8, 21, 18, 13],
                '"' => [10, 10, 0, 0, 0, 0, 0],
                '\'' => [4, 4, 0, 0, 0, 0, 0],
                '`' => [8, 4, 0, 0, 0, 0, 0],
                _ => Unknown
            };
        }
    }
}