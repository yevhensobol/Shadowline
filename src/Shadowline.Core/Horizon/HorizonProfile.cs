namespace Shadowline.Core.Horizon;

public sealed class HorizonProfile
{
    public const int SampleCount = 720;
    public const double StepDeg = 360.0 / SampleCount;

    private readonly float[] _elevations;

    public HorizonProfile(float[] elevations)
    {
        ArgumentNullException.ThrowIfNull(elevations);
        if (elevations.Length != SampleCount)
        {
            throw new ArgumentException($"Expected {SampleCount} samples, got {elevations.Length}", nameof(elevations));
        }

        _elevations = elevations;
    }

    public static HorizonProfile Flat { get; } = new(new float[SampleCount]);

    public double ElevationAt(double azimuthDeg)
    {
        var wrapped = azimuthDeg % 360.0;
        if (wrapped < 0)
        {
            wrapped += 360.0;
        }

        var position = wrapped / StepDeg;
        var lower = (int)Math.Floor(position);
        var fraction = position - lower;
        var a = _elevations[lower % SampleCount];
        var b = _elevations[(lower + 1) % SampleCount];
        return a + (b - a) * fraction;
    }
}
