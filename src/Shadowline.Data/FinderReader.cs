using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using Shadowline.Core.Finder;

namespace Shadowline.Data;

public static class FinderReader
{
    public const int SupportedVersion = 1;

    public static FinderGrid ReadGzip(byte[] compressed)
    {
        using var input = new MemoryStream(compressed);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        gzip.CopyTo(output);
        return Read(output.ToArray());
    }

    public static FinderGrid Read(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length < 40 || Encoding.ASCII.GetString(bytes, 0, 4) != "SLFD")
        {
            throw new DataFormatException("finder.bin: wrong magic");
        }

        var span = bytes.AsSpan();
        var version = BinaryPrimitives.ReadInt32LittleEndian(span[4..]);
        if (version != SupportedVersion)
        {
            throw new DataFormatException($"finder.bin: unsupported version {version}");
        }

        var size = BinaryPrimitives.ReadInt32LittleEndian(span[8..]);
        var cellM = BinaryPrimitives.ReadSingleLittleEndian(span[12..]);
        var steps = BinaryPrimitives.ReadInt32LittleEndian(span[20..]);
        var stepMinutes = BinaryPrimitives.ReadInt32LittleEndian(span[24..]);
        var start = DateTimeOffset.FromUnixTimeSeconds(BinaryPrimitives.ReadInt64LittleEndian(span[28..]));
        var domainLat = BinaryPrimitives.ReadSingleLittleEndian(span[36..]);
        var cells = size * size;
        var perCell = (steps + 7) / 8;
        var offset = 40;
        var expected = offset + 6 * cells + 2 * cells * perCell;
        if (size <= 0 || steps <= 0 || bytes.Length != expected)
        {
            throw new DataFormatException($"finder.bin: expected {expected} bytes, got {bytes.Length}");
        }

        byte[] Take(int length)
        {
            var part = bytes.AsSpan(offset, length).ToArray();
            offset += length;
            return part;
        }

        var domain = Take(cells);
        var slopeMedian = Take(cells);
        var slopeHigh = Take(cells);
        var water = Take(cells);
        var sunYear = Take(cells);
        var earthYear = Take(cells);
        var sun = Take(cells * perCell);
        var earth = Take(cells * perCell);
        return new FinderGrid(size, cellM, steps, stepMinutes, start, domainLat, domain, slopeMedian, slopeHigh, water, sunYear, earthYear, sun, earth);
    }
}
