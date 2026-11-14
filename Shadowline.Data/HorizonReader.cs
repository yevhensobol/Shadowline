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
        var values = new float[grid.Count * BodyEphemeris.FloatsPerRow];

        for (var i = 0; i < values.Length; i++)
        {
            var value = BinaryPrimitives.ReadSingleLittleEndian(bytes.Slice(i * sizeof(float), sizeof(float)));
            values[i] = value;
        }

        return new BodyEphemeris(grid, values);
    }
}
