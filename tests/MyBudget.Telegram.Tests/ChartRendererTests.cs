using System.IO.Compression;
using FluentAssertions;
using MyBudget.Telegram.Charts;

namespace MyBudget.Telegram.Tests;

/// <summary>
/// The dependency-free chart pipeline: raster, bitmap font and PNG encoder. The exact pixels
/// are not pinned, but the format, the compression and the proportionality are.
/// </summary>
public sealed class ChartRendererTests
{
    private static readonly byte[] PngSignature = [137, 80, 78, 71, 13, 10, 26, 10];

    [Fact]
    public void A_chart_is_a_png_with_the_canvas_size_in_its_header()
    {
        var png = SpendingChartRenderer.HorizontalBars([new ChartEntry(100, "100")], total: 100);

        png.Should().StartWith(PngSignature);
        ReadInt(png, 16).Should().Be(SpendingChartRenderer.Width);
        ReadInt(png, 20).Should().Be(
            (SpendingChartRenderer.Margin * 2) + SpendingChartRenderer.RowHeight);
    }

    [Fact]
    public void The_image_data_decompresses_to_the_expected_scanlines()
    {
        // A zlib stream that a decoder accepts is the part of "valid PNG" this can prove
        // without shipping an image library of our own.
        var png = SpendingChartRenderer.VerticalBars([new ChartEntry(10, "1")]);
        var width = ReadInt(png, 16);
        var height = ReadInt(png, 20);

        var raw = Inflate(ImageData(png));

        raw.Length.Should().Be(height * (1 + (width * 3)));
        raw[0].Should().Be(0, "every scanline starts with the no-filter byte");
    }

    [Fact]
    public void A_bar_is_its_share_of_the_total_so_the_number_and_the_length_agree()
    {
        // Three of a hundred must be 3 % of the row: the percentage the user reads is the
        // length the user sees, not a fraction of the longest bar.
        var canvas = SpendingChartRenderer.DrawHorizontalBars([new ChartEntry(3)], total: 100);

        var available = SpendingChartRenderer.Width - (SpendingChartRenderer.Margin * 2);
        var bar = ColouredPixelsInRow(canvas, BarRow(index: 0));

        ((double)bar).Should().BeApproximately(available * 0.03, 2.0);
    }

    [Fact]
    public void Two_bars_keep_their_ratio_against_the_same_total()
    {
        var canvas = SpendingChartRenderer.DrawHorizontalBars(
            [new ChartEntry(100), new ChartEntry(50)], total: 200);

        var longest = ColouredPixelsInRow(canvas, BarRow(index: 0));
        var half = ColouredPixelsInRow(canvas, BarRow(index: 1));

        half.Should().BeGreaterThan(0);
        ((double)half).Should().BeApproximately(longest / 2.0, 1.5);
    }

    [Fact]
    public void The_name_and_the_numbers_sit_above_the_bar()
    {
        var canvas = SpendingChartRenderer.DrawHorizontalBars(
            [new ChartEntry(50, "50 %", "1 Comida")], total: 100);

        ColouredPixelsInRow(canvas, SpendingChartRenderer.Margin + 1)
            .Should().BeGreaterThan(0, "the header line carries the name and the exact numbers");

        ColouredPixelsInRow(canvas, SpendingChartRenderer.Margin + SpendingChartRenderer.BarOffsetInRow + 1)
            .Should().BeGreaterThan(0, "the bar fills the row under them");
    }

    [Fact]
    public void More_bars_than_the_cap_are_not_drawn()
    {
        var entries = Enumerable.Range(0, SpendingChartRenderer.MaxHorizontalBars + 4)
            .Select(index => new ChartEntry(index + 1))
            .ToList();

        var png = SpendingChartRenderer.HorizontalBars(entries, total: 1000);

        ReadInt(png, 20).Should().Be(
            (SpendingChartRenderer.Margin * 2)
            + (SpendingChartRenderer.MaxHorizontalBars * SpendingChartRenderer.RowHeight));
    }

    [Fact]
    public void Vertical_bars_grow_with_their_values()
    {
        var canvas = SpendingChartRenderer.DrawVerticalBars(
            [new ChartEntry(10, "1"), new ChartEntry(20, "2")]);

        var left = ColouredPixelsInColumns(canvas, 0, canvas.Width / 2);
        var right = ColouredPixelsInColumns(canvas, canvas.Width / 2, canvas.Width);

        // Both halves carry a label; only the right half carries a full-height bar.
        right.Should().BeGreaterThan(left);
    }

