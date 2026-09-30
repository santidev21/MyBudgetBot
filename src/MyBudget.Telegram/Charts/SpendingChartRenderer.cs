namespace MyBudget.Telegram.Charts;

/// <summary>
/// One drawn value. <c>Name</c> sits at the left of the row's header line, <c>Label</c> at its
/// right edge, and the bar fills the row underneath.
/// </summary>
internal sealed record ChartEntry(long Value, string? Label = null, string? Name = null);

/// <summary>
/// Turns report numbers into a PNG.
/// <para>
/// Two shapes cover the product: horizontal bars for spending per category and vertical bars for
/// spending per day. The horizontal bar is measured against the <c>total</c> it is given, never
/// against the longest bar, so a row's length is literally that category's share of the month and
/// two rows are comparable; the exact numbers travel in the row itself and in the caption, where
/// the phone's font draws the icon and the accents.
/// </para>
/// </summary>
internal static class SpendingChartRenderer
{
    public const int Width = 760;

    /// <summary>More bars than this stops being readable on a phone.</summary>
    public const int MaxHorizontalBars = 10;

    internal const int Margin = 28;
    internal const int BarHeight = 26;

    /// <summary>Text on a horizontal row is drawn at twice the base glyph size.</summary>
    internal const int LabelScale = 2;

    internal const int NameLineHeight = BitmapFont.GlyphHeight * LabelScale;
    internal const int NameGap = 4;

    /// <summary>Where the bar starts inside its row: under the name and the numbers.</summary>
    internal const int BarOffsetInRow = NameLineHeight + NameGap;

    internal const int RowGap = 16;
    internal const int RowHeight = BarOffsetInRow + BarHeight + RowGap;
    internal const int VerticalAreaHeight = 180;
    private const int VerticalLabelHeight = 18;
    private const int LabelGap = 16;
    private const int MinimumBarWidth = 3;

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

    /// <param name="total">
    /// What the bars are a share of: 100 % fills a row, so a 3 % category is 3 % of the row.
    /// </param>
    public static byte[] HorizontalBars(IReadOnlyList<ChartEntry> entries, long total) =>
        PngEncoder.Encode(DrawHorizontalBars(entries, total));

    public static byte[] VerticalBars(IReadOnlyList<ChartEntry> entries) =>
        PngEncoder.Encode(DrawVerticalBars(entries));

    internal static RgbCanvas DrawHorizontalBars(IReadOnlyList<ChartEntry> entries, long total)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var bars = entries.Take(MaxHorizontalBars).ToList();
        var canvas = new RgbCanvas(Width, (Margin * 2) + (bars.Count * RowHeight));
        canvas.Fill(Rgb.White);

        if (bars.Count == 0)
        {
            return canvas;
        }

        // One width for every row: a bar is a share of the whole row, so rows stay comparable
        // no matter how long their labels are.
        var available = Width - (Margin * 2);

        for (var index = 0; index < bars.Count; index++)
        {
            var entry = bars[index];
            var rowTop = Margin + (index * RowHeight);

            var numbers = entry.Label ?? string.Empty;
            var numbersWidth = BitmapFont.Measure(numbers, LabelScale);
            var name = BitmapFont.Truncate(
                entry.Name ?? string.Empty, available - numbersWidth - LabelGap, LabelScale);

            // The name and the exact numbers share the header line, name at the left edge,
            // numbers at the right edge.
            BitmapFont.Draw(canvas, name, Margin, rowTop, LabelScale, Rgb.Ink);
            BitmapFont.Draw(
                canvas, numbers, Width - Margin - numbersWidth, rowTop, LabelScale, Rgb.Ink);

            var barWidth = total > 0 ? (int)((decimal)entry.Value / total * available) : 0;
            barWidth = Math.Clamp(barWidth, 0, available);

            if (total > 0 && entry.Value > 0 && barWidth < MinimumBarWidth)
            {
                barWidth = MinimumBarWidth;
            }

            canvas.Rectangle(
                Margin,
                rowTop + BarOffsetInRow,
                barWidth,
                BarHeight,
                Palette[index % Palette.Length]);
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
