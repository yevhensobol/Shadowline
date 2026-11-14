using System.Buffers.Binary;
using Shadowline.Core.Ephemeris;
using Shadowline.Core.Time;

namespace Shadowline.Data;

public static class EphemerisReader
{
    public static BodyEphemeris Read(ReadOnlySpan<byte> bytes, TimeGrid grid)
    {
        ArgumentNullException.ThrowIfNull(grid);

        var expected = (long)grid.Count * BodyEphemeris.FloatsPerRow * sizeof(float);
        if (bytes.Length != expected)
        {
            throw new DataFormatException($"Ephemeris size is {bytes.Length} bytes, expected {expected}");
        }

        var values = new float[grid.Count * BodyEphemeris.FloatsPerRow];
        for (var i = 0; i < values.Length; i++)
        {
            var value = BinaryPrimitives.ReadSingleLittleEndian(bytes.Slice(i * sizeof(float), sizeof(float)));
            if (!float.IsFinite(value))
            {
                throw new DataFormatException($"Ephemeris contains a non-finite value at index {i}");
            }

            values[i] = value;
        }

        return new BodyEphemeris(grid, values);
    }
}
