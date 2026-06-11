namespace PurrTTY.Terminal.Rendering;

/// <summary>
/// A renderer-neutral 8-bit RGBA color. The terminal engine resolves cell
/// colors to opaque RGB; alpha is carried for frontend conveniences such as
/// background opacity and selection tinting.
/// </summary>
public readonly struct RgbaColor : IEquatable<RgbaColor>
{
    public byte R { get; }
    public byte G { get; }
    public byte B { get; }
    public byte A { get; }

    public RgbaColor(byte r, byte g, byte b, byte a = 255)
    {
        R = r;
        G = g;
        B = b;
        A = a;
    }

    public static RgbaColor Rgb(byte r, byte g, byte b) => new(r, g, b);

    public RgbaColor WithAlpha(byte a) => new(R, G, B, a);

    public bool Equals(RgbaColor other) => R == other.R && G == other.G && B == other.B && A == other.A;

    public override bool Equals(object? obj) => obj is RgbaColor other && Equals(other);

    public override int GetHashCode() => (R << 24) | (G << 16) | (B << 8) | A;

    public static bool operator ==(RgbaColor left, RgbaColor right) => left.Equals(right);

    public static bool operator !=(RgbaColor left, RgbaColor right) => !left.Equals(right);

    public override string ToString() => $"#{R:X2}{G:X2}{B:X2}{A:X2}";
}
