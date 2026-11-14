using Shadowline.Core.Horizon;

namespace Shadowline.Core.Terrain;

public sealed record SlopeStats(double AtSiteDeg, double MeanDeg, double MaxDeg, double SteepShare, double RadiusM, double BaselineM, double ThresholdDeg);

public sealed class TerrainPatch
{
    private readonly float[] _heights;

    public TerrainPatch(int size, double spacingM, float[] heightsM)
    {
        ArgumentNullException.ThrowIfNull(heightsM);
        if (size < 3 || spacingM <= 0 || heightsM.Length != size * size)
        {
            throw new ArgumentException("Bad terrain patch");
        }

        Size = size;
        SpacingM = spacingM;
        _heights = heightsM;
    }

    public int Size { get; }

    public double SpacingM { get; }

    public double HalfExtentM => (Size - 1) / 2.0 * SpacingM;

    public static TerrainPatch Parse(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length < 12)
        {
            throw new ArgumentException("Terrain patch too short");
        }

        var size = BitConverter.ToInt32(bytes, 0);
        var spacing = BitConverter.ToSingle(bytes, 4);
        var unit = BitConverter.ToSingle(bytes, 8);
        if (size < 3 || size > 4096 || bytes.Length != 12 + size * size * 2 || spacing <= 0 || unit <= 0)
        {
            throw new ArgumentException("Bad terrain patch header");
        }

        var heights = new float[size * size];
        for (var i = 0; i < heights.Length; i++)
        {
            heights[i] = BitConverter.ToInt16(bytes, 12 + i * 2) * unit;
        }

        return new TerrainPatch(size, spacing, heights);
    }

    public double Height(double eastM, double northM)
    {
        var c = (Size - 1) / 2.0;
        var fx = Math.Clamp(c + eastM / SpacingM, 0, Size - 1.000001);
        var fy = Math.Clamp(c - northM / SpacingM, 0, Size - 1.000001);
        var x0 = (int)fx;
        var y0 = (int)fy;
        var ax = fx - x0;
        var ay = fy - y0;
        var top = _heights[y0 * Size + x0] * (1 - ax) + _heights[y0 * Size + x0 + 1] * ax;
        var bottom = _heights[(y0 + 1) * Size + x0] * (1 - ax) + _heights[(y0 + 1) * Size + x0 + 1] * ax;
        return top * (1 - ay) + bottom * ay;
    }

    public bool Contains(double eastM, double northM) => Math.Abs(eastM) <= HalfExtentM && Math.Abs(northM) <= HalfExtentM;

    public double SlopeDeg(double eastM, double northM)
    {
        var d = SpacingM;
        var gx = (Height(eastM + d, northM) - Height(eastM - d, northM)) / (2 * d);
        var gy = (Height(eastM, northM + d) - Height(eastM, northM - d)) / (2 * d);
        return Math.Atan(Math.Sqrt(gx * gx + gy * gy)) * 180 / Math.PI;
    }

    public (double SlopeDeg, double AspectDeg) Tilt(double baselineM)
    {
        var d = Math.Max(SpacingM, baselineM / 2);
        var gx = (Height(d, 0) - Height(-d, 0)) / (2 * d);
        var gy = (Height(0, d) - Height(0, -d)) / (2 * d);
        var slope = Math.Atan(Math.Sqrt(gx * gx + gy * gy)) * 180 / Math.PI;
        var aspect = (Math.Atan2(-gx, -gy) * 180 / Math.PI + 360) % 360;
        return (slope, aspect);
    }

    public SlopeStats Slopes(double radiusM, double thresholdDeg)
    {
        var steps = Math.Max(1, (int)Math.Floor(radiusM / SpacingM));
        double sum = 0, max = 0;
        int count = 0, steep = 0;
        for (var j = -steps; j <= steps; j++)
        {
            for (var i = -steps; i <= steps; i++)
            {
                var e = i * SpacingM;
                var n = j * SpacingM;
                if (e * e + n * n > radiusM * radiusM + 1e-6 || !Contains(Math.Abs(e) + SpacingM, Math.Abs(n) + SpacingM))
                {
                    continue;
                }

                var slope = SlopeDeg(e, n);
                sum += slope;
                max = Math.Max(max, slope);
                count++;
                if (slope >= thresholdDeg)
                {
                    steep++;
                }
            }
        }

        return new SlopeStats(SlopeDeg(0, 0), count == 0 ? 0 : sum / count, max, count == 0 ? 0 : (double)steep / count, radiusM, 2 * SpacingM, thresholdDeg);
    }

    public HorizonProfile Horizon(double eastM, double northM, double observerM = 2.0, int azimuths = 360)
    {
        var baseHeight = Height(eastM, northM) + observerM;
        var maxRange = HalfExtentM * 1.4;
        var distances = new List<double>();
        for (var d = SpacingM; d <= maxRange; d += Math.Max(SpacingM, d * 0.02))
        {
            distances.Add(d);
        }

        var coarse = new float[azimuths];
        for (var k = 0; k < azimuths; k++)
        {
            var az = k * 360.0 / azimuths * Math.PI / 180;
            var se = Math.Sin(az);
            var ce = Math.Cos(az);
            var best = double.NegativeInfinity;
            foreach (var d in distances)
            {
                var te = eastM + d * se;
                var tn = northM + d * ce;
                if (!Contains(te, tn))
                {
                    break;
                }

                var curvature = (eastM * (te - eastM) + northM * (tn - northM)) / LocalTerrain.MoonRadiusM;
                var rise = Height(te, tn) - baseHeight + curvature;
                var angle = Math.Atan2(rise, d);
                if (angle > best)
                {
                    best = angle;
                }
            }

            coarse[k] = (float)(double.IsNegativeInfinity(best) ? 0 : best * 180 / Math.PI);
        }

        var elevations = new float[HorizonProfile.SampleCount];
        for (var i = 0; i < elevations.Length; i++)
        {
            var position = i * HorizonProfile.StepDeg / (360.0 / azimuths);
            var a = (int)Math.Floor(position) % azimuths;
            var b = (a + 1) % azimuths;
            var t = position - Math.Floor(position);
            elevations[i] = (float)(coarse[a] * (1 - t) + coarse[b] * t);
        }

        return new HorizonProfile(elevations);
    }
}
