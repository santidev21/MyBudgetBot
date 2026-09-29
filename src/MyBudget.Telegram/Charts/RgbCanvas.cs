namespace MyBudget.Telegram.Charts;

/// <summary>An opaque 24-bit colour.</summary>
internal readonly record struct Rgb(byte R, byte G, byte B)
{
    public static readonly Rgb White = new(255, 255, 255);

    /// <summary>Near-black used for labels; pure black is harsh on a phone.</summary>
    public static readonly Rgb Ink = new(51, 58, 71);
}

/// <summary>
/// A tiny RGB raster surface.
/// <para>
/// Hand-rolled instead of pulling an imaging library: the charts are rectangles and digits,
/// and a dependency-free renderer works identically in the Debian runtime image, in tests and
/// on a developer laptop, with no native libraries or system fonts to install.
/// </para>
/// </summary>
internal sealed class RgbCanvas(int width, int height)
{
    private readonly byte[] _pixels = new byte[width * height * 3];

    public int Width { get; } = width;

    public int Height { get; } = height;

    public void Fill(Rgb colour) => Rectangle(0, 0, Width, Height, colour);

    public void Rectangle(int x, int y, int rectangleWidth, int rectangleHeight, Rgb colour)
    {
        var left = Math.Max(0, x);
        var top = Math.Max(0, y);
        var right = Math.Min(Width, x + rectangleWidth);
        var bottom = Math.Min(Height, y + rectangleHeight);

        for (var row = top; row < bottom; row++)
        {
            var offset = (row * Width + left) * 3;
            for (var column = left; column < right; column++)
            {
                _pixels[offset++] = colour.R;
                _pixels[offset++] = colour.G;
                _pixels[offset++] = colour.B;
            }
        }
    }

    public Rgb PixelAt(int x, int y)
    {
        var offset = (y * Width + x) * 3;
        return new Rgb(_pixels[offset], _pixels[offset + 1], _pixels[offset + 2]);
    }

    /// <summary>Raw RGB bytes of one scanline, for the PNG encoder.</summary>
    public ReadOnlySpan<byte> RowSpan(int y) => _pixels.AsSpan(y * Width * 3, Width * 3);
}
