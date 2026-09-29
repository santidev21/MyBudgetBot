namespace MyBudget.Telegram.Charts;

/// <summary>One drawn value. The label is optional and drawn with the bitmap font.</summary>
internal sealed record ChartEntry(long Value, string? Label = null);

/// <summary>
/// Turns report numbers into a PNG.
/// <para>
/// Two shapes cover the product: horizontal bars for spending per category (the label is the
/// amount, at the end of each bar) and vertical bars for spending per day (the label is the
/// day number). Category names, month names and totals stay in the caption: the image is the
/// visual comparison, the message is the exact data.
/// </para>
/// </summary>
internal static class SpendingChartRenderer
{
    public const int Width = 760;

    /// <summary>More bars than this stops being readable on a phone.</summary>
    public const int MaxHorizontalBars = 10;

    internal const int Margin = 28;
    internal const int BarHeight = 26;
    internal const int RowHeight = 44;
    internal const int VerticalAreaHeight = 180;
    private const int VerticalLabelHeight = 18;
    private const int LabelGap = 16;

    private static readonly Rgb[] Palette =
    [
        new(46, 134, 171),
        new(217, 83, 79),
        new(89, 161, 79),
        new(226, 155, 61),
        new(128, 108, 168),
        new(78, 154, 158),
        new(192, 90, 138),
        new(138, 138, 78),
    ];

    public static byte[] HorizontalBars(IReadOnlyList<ChartEntry> entries) =>
        PngEncoder.Encode(DrawHorizontalBars(entries));

    public static byte[] VerticalBars(IReadOnlyList<ChartEntry> entries) =>
        PngEncoder.Encode(DrawVerticalBars(entries));

    internal static RgbCanvas DrawHorizontalBars(IReadOnlyList<ChartEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var bars = entries.Take(MaxHorizontalBars).ToList();
        var canvas = new RgbCanvas(Width, (Margin * 2) + (bars.Count * RowHeight));
        canvas.Fill(Rgb.White);

        if (bars.Count == 0)
        {
            return canvas;
        }

        const int scale = 2;
        var maximum = bars.Max(entry => entry.Value);

        for (var index = 0; index < bars.Count; index++)
        {
            var entry = bars[index];
            var label = entry.Label ?? string.Empty;
            var labelWidth = BitmapFont.Measure(label, scale);

            // The label owns the right edge; the bar grows in what is left of the row.
            var available = Math.Max(0, Width - (Margin * 2) - labelWidth - LabelGap);
            var barWidth = maximum > 0 ? (int)((decimal)entry.Value / maximum * available) : 0;
            if (entry.Value > 0 && barWidth < 3)
            {
                barWidth = 3;
            }

            var y = Margin + (index * RowHeight);
            canvas.Rectangle(Margin, y, barWidth, BarHeight, Palette[index % Palette.Length]);
            BitmapFont.Draw(
                canvas,
                label,
                Width - Margin - labelWidth,
                y + ((BarHeight - (BitmapFont.GlyphHeight * scale)) / 2),
                scale,
                Rgb.Ink);
        }

        return canvas;
    }

    internal static RgbCanvas DrawVerticalBars(IReadOnlyList<ChartEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var canvas = new RgbCanvas(Width, (Margin * 2) + VerticalAreaHeight + VerticalLabelHeight);
        canvas.Fill(Rgb.White);

        if (entries.Count == 0)
        {
            return canvas;
        }

        var maximum = entries.Max(entry => entry.Value);
        var slot = (Width - (Margin * 2)) / entries.Count;
        var barWidth = Math.Max(3, slot - 6);

        // With a full month the bars are ~22px wide and a smaller glyph keeps numbers legible.
        var scale = entries.Count <= 16 ? 2 : 1;

        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            var barHeight = maximum > 0
                ? (int)((decimal)entry.Value / maximum * VerticalAreaHeight)
                : 0;

            var x = Margin + (index * slot) + ((slot - barWidth) / 2);
            var y = Margin + VerticalAreaHeight - barHeight;

            if (entry.Value > 0)
            {
                canvas.Rectangle(x, y, barWidth, Math.Max(barHeight, 2), Palette[0]);
            }

            if (entry.Label is { Length: > 0 } label)
            {
                var labelWidth = BitmapFont.Measure(label, scale);
                BitmapFont.Draw(
                    canvas,
                    label,
                    x + ((barWidth - labelWidth) / 2),
                    Margin + VerticalAreaHeight + 6,
                    scale,
                    Rgb.Ink);
            }
        }

        return canvas;
    }
}
