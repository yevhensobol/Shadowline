using Shadowline.Core.Ephemeris;
using Shadowline.Core.Geometry;
using Shadowline.Core.Horizon;
using Shadowline.Core.Power;
using Shadowline.Core.Time;

namespace Shadowline.Core.Thermal;

public sealed record ThermalEstimate(double NowK, double DayMaxK, double NightK);

public static class ThermalModel
{
    public const double Albedo = 0.12;

    public const double Emissivity = 0.95;

    public const double StefanBoltzmann = 5.670374419e-8;

    public const double EquatorNightK = 100.0;

    public const double MinNightK = 40.0;

    public static double NightK(double latitudeDeg) =>
        Math.Max(MinNightK, EquatorNightK * Math.Pow(Math.Max(0, Math.Cos(latitudeDeg * Math.PI / 180)), 0.25));

    public static double SurfaceK(SkySnapshot sky, HorizonProfile horizon, double latitudeDeg)
    {
        var fraction = PowerModel.VisibleFraction(sky.Sun.ElevationDeg, horizon.ElevationAt(sky.Sun.AzimuthDeg), sky.SunRadiusDeg);
        var night = NightK(latitudeDeg);
        if (fraction <= 0 || sky.Sun.ElevationDeg <= 0)
        {
            return night;
        }

        var au = PowerModel.AuKm / sky.Sun.RangeKm;
        var absorbed = PowerModel.SolarConstantW * au * au * (1 - Albedo) * fraction * Math.Sin(sky.Sun.ElevationDeg * Math.PI / 180);
        return Math.Max(night, Math.Pow(absorbed / (Emissivity * StefanBoltzmann), 0.25));
    }

    public static ThermalEstimate Evaluate(SiteFrame frame, BodyEphemeris ephemeris, HorizonProfile horizon, TimeGrid grid, int start, int length, double latitudeDeg)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(ephemeris);
        ArgumentNullException.ThrowIfNull(horizon);
        ArgumentNullException.ThrowIfNull(grid);
        var first = Math.Clamp(start, 0, grid.Count - 1);
        var last = Math.Clamp(start + length, first + 1, grid.Count);
        var now = SurfaceK(SkySnapshot.At(frame, ephemeris, first), horizon, latitudeDeg);
        var max = now;
        for (var i = first; i < last; i++)
        {
            max = Math.Max(max, SurfaceK(SkySnapshot.At(frame, ephemeris, i), horizon, latitudeDeg));
        }

        return new ThermalEstimate(now, max, NightK(latitudeDeg));
    }
}
