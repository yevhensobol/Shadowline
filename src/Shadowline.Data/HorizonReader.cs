using System.Buffers.Binary;
using Shadowline.Core.Horizon;

namespace Shadowline.Data;

public static class HorizonReader
{
    public const int ExpectedBytes = HorizonProfile.SampleCount * sizeof(float);

    public static HorizonProfile Read(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != ExpectedBytes)
        {
            throw new DataFormatException($"Horizon size is {bytes.Length} bytes, expected {ExpectedBytes}");
        }

        var values = new float[HorizonProfile.SampleCount];
        for (var i = 0; i < values.Length; i++)
        {
            var value = BinaryPrimitives.ReadSingleLittleEndian(bytes.Slice(i * sizeof(float), sizeof(float)));
            if (!float.IsFinite(value) || value < -90f || value > 90f)
            {
                throw new DataFormatException($"Horizon contains an invalid angle at index {i}");
            }

            values[i] = value;
        }

        return new HorizonProfile(values);
    }
}
