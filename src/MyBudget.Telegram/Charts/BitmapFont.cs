using System.Globalization;
using System.Text;

namespace MyBudget.Telegram.Charts;

/// <summary>
/// A 5x7 bitmap font for the text a chart draws: digits, money symbols and the letters a
/// category name needs.
/// <para>
/// Accents are folded rather than drawn, so "Alimentación" is rendered as "Alimentacion" inside
/// the five-by-seven grid. Anything the font still does not know (an icon, another script) leaves
/// a blank, and the exact name keeps its icon and accents in the caption, where Telegram renders
/// it with the phone's own font. That is what keeps the image dependency-free: no TTF to embed,
/// no fontconfig in the container, and identical pixels in every environment.
/// </para>
/// </summary>
internal static class BitmapFont
{
    public const int GlyphWidth = 5;
    public const int GlyphHeight = 7;

    private const int Advance = GlyphWidth + 1;

    /// <summary>Appended when a string does not fit, so a cut name cannot read as a full one.</summary>
    private const string CutMark = "..";

    public static int Measure(string text, int scale) =>
        string.IsNullOrEmpty(text) ? 0 : ((Normalized(text).Length * Advance) - 1) * scale;

    /// <summary>
    /// Cuts a string down to the widest it can be in <paramref name="maxWidth"/> pixels. Every
    /// character advances the same number of pixels, so the fit is a division rather than a
    /// measurement loop.
    /// </summary>
    public static string Truncate(string text, int maxWidth, int scale)
    {
        var normalized = string.IsNullOrEmpty(text) ? string.Empty : Normalized(text);

        if (Measure(normalized, scale) <= maxWidth)
        {
            return normalized;
        }

        var room = Math.Max(0, (((maxWidth / scale) + 1) / Advance) - CutMark.Length);

        // Never cut between the halves of a surrogate pair: the result is normalized again.
        if (room > 0 && char.IsHighSurrogate(normalized[room - 1]))
        {
            room--;
        }

        return room == 0 ? string.Empty : normalized[..room] + CutMark;
    }

    public static void Draw(RgbCanvas canvas, string text, int x, int y, int scale, Rgb colour)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(text);

        var cursor = x;

        foreach (var character in Normalized(text))
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

