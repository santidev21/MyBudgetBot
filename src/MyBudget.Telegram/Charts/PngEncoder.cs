using System.Buffers.Binary;
using System.IO.Compression;

namespace MyBudget.Telegram.Charts;

/// <summary>
/// Minimal PNG writer: 8-bit truecolour, no interlacing, one <c>IDAT</c> chunk.
/// <para>
/// Only what the charts need. The zlib stream comes from the BCL, and the CRC is the standard
/// PNG CRC-32, so the output is a normal PNG any client can open.
/// </para>
/// </summary>
internal static class PngEncoder
{
    private static readonly byte[] Signature = [137, 80, 78, 71, 13, 10, 26, 10];

    public static byte[] Encode(RgbCanvas canvas)
    {
        ArgumentNullException.ThrowIfNull(canvas);

        using var output = new MemoryStream();
        output.Write(Signature);

        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0, 4), canvas.Width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4, 4), canvas.Height);
        header[8] = 8;  // bit depth
        header[9] = 2;  // colour type: truecolour RGB
        header[10] = 0; // compression method: deflate
        header[11] = 0; // filter method: adaptive
        header[12] = 0; // interlace: none
        WriteChunk(output, "IHDR", header);

        WriteChunk(output, "IDAT", Compress(canvas));
        WriteChunk(output, "IEND", []);

        return output.ToArray();
    }

    private static byte[] Compress(RgbCanvas canvas)
    {
        using var raw = new MemoryStream();

        // One filter byte per scanline; 0 means "no filtering", which is fine for flat charts.
        for (var y = 0; y < canvas.Height; y++)
        {
            raw.WriteByte(0);
            raw.Write(canvas.RowSpan(y));
        }

        raw.Position = 0;
        using var compressed = new MemoryStream();

        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            raw.CopyTo(zlib);
        }

        return compressed.ToArray();
    }

    private static void WriteChunk(Stream output, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        output.Write(length);

        Span<byte> typeBytes = stackalloc byte[4];
        for (var index = 0; index < 4; index++)
        {
            typeBytes[index] = (byte)type[index];
        }

        output.Write(typeBytes);
        output.Write(data);

        Span<byte> crc = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crc, Crc32.Compute(typeBytes, data));
        output.Write(crc);
    }
}

/// <summary>The CRC-32 PNG uses: reflected polynomial <c>0xEDB88320</c>.</summary>
internal static class Crc32
{
    private static readonly uint[] Table = BuildTable();

    public static uint Compute(ReadOnlySpan<byte> first, ReadOnlySpan<byte> second)
    {
        var crc = 0xFFFFFFFFu;
        crc = Update(crc, first);
        crc = Update(crc, second);
        return crc ^ 0xFFFFFFFFu;
    }

    private static uint Update(uint crc, ReadOnlySpan<byte> data)
    {
        foreach (var value in data)
        {
            crc = Table[(crc ^ value) & 0xFF] ^ (crc >> 8);
        }

        return crc;
    }

    private static uint[] BuildTable()
    {
        var table = new uint[256];

        for (var index = 0u; index < table.Length; index++)
        {
            var value = index;
            for (var bit = 0; bit < 8; bit++)
            {
                value = (value & 1) != 0 ? 0xEDB88320u ^ (value >> 1) : value >> 1;
            }

            table[index] = value;
        }

        return table;
    }
}
