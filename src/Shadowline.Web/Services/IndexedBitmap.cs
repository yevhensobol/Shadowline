namespace Shadowline.Web.Services;

public static class IndexedBitmap
{
    public static string DataUrl(byte[] pixels, int width, int height, IReadOnlyList<(byte R, byte G, byte B)> palette)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        ArgumentNullException.ThrowIfNull(palette);
        var stride = (width + 3) / 4 * 4;
        const int headerSize = 14 + 40;
        var paletteSize = 256 * 4;
        var dataOffset = headerSize + paletteSize;
        var fileSize = dataOffset + stride * height;
        var bytes = new byte[fileSize];
        bytes[0] = (byte)'B';
        bytes[1] = (byte)'M';
        BitConverter.TryWriteBytes(bytes.AsSpan(2), fileSize);
        BitConverter.TryWriteBytes(bytes.AsSpan(10), dataOffset);
        BitConverter.TryWriteBytes(bytes.AsSpan(14), 40);
        BitConverter.TryWriteBytes(bytes.AsSpan(18), width);
        BitConverter.TryWriteBytes(bytes.AsSpan(22), height);
        BitConverter.TryWriteBytes(bytes.AsSpan(26), (short)1);
        BitConverter.TryWriteBytes(bytes.AsSpan(28), (short)8);
        BitConverter.TryWriteBytes(bytes.AsSpan(34), stride * height);
        BitConverter.TryWriteBytes(bytes.AsSpan(46), 256);
        for (var i = 0; i < palette.Count && i < 256; i++)
        {
            var at = headerSize + i * 4;
            bytes[at] = palette[i].B;
            bytes[at + 1] = palette[i].G;
            bytes[at + 2] = palette[i].R;
        }

        for (var row = 0; row < height; row++)
        {
            pixels.AsSpan(row * width, width).CopyTo(bytes.AsSpan(dataOffset + (height - 1 - row) * stride));
        }

        return "data:image/bmp;base64," + Convert.ToBase64String(bytes);
    }
}
