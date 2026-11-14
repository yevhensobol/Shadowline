using System.Buffers.Binary;
using Shadowline.Core.Horizon;
using Shadowline.Data;

namespace Shadowline.Tests;

public class HorizonReaderTests
{
    private static byte[] Encode(Func<int, float> value)
    {
        var bytes = new byte[HorizonReader.ExpectedBytes];
        for (var i = 0; i < HorizonProfile.SampleCount; i++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(i * sizeof(float)), value(i));
        }

        return bytes;
    }

    [Fact]
    public void Reads_samples_in_order()
    {
        var profile = HorizonReader.Read(Encode(i => i == 2 ? 5f : 0f));

        Assert.True(Math.Abs(profile.ElevationAt(1.0) - 5.0) < 1e-6);
        Assert.True(Math.Abs(profile.ElevationAt(0.0)) < 1e-6);
    }

    [Fact]
    public void Wrong_size_is_rejected()
    {
        Assert.Throws<DataFormatException>(() => HorizonReader.Read(new byte[100]));
    }

    [Fact]
    public void Non_finite_values_are_rejected()
    {
        Assert.Throws<DataFormatException>(() => HorizonReader.Read(Encode(i => i == 7 ? float.NaN : 0f)));
    }

    [Fact]
    public void Angles_beyond_ninety_degrees_are_rejected()
    {
        Assert.Throws<DataFormatException>(() => HorizonReader.Read(Encode(i => i == 7 ? 120f : 0f)));
    }
}
