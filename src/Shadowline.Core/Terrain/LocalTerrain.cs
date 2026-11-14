using Shadowline.Core.Horizon;

namespace Shadowline.Core.Terrain;

public static class LocalTerrain
{
    public const double MoonRadiusM = 1737400.0;
    public const float UnitM = 0.5f;

    public static (double Lat, double Lon) Destination(double latDeg, double lonDeg, double bearingRad, double distanceM)
    {
        var lat = latDeg * Math.PI / 180.0;
        var lon = lonDeg * Math.PI / 180.0;
        var delta = distanceM / MoonRadiusM;
        var lat2 = Math.Asin(Math.Sin(lat) * Math.Cos(delta) + Math.Cos(lat) * Math.Sin(delta) * Math.Cos(bearingRad));
        var lon2 = lon + Math.Atan2(Math.Sin(bearingRad) * Math.Sin(delta) * Math.Cos(lat), Math.Cos(delta) - Math.Sin(lat) * Math.Sin(lat2));
        return (lat2 * 180.0 / Math.PI, lon2 * 180.0 / Math.PI);
    }

    public static IReadOnlyList<double> Distances(double rangeM)
    {
        var distances = new List<double>();
        for (var d = 500.0; d <= Math.Min(rangeM, 20000); d += 500)
        {
            distances.Add(d);
        }

        for (var d = 21000.0; d <= Math.Min(rangeM, 80000); d += 1000)
        {
            distances.Add(d);
        }

        for (var d = 82500.0; d <= rangeM; d += 2500)
        {
            distances.Add(d);
        }

        return distances;
    }

    public static HorizonProfile Horizon(Func<double, double, double> sample, double latDeg, double lonDeg, double rangeM = 300000, double observerM = 2)
    {
        var baseHeight = sample(latDeg, lonDeg) + observerM;
        var distances = Distances(rangeM);
        var elevations = new float[HorizonProfile.SampleCount];
        for (var i = 0; i < HorizonProfile.SampleCount; i++)
        {
            var bearing = i * HorizonProfile.StepDeg * Math.PI / 180.0;
            var best = double.NegativeInfinity;
            foreach (var distance in distances)
            {
                var (lat, lon) = Destination(latDeg, lonDeg, bearing, distance);
                var rise = sample(lat, lon) - baseHeight - distance * distance / (2.0 * MoonRadiusM);
                var angle = Math.Atan2(rise, distance);
                if (angle > best)
                {
                    best = angle;
                }
            }

            elevations[i] = (float)Math.Clamp(best * 180.0 / Math.PI, -90.0, 90.0);
        }

        return new HorizonProfile(elevations);
    }

    public static byte[] Tile(Func<double, double, double> sample, double latDeg, double lonDeg, int size = 301, double spacingM = 1000)
    {
        var half = (size - 1) / 2.0;
        var centre = sample(latDeg, lonDeg);
        var bytes = new byte[12 + 2 * size * size];
        BitConverter.TryWriteBytes(bytes.AsSpan(0, 4), size);
        BitConverter.TryWriteBytes(bytes.AsSpan(4, 4), (float)spacingM);
        BitConverter.TryWriteBytes(bytes.AsSpan(8, 4), UnitM);
        for (var row = 0; row < size; row++)
        {
            for (var col = 0; col < size; col++)
            {
                var east = (col - half) * spacingM;
                var north = (half - row) * spacingM;
                var distance = Math.Sqrt(east * east + north * north);
                var (lat, lon) = distance == 0 ? (latDeg, lonDeg) : Destination(latDeg, lonDeg, Math.Atan2(east, north), distance);
                var height = sample(lat, lon) - centre - distance * distance / (2.0 * MoonRadiusM);
                var units = (short)Math.Clamp(Math.Round(height / UnitM), -short.MaxValue, short.MaxValue);
                BitConverter.TryWriteBytes(bytes.AsSpan(12 + 2 * (row * size + col), 2), units);
            }
        }

        return bytes;
    }
}
