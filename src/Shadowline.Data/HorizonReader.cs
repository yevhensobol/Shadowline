using System.Buffers.Binary;
using Shadowline.Core.Horizon;

namespace Shadowline.Data;

public static class HorizonReader
{
    public const int ExpectedBytes = HorizonProfile.SampleCount * sizeof(float);

    public static HorizonProfile Read(ReadOnlySpan<byte> bytes)
    {
        var values = new float[HorizonProfile.SampleCount];
        for (var i = 0; i < values.Length; i++)
        {
            var value = BinaryPrimitives.ReadSingleLittleEndian(bytes.Slice(i * sizeof(float), sizeof(float)));
          
            values[i] = value;
        }

        return new HorizonProfile(values);
    }
}
