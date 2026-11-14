namespace Shadowline.Core.Finder;

public sealed class FinderGrid
{
    public const double MoonRadiusM = 1737400.0;

    private readonly byte[] _sun;
    private readonly byte[] _earth;

    public FinderGrid(int size, double cellM, int steps, int stepMinutes, DateTimeOffset startUtc, double domainLatitudeDeg,
        byte[] domain, byte[] slopeMedian, byte[] slopeHigh, byte[] waterKm, byte[] sunYear, byte[] earthYear, byte[] sun, byte[] earth)
    {
        var cells = size * size;
        BytesPerCell = (steps + 7) / 8;
        if (domain.Length != cells || slopeMedian.Length != cells || slopeHigh.Length != cells || waterKm.Length != cells
            || sunYear.Length != cells || earthYear.Length != cells || sun.Length != cells * BytesPerCell || earth.Length != cells * BytesPerCell)
        {
            throw new ArgumentException("Plane sizes do not match the grid");
        }

        Size = size;
        CellM = cellM;
        HalfExtentM = size * cellM / 2;
        Steps = steps;
        Step = TimeSpan.FromMinutes(stepMinutes);
        StartUtc = startUtc;
        DomainLatitudeDeg = domainLatitudeDeg;
        Domain = domain;
        SlopeMedian = slopeMedian;
        SlopeHigh = slopeHigh;
        WaterKm = waterKm;
        SunYear = sunYear;
        EarthYear = earthYear;
        _sun = sun;
        _earth = earth;
    }

    public int Size { get; }

    public int Count => Size * Size;

    public double CellM { get; }

    public double CellKm => CellM / 1000.0;

    public double HalfExtentM { get; }

    public int Steps { get; }

    public TimeSpan Step { get; }

    public int StepsPerDay => (int)Math.Round(TimeSpan.FromDays(1) / Step);

    public DateTimeOffset StartUtc { get; }

    public DateTimeOffset EndUtc => StartUtc + Step * Steps;

    public double DomainLatitudeDeg { get; }

    public int BytesPerCell { get; }

    public byte[] Domain { get; }

    public byte[] SlopeMedian { get; }

    public byte[] SlopeHigh { get; }

    public byte[] WaterKm { get; }

    public byte[] SunYear { get; }

    public byte[] EarthYear { get; }

    public bool InDomain(int cell) => Domain[cell] != 0;

    public double SlopeMedianDeg(int cell) => SlopeMedian[cell] / 4.0;

    public double SlopeHighDeg(int cell) => SlopeHigh[cell] / 4.0;

    public double WaterDistanceKm(int cell) => WaterKm[cell];

    public bool Sun(int cell, int step) => Bit(_sun, cell, step);

    public bool Earth(int cell, int step) => Bit(_earth, cell, step);

    private bool Bit(byte[] bits, int cell, int step) =>
        (bits[cell * BytesPerCell + (step >> 3)] & (1 << (step & 7))) != 0;

    public (double X, double Y) CellCenter(int cell)
    {
        var row = cell / Size;
        var col = cell % Size;
        return (-HalfExtentM + (col + 0.5) * CellM, HalfExtentM - (row + 0.5) * CellM);
    }

    public (double LatitudeDeg, double LongitudeEastDeg) LatLon(int cell)
    {
        var (x, y) = CellCenter(cell);
        return ToLatLon(x, y);
    }

    public static (double LatitudeDeg, double LongitudeEastDeg) ToLatLon(double x, double y)
    {
        var rho = Math.Sqrt(x * x + y * y);
        var latitude = -90.0 + 2.0 * Math.Atan(rho / (2.0 * MoonRadiusM)) * 180.0 / Math.PI;
        var longitude = Math.Atan2(x, y) * 180.0 / Math.PI;
        return (latitude, longitude);
    }

    public static (double X, double Y) ToXY(double latitudeDeg, double longitudeEastDeg)
    {
        var rho = 2.0 * MoonRadiusM * Math.Tan((90.0 + latitudeDeg) * Math.PI / 360.0);
        var longitude = longitudeEastDeg * Math.PI / 180.0;
        return (rho * Math.Sin(longitude), rho * Math.Cos(longitude));
    }

    public int? CellAt(double latitudeDeg, double longitudeEastDeg)
    {
        var (x, y) = ToXY(latitudeDeg, longitudeEastDeg);
        var col = (int)Math.Floor((x + HalfExtentM) / CellM);
        var row = (int)Math.Floor((HalfExtentM - y) / CellM);
        if (col < 0 || row < 0 || col >= Size || row >= Size)
        {
            return null;
        }

        return row * Size + col;
    }

    public int StepAt(DateTimeOffset moment) => (int)Math.Floor((moment - StartUtc) / Step);

    public DateTimeOffset At(int step) => StartUtc + Step * step;

    public double DistanceKm(int a, int b)
    {
        var (ax, ay) = CellCenter(a);
        var (bx, by) = CellCenter(b);
        return Math.Sqrt((ax - bx) * (ax - bx) + (ay - by) * (ay - by)) / 1000.0;
    }
}