    [Fact]
    public void The_font_measures_advances_and_draws_digits()
    {
        BitmapFont.Measure("90", scale: 2).Should().Be(22);
        BitmapFont.Measure(string.Empty, scale: 2).Should().Be(0);

        var canvas = new RgbCanvas(32, 16);
        canvas.Fill(Rgb.White);

        BitmapFont.Draw(canvas, "7", 0, 0, 2, Rgb.Ink);

        ColouredPixels(canvas).Should().BeGreaterThan(0);
    }

    [Fact]
    public void A_category_name_is_drawn_letter_by_letter()
    {
        var canvas = new RgbCanvas(160, 16);
        canvas.Fill(Rgb.White);

        BitmapFont.Draw(canvas, "Mercado", 0, 0, 2, Rgb.Ink);

        ColouredPixels(canvas).Should().BeGreaterThan(0);
    }

    [Fact]
    public void An_accent_is_folded_away_instead_of_dropping_the_word()
    {
        BitmapFont.Measure("holá", scale: 2).Should().Be(BitmapFont.Measure("hola", scale: 2));

        var canvas = new RgbCanvas(64, 16);
        canvas.Fill(Rgb.White);

        BitmapFont.Draw(canvas, "holá", 0, 0, 2, Rgb.Ink);

        ColouredPixels(canvas).Should().BeGreaterThan(0);
    }

    [Fact]
    public void Characters_the_font_does_not_know_draw_nothing()
    {
        // An icon or another script leaves a blank; the exact name stays in the caption.
        var canvas = new RgbCanvas(64, 16);
        canvas.Fill(Rgb.White);

        BitmapFont.Draw(canvas, "日本語", 0, 0, 2, Rgb.Ink);

        ColouredPixels(canvas).Should().Be(0);
    }

    [Fact]
    public void A_name_that_does_not_fit_is_cut_with_two_dots()
    {
        var room = BitmapFont.Measure("abc", scale: 2);

        var cut = BitmapFont.Truncate("una categoría muy larga", room, scale: 2);

        BitmapFont.Measure(cut, scale: 2).Should().BeLessOrEqualTo(room);
        cut.Should().EndWith("..");
    }

    [Fact]
    public void The_font_draws_the_money_symbols_used_on_chart_labels()
    {
        var canvas = new RgbCanvas(64, 16);
        canvas.Fill(Rgb.White);

        BitmapFont.Draw(canvas, "$1/2%", 0, 0, 2, Rgb.Ink);

        ColouredPixels(canvas).Should().BeGreaterThan(0);
    }

    private static int BarRow(int index) =>
        SpendingChartRenderer.Margin
        + (index * SpendingChartRenderer.RowHeight)
        + SpendingChartRenderer.BarOffsetInRow
        + (SpendingChartRenderer.BarHeight / 2);

    private static int ColouredPixelsInRow(RgbCanvas canvas, int y)
    {
        var count = 0;
        for (var x = 0; x < canvas.Width; x++)
        {
            if (canvas.PixelAt(x, y) != Rgb.White)
            {
                count++;
            }
        }

        return count;
    }

    private static int ColouredPixelsInColumns(RgbCanvas canvas, int fromX, int toX)
    {
        var count = 0;
        for (var x = fromX; x < toX; x++)
        {
            for (var y = 0; y < canvas.Height; y++)
            {
                if (canvas.PixelAt(x, y) != Rgb.White)
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static int ColouredPixels(RgbCanvas canvas) =>
        ColouredPixelsInColumns(canvas, 0, canvas.Width);

    private static int ReadInt(byte[] png, int offset) =>
        (png[offset] << 24) | (png[offset + 1] << 16) | (png[offset + 2] << 8) | png[offset + 3];

    private static byte[] ImageData(byte[] png)
    {
        using var data = new MemoryStream();
        var offset = 8;

        while (offset + 8 <= png.Length)
        {
            var length = ReadInt(png, offset);
            var type = System.Text.Encoding.ASCII.GetString(png, offset + 4, 4);

            if (type == "IDAT")
            {
                data.Write(png, offset + 8, length);
            }

            offset += 12 + length;
        }

        return data.ToArray();
    }

    private static byte[] Inflate(byte[] compressed)
    {
        using var source = new MemoryStream(compressed);
        using var zlib = new ZLibStream(source, CompressionMode.Decompress);
        using var raw = new MemoryStream();
        zlib.CopyTo(raw);
        return raw.ToArray();
    }
}
