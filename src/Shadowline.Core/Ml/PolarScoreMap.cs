namespace Shadowline.Core.Ml;

public sealed class PolarScoreMap
{
    private const double MoonRadiusM = 1737400.0;

    private readonly byte[] _values;

    private PolarScoreMap(int size, double halfExtentM, double domainLatitudeDeg, byte[] values)
    {
        Size = size;
        HalfExtentM = halfExtentM;
        DomainLatitudeDeg = domainLatitudeDeg;
        _values = values;
    }

    public int Size { get; }

    public double HalfExtentM { get; }

    public double DomainLatitudeDeg { get; }

    public static PolarScoreMap Parse(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length < 12)
        {
            throw new ArgumentException("Score map too short");
        }

        var size = BitConverter.ToInt32(bytes, 0);
        var half = BitConverter.ToSingle(bytes, 4);
        var domain = BitConverter.ToSingle(bytes, 8);
        if (size < 2 || size > 8192 || half <= 0 || bytes.Length != 12 + size * size)
        {
            throw new ArgumentException("Bad score map header");
        }

        return new PolarScoreMap(size, half, domain, bytes[12..]);
    }

    public double? Percentile(double latitudeDeg, double longitudeEastDeg)
    {
        if (latitudeDeg > DomainLatitudeDeg)
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

        var value = _values[row * Size + col];
        return value == 0 ? null : value / 255.0;
    }
}
