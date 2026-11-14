namespace Shadowline.Core.Terrain;

public sealed class PolarDem
{
    private readonly short[] _units;

    public PolarDem(int size, double halfExtentM, double unitM, short[] units)
    {
        ArgumentNullException.ThrowIfNull(units);
        if (size < 2 || units.Length != size * size)
        {
            throw new ArgumentException("Polar DEM size does not match its data", nameof(units));
        }

        Size = size;
        HalfExtentM = halfExtentM;
        UnitM = unitM;
        SpacingM = 2 * halfExtentM / size;
        _units = units;
    }

    public int Size { get; }

    public double HalfExtentM { get; }

    public double UnitM { get; }

    public double SpacingM { get; }

    public static PolarDem? Parse(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 12)
        {
            return null;
        }

        var size = BitConverter.ToInt32(bytes[..4]);
        var half = BitConverter.ToSingle(bytes.Slice(4, 4));
        var unit = BitConverter.ToSingle(bytes.Slice(8, 4));
        if (size < 2 || size > 8192 || half <= 0 || unit <= 0 || bytes.Length != 12 + size * size * 2)
        {
            return null;
        }

        var units = new short[size * size];
        for (var i = 0; i < units.Length; i++)
        {
            units[i] = BitConverter.ToInt16(bytes.Slice(12 + i * 2, 2));
        }

        return new PolarDem(size, half, unit, units);
    }

    public static (double X, double Y) Project(double latDeg, double lonDeg)
    {
        var rho = 2 * LocalTerrain.MoonRadiusM * Math.Tan((latDeg * Math.PI / 180.0 + Math.PI / 2) / 2);
        var lon = lonDeg * Math.PI / 180.0;
        return (rho * Math.Sin(lon), rho * Math.Cos(lon));
    }

    public static (double Lat, double Lon) Unproject(double x, double y)
    {
        var rho = Math.Sqrt(x * x + y * y);
        var lat = 2 * Math.Atan(rho / (2 * LocalTerrain.MoonRadiusM)) - Math.PI / 2;
        return (lat * 180.0 / Math.PI, rho == 0 ? 0 : Math.Atan2(x, y) * 180.0 / Math.PI);
    }

    public static PolarDem? ParseDelta(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 12)
        {
            return null;
        }

        var size = BitConverter.ToInt32(bytes[..4]);
        var half = BitConverter.ToSingle(bytes.Slice(4, 4));
        var unit = BitConverter.ToSingle(bytes.Slice(8, 4));
        if (size < 2 || size > 8192 || half <= 0 || unit <= 0 || bytes.Length != 12 + size * size * 2)
        {
            return null;
        }

        return new PolarDem(size, half, unit, PoleGrid.Decode(bytes[12..], size, size));
    }

    public double? Sample(double latDeg, double lonDeg)
    {
        if (latDeg > -60)
        {
            return null;
        }

        var (x, y) = Project(latDeg, lonDeg);
        return SampleXY(x, y);
    }

    public double? SampleXY(double x, double y)
    {
        var col = (x + HalfExtentM) / SpacingM - 0.5;
        var row = (HalfExtentM - y) / SpacingM - 0.5;
        if (col < 0 || row < 0 || col > Size - 1 || row > Size - 1)
        {
            return null;
        }

        var c0 = Math.Min((int)Math.Floor(col), Size - 2);
        var r0 = Math.Min((int)Math.Floor(row), Size - 2);
        var fc = col - c0;
        var fr = row - r0;
        double At(int r, int c) => _units[r * Size + c] * UnitM;
        return (At(r0, c0) * (1 - fc) + At(r0, c0 + 1) * fc) * (1 - fr) + (At(r0 + 1, c0) * (1 - fc) + At(r0 + 1, c0 + 1) * fc) * fr;
    }

    public Func<double, double, double> Over(Func<double, double, double> fallback)
    {
        ArgumentNullException.ThrowIfNull(fallback);
        return (lat, lon) => Sample(lat, lon) ?? fallback(lat, lon);
    }
}
