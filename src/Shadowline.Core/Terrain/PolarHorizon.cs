using Shadowline.Core.Horizon;

namespace Shadowline.Core.Terrain;

public static class PolarHorizon
{
    public const double NearRangeM = 30000;
    public const double MidRangeM = 150000;
    public const double FarRangeM = 300000;
    public const double FarStepM = 1000;

    public static IReadOnlyList<double> Distances(double nearStepM, double midStepM, double nearRangeM = NearRangeM)
    {
        var distances = new List<double>();
        for (var d = nearStepM; d <= nearRangeM + 1e-6; d += nearStepM)
        {
            distances.Add(d);
        }

        for (var d = nearRangeM + midStepM; d <= MidRangeM + 1e-6; d += midStepM)
        {
            distances.Add(d);
        }

        for (var d = MidRangeM + FarStepM; d <= FarRangeM + 1e-6; d += FarStepM)
        {
            distances.Add(d);
        }

        return distances;
    }

    public static HorizonProfile Compute(
        double latDeg,
        double lonDeg,
        PoleWindow near,
        PolarDem coarse,
        Func<double, double, double> global,
        double observerM = 2,
        double nearRangeM = NearRangeM)
    {
        ArgumentNullException.ThrowIfNull(near);
        ArgumentNullException.ThrowIfNull(coarse);
        ArgumentNullException.ThrowIfNull(global);
        var (x0, y0) = PolarDem.Project(latDeg, lonDeg);
        var baseHeight = (near.Sample(x0, y0) ?? coarse.SampleXY(x0, y0) ?? global(latDeg, lonDeg)) + observerM;
        var rho = Math.Sqrt(x0 * x0 + y0 * y0);
        var (nx, ny) = rho > 1 ? (x0 / rho, y0 / rho) : (0.0, 1.0);
        var (ex, ey) = (ny, -nx);
        var distances = Distances(near.SpacingM, coarse.SpacingM, nearRangeM);
        var elevations = new float[HorizonProfile.SampleCount];
        for (var i = 0; i < HorizonProfile.SampleCount; i++)
        {
            var azimuth = i * HorizonProfile.StepDeg * Math.PI / 180.0;
            var dx = Math.Cos(azimuth) * nx + Math.Sin(azimuth) * ex;
            var dy = Math.Cos(azimuth) * ny + Math.Sin(azimuth) * ey;
            var best = double.NegativeInfinity;
            foreach (var distance in distances)
            {
                var x = x0 + dx * distance;
                var y = y0 + dy * distance;
                double? height = distance <= nearRangeM ? near.Sample(x, y) : null;
                height ??= coarse.SampleXY(x, y);
                if (height is null)
                {
                    var (lat, lon) = PolarDem.Unproject(x, y);
                    height = global(lat, lon);
                }

                var angle = Math.Atan2(height.Value - baseHeight - distance * distance / (2.0 * LocalTerrain.MoonRadiusM), distance);
                if (angle > best)
                {
                    best = angle;
                }
            }

            elevations[i] = (float)Math.Clamp(best * 180.0 / Math.PI, -90.0, 90.0);
        }

        return new HorizonProfile(elevations);
    }
}
