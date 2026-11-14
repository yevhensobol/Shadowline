using System.Buffers.Binary;
using Shadowline.Core.Time;
using Shadowline.Data;

namespace Shadowline.Tests;

public class EphemerisReaderTests
{
    private static readonly TimeGrid Grid = new(new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero), TimeSpan.FromMinutes(30), 2);

    [Fact]
    public void Reads_sun_and_earth_rows()
    {
        var bytes = Encode(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12);

        var ephemeris = EphemerisReader.Read(bytes, Grid);

        Assert.Equal(1, ephemeris.Sun(0).X);
        Assert.Equal(6, ephemeris.Earth(0).Z);
        Assert.Equal(7, ephemeris.Sun(1).X);
        Assert.Equal(11, ephemeris.Earth(1).Y);
    }

    [Fact]
    public void Rejects_wrong_size()
    {
        var bytes = Encode(1, 2, 3, 4, 5, 6);

        Assert.Throws<DataFormatException>(() => EphemerisReader.Read(bytes, Grid));
    }

    [Fact]
    public void Rejects_non_finite_values()
    {
        var bytes = Encode(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, float.NaN);

        Assert.Throws<DataFormatException>(() => EphemerisReader.Read(bytes, Grid));
    }

    [Fact]
    public void Rejects_row_index_outside_grid()
    {
        var ephemeris = EphemerisReader.Read(Encode(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12), Grid);

        Assert.Throws<ArgumentOutOfRangeException>(() => ephemeris.Sun(2));
    }

    private static byte[] Encode(params float[] values)
    {
        var bytes = new byte[values.Length * sizeof(float)];
        for (var i = 0; i < values.Length; i++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(i * sizeof(float)), values[i]);
        }

        return bytes;
    }
}