    /// <summary>
    /// Removes the combining marks a five-by-seven glyph cannot hold: <c>á</c> decomposes into
    /// <c>a</c> plus a acute, and the acute is what gets dropped.
    /// </summary>
    private static string Normalized(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var decomposed = text.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);

        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(character);
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
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
        '$' => Rows("00100", "01111", "10100", "00110", "00001", "11110", "00100"),
        '/' => Rows("00001", "00010", "00010", "00100", "01000", "01000", "10000"),
        '-' => Rows("00000", "00000", "00000", "01110", "00000", "00000", "00000"),
        ':' => Rows("00000", "00110", "00110", "00000", "00110", "00110", "00000"),
        '(' => Rows("00010", "00100", "01000", "01000", "01000", "00100", "00010"),
        ')' => Rows("01000", "00100", "00010", "00010", "00010", "00100", "01000"),
        '+' => Rows("00000", "00100", "00100", "11111", "00100", "00100", "00000"),
        '=' => Rows("00000", "00000", "11111", "00000", "11111", "00000", "00000"),
        '?' => Rows("01110", "10001", "00001", "00110", "00100", "00000", "00100"),
        '!' => Rows("00100", "00100", "00100", "00100", "00100", "00000", "00100"),
        '\'' => Rows("00100", "00100", "01000", "00000", "00000", "00000", "00000"),
        '&' => Rows("01100", "10010", "10010", "01100", "10101", "10010", "01101"),
        '·' => Rows("00000", "00000", "00000", "00100", "00000", "00000", "00000"),
        ' ' => Rows("00000", "00000", "00000", "00000", "00000", "00000", "00000"),
        'A' => Rows("01110", "10001", "10001", "11111", "10001", "10001", "10001"),
        'B' => Rows("11110", "10001", "10001", "11110", "10001", "10001", "11110"),
        'C' => Rows("01110", "10001", "10000", "10000", "10000", "10001", "01110"),
        'D' => Rows("11110", "10001", "10001", "10001", "10001", "10001", "11110"),
        'E' => Rows("11111", "10000", "10000", "11110", "10000", "10000", "11111"),
        'F' => Rows("11111", "10000", "10000", "11110", "10000", "10000", "10000"),
        'G' => Rows("01110", "10001", "10000", "10111", "10001", "10001", "01111"),
        'H' => Rows("10001", "10001", "10001", "11111", "10001", "10001", "10001"),
        'I' => Rows("11111", "00100", "00100", "00100", "00100", "00100", "11111"),
        'J' => Rows("00111", "00010", "00010", "00010", "00010", "10010", "01100"),
        'K' => Rows("10001", "10010", "10100", "11000", "10100", "10010", "10001"),
        'L' => Rows("10000", "10000", "10000", "10000", "10000", "10000", "11111"),
        'M' => Rows("10001", "11011", "10101", "10101", "10001", "10001", "10001"),
        'N' => Rows("10001", "10001", "11001", "10101", "10011", "10001", "10001"),
        'O' => Rows("01110", "10001", "10001", "10001", "10001", "10001", "01110"),
        'P' => Rows("11110", "10001", "10001", "11110", "10000", "10000", "10000"),
        'Q' => Rows("01110", "10001", "10001", "10001", "10101", "10010", "01101"),
        'R' => Rows("11110", "10001", "10001", "11110", "10100", "10010", "10001"),
        'S' => Rows("01111", "10000", "10000", "01110", "00001", "00001", "11110"),
        'T' => Rows("11111", "00100", "00100", "00100", "00100", "00100", "00100"),
        'U' => Rows("10001", "10001", "10001", "10001", "10001", "10001", "01110"),
        'V' => Rows("10001", "10001", "10001", "10001", "10001", "01010", "00100"),
        'W' => Rows("10001", "10001", "10001", "10101", "10101", "10101", "01010"),
        'X' => Rows("10001", "10001", "01010", "00100", "01010", "10001", "10001"),
        'Y' => Rows("10001", "10001", "01010", "00100", "00100", "00100", "00100"),
        'Z' => Rows("11111", "00001", "00010", "00100", "01000", "10000", "11111"),
        'a' => Rows("00000", "00000", "01110", "00001", "01111", "10001", "01111"),
        'b' => Rows("10000", "10000", "11110", "10001", "10001", "10001", "11110"),
        'c' => Rows("00000", "00000", "01111", "10000", "10000", "10000", "01111"),
        'd' => Rows("00001", "00001", "01111", "10001", "10001", "10001", "01111"),
        'e' => Rows("00000", "00000", "01110", "10001", "11111", "10000", "01110"),
        'f' => Rows("00110", "01001", "01000", "11110", "01000", "01000", "01000"),
        'g' => Rows("00000", "00000", "01110", "10001", "10001", "01111", "11111"),
        'h' => Rows("10000", "10000", "11110", "10001", "10001", "10001", "10001"),
        'i' => Rows("00100", "00000", "01100", "00100", "00100", "00100", "01110"),
        'j' => Rows("00010", "00000", "00110", "00010", "00010", "10010", "01100"),
        'k' => Rows("10000", "10000", "10010", "10100", "11000", "10100", "10010"),
        'l' => Rows("01100", "00100", "00100", "00100", "00100", "00100", "01110"),
        'm' => Rows("00000", "00000", "11110", "10101", "10101", "10101", "10101"),
        'n' => Rows("00000", "00000", "11110", "10001", "10001", "10001", "10001"),
        'o' => Rows("00000", "00000", "01110", "10001", "10001", "10001", "01110"),
        'p' => Rows("00000", "00000", "11110", "10001", "10001", "11110", "10000"),
        'q' => Rows("00000", "00000", "01110", "10001", "10001", "01111", "00001"),
        'r' => Rows("00000", "00000", "10110", "11001", "10000", "10000", "10000"),
        's' => Rows("00000", "00000", "01111", "10000", "01110", "00001", "11110"),
        't' => Rows("01000", "01000", "11110", "01000", "01000", "01001", "00110"),
        'u' => Rows("00000", "00000", "10001", "10001", "10001", "10011", "01101"),
        'v' => Rows("00000", "00000", "10001", "10001", "10001", "01010", "00100"),
        'w' => Rows("00000", "00000", "10001", "10101", "10101", "10101", "01010"),
        'x' => Rows("00000", "00000", "10001", "01010", "00100", "01010", "10001"),
        'y' => Rows("00000", "00000", "10001", "10001", "01010", "00100", "00100"),
        'z' => Rows("00000", "00000", "11111", "00010", "00100", "01000", "11111"),
        _ => null,
    };

    private static byte[] Rows(params string[] rows) =>
        rows.Select(row => Convert.ToByte(row, 2)).ToArray();
}
