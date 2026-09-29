namespace MyBudget.Telegram.Charts;

/// <summary>
/// A 5x7 bitmap font for the only text a chart draws: digits, separators and a few symbols.
/// <para>
/// Category and month names stay in the caption, where Telegram renders them with the phone's
/// own font and full accents. That is what keeps the image dependency-free: no TTF to embed,
/// no fontconfig in the container, and identical pixels in every environment.
/// </para>
/// </summary>
internal static class BitmapFont
{
    public const int GlyphWidth = 5;
    public const int GlyphHeight = 7;

    private const int Advance = GlyphWidth + 1;

    public static int Measure(string text, int scale) =>
        string.IsNullOrEmpty(text) ? 0 : ((text.Length * Advance) - 1) * scale;

    public static void Draw(RgbCanvas canvas, string text, int x, int y, int scale, Rgb colour)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(text);

        var cursor = x;

        foreach (var character in text)
        {
            if (Glyph(character) is { } rows)
            {
                for (var row = 0; row < GlyphHeight; row++)
                {
                    for (var column = 0; column < GlyphWidth; column++)
                    {
                        if ((rows[row] & (1 << (GlyphWidth - 1 - column))) != 0)
                        {
                            canvas.Rectangle(
                                cursor + (column * scale), y + (row * scale), scale, scale, colour);
                        }
                    }
                }
            }

            cursor += Advance * scale;
        }
    }

    private static byte[]? Glyph(char character) => character switch
    {
        '0' => Rows("01110", "10001", "10011", "10101", "11001", "10001", "01110"),
        '1' => Rows("00100", "01100", "00100", "00100", "00100", "00100", "01110"),
        '2' => Rows("01110", "10001", "00001", "00010", "00100", "01000", "11111"),
        '3' => Rows("11111", "00010", "00100", "00010", "00001", "10001", "01110"),
        '4' => Rows("00010", "00110", "01010", "10010", "11111", "00010", "00010"),
        '5' => Rows("11111", "10000", "11110", "00001", "00001", "10001", "01110"),
        '6' => Rows("00110", "01000", "10000", "11110", "10001", "10001", "01110"),
        '7' => Rows("11111", "00001", "00010", "00100", "01000", "01000", "01000"),
        '8' => Rows("01110", "10001", "10001", "01110", "10001", "10001", "01110"),
        '9' => Rows("01110", "10001", "10001", "01111", "00001", "00010", "01100"),
        '.' => Rows("00000", "00000", "00000", "00000", "00000", "00110", "00110"),
        ',' => Rows("00000", "00000", "00000", "00000", "00110", "00110", "00100"),
        '%' => Rows("11001", "11010", "00010", "00100", "01000", "01011", "10011"),
        '$' => Rows("00100", "01111", "10100", "01110", "00101", "11110", "00100"),
        '/' => Rows("00001", "00010", "00010", "00100", "01000", "01000", "10000"),
        '-' => Rows("00000", "00000", "00000", "01110", "00000", "00000", "00000"),
        ' ' => Rows("00000", "00000", "00000", "00000", "00000", "00000", "00000"),
        _ => null,
    };

    private static byte[] Rows(params string[] rows) =>
        rows.Select(row => Convert.ToByte(row, 2)).ToArray();
}
