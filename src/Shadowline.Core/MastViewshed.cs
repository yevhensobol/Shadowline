using Shadowline.Core.Terrain;

namespace Shadowline.Core.Radio;

public sealed class MastViewshed
{
    public const int Rays = 1440;
    public const double StepM = 80;
    public const double RangeM = 22000;
    public const double MastM = 4;
    public const double ReceiverM = 1.5;
    public const double EvaRadiusM = 2000;
    public const double NearRadiusM = 10000;
    public const int MapSizePx = 176;
    public const int ShadeLevels = 64;
    public const byte Outside = 2 * ShadeLevels;

    public static int Steps => (int)Math.Round(RangeM / StepM);

    private readonly bool[] _visible;
    private readonly double _x0;
    private readonly double _y0;

    private MastViewshed(bool[] visible, double x0, double y0)
    {
        _visible = visible;
        _x0 = x0;
        _y0 = y0;
    }

    public bool Visible(int ray, int step) => _visible[ray * Steps + step];

    public static double DistanceM(int step) => (step + 1) * StepM;

    public static MastViewshed? Compute(double x0, double y0, Func<double, double, double?> sample)
    {
        ArgumentNullException.ThrowIfNull(sample);
        if (sample(x0, y0) is not { } ground)
        {
            return null;
        }

        var steps = Steps;
        var baseHeight = ground + MastM;
        var rho = Math.Sqrt(x0 * x0 + y0 * y0);
        var (nx, ny) = rho > 1 ? (x0 / rho, y0 / rho) : (0.0, 1.0);
        var (ex, ey) = (ny, -nx);
        var visible = new bool[Rays * steps];
        for (var ray = 0; ray < Rays; ray++)
        {
            var azimuth = ray * 2 * Math.PI / Rays;
            var dx = Math.Cos(azimuth) * nx + Math.Sin(azimuth) * ex;
            var dy = Math.Cos(azimuth) * ny + Math.Sin(azimuth) * ey;
            var blocking = double.NegativeInfinity;
            for (var step = 0; step < steps; step++)
            {
                var distance = DistanceM(step);
                if (sample(x0 + dx * distance, y0 + dy * distance) is not { } height)
                {
                    break;
                }

                var terrain = (height - distance * distance / (2.0 * LocalTerrain.MoonRadiusM) - baseHeight) / distance;
                visible[ray * steps + step] = terrain + ReceiverM / distance >= blocking;
                if (terrain > blocking)
                {
                    blocking = terrain;
                }
            }
        }

        return new MastViewshed(visible, x0, y0);
    }

    public double FractionWithin(double radiusM)
    {
        var steps = Steps;
        double seen = 0, total = 0;
        for (var step = 0; step < steps; step++)
        {
            var distance = DistanceM(step);
            if (distance > radiusM + 1e-6)
            {
                break;
            }

            var count = 0;
            for (var ray = 0; ray < Rays; ray++)
            {
                if (_visible[ray * steps + step])
                {
                    count++;
                }
            }

            seen += count * distance;
            total += Rays * distance;
        }

        return total > 0 ? seen / total : 0;
    }

    public double EvaFraction => FractionWithin(EvaRadiusM);

    public double NearFraction => FractionWithin(NearRadiusM);

    public double AreaKm2 => FractionWithin(RangeM) * Math.PI * Math.Pow(RangeM / 1000.0, 2);

    public byte[] Raster(int sizePx)
    {
        var raster = new byte[sizePx * sizePx];
        var cell = 2 * RangeM / sizePx;
        var steps = Steps;
        for (var row = 0; row < sizePx; row++)
        {
            var north = (sizePx / 2.0 - row - 0.5) * cell;
            for (var col = 0; col < sizePx; col++)
            {
                var east = (col + 0.5 - sizePx / 2.0) * cell;
                var distance = Math.Sqrt(north * north + east * east);
                var step = (int)Math.Round(distance / StepM) - 1;
                if (distance > RangeM || step < 0)
                {
                    raster[row * sizePx + col] = (byte)(step < 0 ? 2 : 0);
                    continue;
                }

                var azimuth = Math.Atan2(east, north);
                var ray = (int)Math.Round(azimuth / (2 * Math.PI) * Rays);
                ray = ((ray % Rays) + Rays) % Rays;
                raster[row * sizePx + col] = (byte)(_visible[ray * steps + Math.Min(step, steps - 1)] ? 2 : 1);
            }
        }

        return raster;
    }

    public byte[] ShadedMap(Func<double, double, double?> sample, int sizePx = MapSizePx)
    {
        ArgumentNullException.ThrowIfNull(sample);
        var visible = Raster(sizePx);
        var cell = 2 * RangeM / sizePx;
        var rho = Math.Sqrt(_x0 * _x0 + _y0 * _y0);
        var (nx, ny) = rho > 1 ? (_x0 / rho, _y0 / rho) : (0.0, 1.0);
        var (ex, ey) = (ny, -nx);
        double? Height(double east, double north) => sample(_x0 + east * ex + north * nx, _y0 + east * ey + north * ny);
        var sunAz = 315 * Math.PI / 180;
        var sunEl = 45 * Math.PI / 180;
        var map = new byte[sizePx * sizePx];
        for (var row = 0; row < sizePx; row++)
        {
            var north = (sizePx / 2.0 - row - 0.5) * cell;
            for (var col = 0; col < sizePx; col++)
            {
                var east = (col + 0.5 - sizePx / 2.0) * cell;
                var index = row * sizePx + col;
                if (visible[index] == 0)
                {
                    map[index] = Outside;
                    continue;
                }

                var shade = 0.5;
                if (Height(east + cell, north) is { } he && Height(east - cell, north) is { } hw && Height(east, north + cell) is { } hn && Height(east, north - cell) is { } hs)
                {
                    var gx = (he - hw) / (2 * cell);
                    var gy = (hn - hs) / (2 * cell);
                    var normal = Math.Sqrt(1 + gx * gx + gy * gy);
                    var lit = (-gx * Math.Sin(sunAz) * Math.Cos(sunEl) - gy * Math.Cos(sunAz) * Math.Cos(sunEl) + Math.Sin(sunEl)) / normal;
                    shade = Math.Clamp(lit, 0, 1);
                }

                var level = (int)Math.Round(shade * (ShadeLevels - 1));
                map[index] = (byte)((visible[index] == 2 ? ShadeLevels : 0) + level);
            }
        }

        return map;
    }
}
