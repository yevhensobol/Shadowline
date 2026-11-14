namespace Shadowline.Core.Thermal;

public sealed record DivinerCell(double AnnualAverage2cmK, double AnnualMaxK, double? IceDepthM);

public sealed class DivinerPolarMap
{
    private const double MoonRadiusM = 1737400.0;

    private readonly byte[] _data;

    private DivinerPolarMap(int size, double halfExtentM, double unitK, byte[] data)
    {
        Size = size;
        HalfExtentM = halfExtentM;
        UnitK = unitK;
        _data = data;
    }

    public int Size { get; }

    public double HalfExtentM { get; }

    public double UnitK { get; }

    public static DivinerPolarMap Parse(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length < 12)
        {
            throw new ArgumentException("Diviner map too short");
        }

        var size = BitConverter.ToInt32(bytes, 0);
        var half = BitConverter.ToSingle(bytes, 4);
        var unit = BitConverter.ToSingle(bytes, 8);
        if (size < 2 || size > 8192 || half <= 0 || unit <= 0 || bytes.Length != 12 + 3 * size * size)
        {
            throw new ArgumentException("Bad Diviner map header");
        }

        return new DivinerPolarMap(size, half, unit, bytes[12..]);
    }

    public DivinerCell? At(double latitudeDeg, double longitudeEastDeg)
    {
        if (latitudeDeg > -60)
        {
            return null;
        }

        var rho = 2 * MoonRadiusM * Math.Tan((90 + latitudeDeg) * Math.PI / 360);
        var x = rho * Math.Sin(longitudeEastDeg * Math.PI / 180);
        var y = rho * Math.Cos(longitudeEastDeg * Math.PI / 180);
        var col = (int)Math.Floor((x + HalfExtentM) / (2 * HalfExtentM) * Size);
        var row = (int)Math.Floor((HalfExtentM - y) / (2 * HalfExtentM) * Size);
        if (col < 0 || row < 0 || col >= Size || row >= Size)
        {
            return null;
        }

        var i = row * Size + col;
        var plane = Size * Size;
        var avg = _data[i];
        var max = _data[plane + i];
        var ice = _data[2 * plane + i];
        if (avg == 0 || max == 0)
        {
            return null;
        }

        return new DivinerCell(avg * UnitK, max * UnitK, ice == 255 ? null : ice / 50.0);
    }
}
