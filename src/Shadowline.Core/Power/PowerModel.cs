using Shadowline.Core.Ephemeris;
using Shadowline.Core.Geometry;
using Shadowline.Core.Horizon;
using Shadowline.Core.Thermal;
using Shadowline.Core.Time;

namespace Shadowline.Core.Power;

public enum PanelMount
{
    SunTracking,
    FixedVertical,
    FourFaces,
    Horizontal,
}

public sealed record PowerOptions(
    double Efficiency = 0.30,
    double AreaM2 = 1.0,
    double LoadW = 100.0,
    double SystemFactor = PowerModel.SystemFactorExplicit,
    double TemperatureCoefficient = -0.000857,
    double ReferenceC = 28.0,
    double Absorptance = 0.92,
    double Emittance = 0.85,
    double DustFactor = 1.0,
    bool CellTemperature = true,
    double UsableDepth = 0.8,
    double ChargeEfficiency = 0.95,
    double DischargeEfficiency = 0.95)
{
    public static PowerOptions Default { get; } = new();

    public static PowerOptions Ideal { get; } = new(SystemFactor: 1.0, CellTemperature: false, UsableDepth: 1.0, ChargeEfficiency: 1.0, DischargeEfficiency: 1.0);
}

public sealed record PowerEstimate(
    double VerticalKWhPerDay,
    double HorizontalKWhPerDay,
    double PeakVerticalW,
    double MeanVerticalW,
    double LongestDarkHours,
    double BatteryKWh);

public sealed record MountEstimate(PanelMount Mount, double AzimuthDeg, double KWhPerDay, double PeakW, double LongestDarkHours, double MaxCellC);

public static class PowerModel
{
    public const double SolarConstantW = 1361.0;

    public const double AuKm = 149_597_870.7;

    public const double SystemFactorBol = 0.775;

    public const double TemperatureFactor = 0.950 * 0.930;

    public const double DistanceFactor = 0.967;

    public const double SystemFactorExplicit = SystemFactorBol / (TemperatureFactor * DistanceFactor);

    public const double SystemFactorFixedEfficiency = SystemFactorBol / DistanceFactor;

    public static double VisibleFraction(double elevationDeg, double horizonDeg, double sunRadiusDeg)
    {
        var diameter = 2 * Math.Max(sunRadiusDeg, 1e-6);
        var x = Math.Clamp((elevationDeg - horizonDeg) / diameter + 0.5, 0.0, 1.0);
        var c = 1 - 2 * x;
        return Math.Clamp((Math.Acos(c) - c * Math.Sqrt(1 - c * c)) / Math.PI, 0.0, 1.0);
    }

    public static double Irradiance(SkySnapshot sky, HorizonProfile horizon)
    {
        ArgumentNullException.ThrowIfNull(horizon);
        var fraction = VisibleFraction(sky.Sun.ElevationDeg, horizon.ElevationAt(sky.Sun.AzimuthDeg), sky.SunRadiusDeg);
        var au = AuKm / sky.Sun.RangeKm;
        return SolarConstantW * au * au * fraction * (1 - CoveredFraction(sky.SunRadiusDeg, sky.EarthRadiusDeg, sky.Sun.SeparationDeg(sky.Earth)));
    }

    public static double CoveredFraction(double sunRadiusDeg, double coverRadiusDeg, double separationDeg)
    {
        var r = sunRadiusDeg;
        var R = coverRadiusDeg;
        var d = separationDeg;
        if (r <= 0 || d >= r + R)
        {
            return 0;
        }

        if (d <= R - r)
        {
            return 1;
        }

        if (d <= r - R)
        {
            return R * R / (r * r);
        }

        var a = Math.Acos(Math.Clamp((d * d + r * r - R * R) / (2 * d * r), -1, 1));
        var b = Math.Acos(Math.Clamp((d * d + R * R - r * r) / (2 * d * R), -1, 1));
        var lens = r * r * (a - Math.Sin(2 * a) / 2) + R * R * (b - Math.Sin(2 * b) / 2);
        return Math.Clamp(lens / (Math.PI * r * r), 0, 1);
    }

    public static double CellTemperatureK(double incidentW, double groundK, PowerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var sigma = ThermalModel.StefanBoltzmann;
        var t = 300.0;
        for (var k = 0; k < 3; k++)
        {
            var eta = CellEfficiency(t, options);
            var absorbed = Math.Max(0, options.Absorptance - eta) * incidentW + options.Emittance * sigma * Math.Pow(groundK, 4);
            t = Math.Pow(absorbed / (2 * options.Emittance * sigma), 0.25);
        }

        return t;
    }

    public static double CellEfficiency(double cellK, PowerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!options.CellTemperature)
        {
            return options.Efficiency;
        }

        return Math.Clamp(options.Efficiency + options.TemperatureCoefficient * (cellK - 273.15 - options.ReferenceC), 0, options.Efficiency);
    }

    public static double CosIncidence(SkySnapshot sky, PanelMount mount, double azimuthDeg)
    {
        var el = sky.Sun.ElevationDeg * Math.PI / 180;
        return mount switch
        {
            PanelMount.SunTracking => Math.Max(0, Math.Cos(el)),
            PanelMount.Horizontal => Math.Max(0, Math.Sin(el)),
            PanelMount.FixedVertical => Math.Max(0, Math.Cos(el) * Math.Cos((sky.Sun.AzimuthDeg - azimuthDeg) * Math.PI / 180)),
            _ => Enumerable.Range(0, 4).Sum(k => Math.Max(0, Math.Cos(el) * Math.Cos((sky.Sun.AzimuthDeg - azimuthDeg - 90 * k) * Math.PI / 180))) / 4,
        };
    }

    public static (double Watts, double CellK) MountPower(SkySnapshot sky, HorizonProfile horizon, PowerOptions options, PanelMount mount, double azimuthDeg, double groundK)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (mount == PanelMount.FourFaces)
        {
            var face = options with { AreaM2 = options.AreaM2 / 4 };
            double total = 0, hottest = 0;
            for (var k = 0; k < 4; k++)
            {
                var (w, cell) = MountPower(sky, horizon, face, PanelMount.FixedVertical, azimuthDeg + 90 * k, groundK);
                total += w;
                hottest = Math.Max(hottest, cell);
            }

            return (total, hottest);
        }

        var irradiance = Irradiance(sky, horizon);
        var cos = CosIncidence(sky, mount, azimuthDeg);
        if (irradiance <= 0 || cos <= 0)
        {
            return (0, options.CellTemperature ? CellTemperatureK(0, groundK, options) : 273.15 + options.ReferenceC);
        }

        var incident = irradiance * cos;
        var cellK = options.CellTemperature ? CellTemperatureK(incident, groundK, options) : 273.15 + options.ReferenceC;
        var watts = incident * options.AreaM2 * CellEfficiency(cellK, options) * options.SystemFactor * options.DustFactor;
        return (watts, cellK);
    }

    public static (double Vertical, double Horizontal) PanelPower(SkySnapshot sky, HorizonProfile horizon, PowerOptions options, double groundK = 250)
    {
        var vertical = MountPower(sky, horizon, options, PanelMount.SunTracking, 0, groundK).Watts;
        var horizontal = MountPower(sky, horizon, options, PanelMount.Horizontal, 0, groundK).Watts;
        return (vertical, horizontal);
    }

    private static double Latitude(SiteFrame frame) => Math.Asin(Math.Clamp(frame.Up.Z, -1, 1)) * 180 / Math.PI;

    public static PowerEstimate Evaluate(SiteFrame frame, BodyEphemeris ephemeris, HorizonProfile horizon, TimeGrid grid, int start, int length, PowerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(ephemeris);
        ArgumentNullException.ThrowIfNull(horizon);
        ArgumentNullException.ThrowIfNull(grid);
        var settings = options ?? PowerOptions.Default;
        var latitude = Latitude(frame);
        var first = Math.Clamp(start, 0, grid.Count);
        var last = Math.Clamp(start + length, first, grid.Count);
        var hours = grid.Step.TotalHours;
        double vertical = 0, horizontal = 0, peak = 0;
        int dark = 0, longest = 0;
        for (var i = first; i < last; i++)
        {
            var sky = SkySnapshot.At(frame, ephemeris, i);
            var ground = ThermalModel.SurfaceK(sky, horizon, latitude);
            var (v, h) = PanelPower(sky, horizon, settings, ground);
            vertical += v;
            horizontal += h;
            peak = Math.Max(peak, v);
            if (v <= 0 && h <= 0)
            {
                dark++;
                longest = Math.Max(longest, dark);
            }
            else
            {
                dark = 0;
            }
        }

        var steps = last - first;
        if (steps == 0)
        {
            return new PowerEstimate(0, 0, 0, 0, 0, 0);
        }

        var days = steps * hours / 24;
        var darkHours = longest * hours;
        return new PowerEstimate(
            vertical * hours / 1000 / days,
            horizontal * hours / 1000 / days,
            peak,
            vertical / steps,
            darkHours,
            settings.LoadW * darkHours / 1000 / (settings.UsableDepth * settings.DischargeEfficiency));
    }

    public static IReadOnlyList<double> Series(SiteFrame frame, BodyEphemeris ephemeris, HorizonProfile horizon, int start, int length, PowerOptions options, PanelMount mount, double azimuthDeg)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(ephemeris);
        var latitude = Latitude(frame);
        var list = new double[length];
        for (var i = 0; i < length; i++)
        {
            var sky = SkySnapshot.At(frame, ephemeris, start + i);
            list[i] = MountPower(sky, horizon, options, mount, azimuthDeg, ThermalModel.SurfaceK(sky, horizon, latitude)).Watts;
        }

        return list;
    }

    public static IReadOnlyList<MountEstimate> CompareMounts(SiteFrame frame, BodyEphemeris ephemeris, HorizonProfile horizon, int start, int length, PowerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(ephemeris);
        ArgumentNullException.ThrowIfNull(horizon);
        var settings = options ?? PowerOptions.Default;
        var latitude = Latitude(frame);
        var hours = ephemeris.Grid.Step.TotalHours;
        var azimuths = Enumerable.Range(0, 8).Select(k => k * 45.0).ToArray();
        var configs = new List<(PanelMount Mount, double Az)> { (PanelMount.SunTracking, 0), (PanelMount.FourFaces, 0), (PanelMount.Horizontal, 0) };
        configs.AddRange(azimuths.Select(az => (PanelMount.FixedVertical, az)));
        var energy = new double[configs.Count];
        var peak = new double[configs.Count];
        var dark = new int[configs.Count];
        var longest = new int[configs.Count];
        var hottest = new double[configs.Count];
        for (var i = 0; i < length; i++)
        {
            var sky = SkySnapshot.At(frame, ephemeris, start + i);
            var ground = ThermalModel.SurfaceK(sky, horizon, latitude);
            for (var c = 0; c < configs.Count; c++)
            {
                var (w, cell) = MountPower(sky, horizon, settings, configs[c].Mount, configs[c].Az, ground);
                energy[c] += w * hours;
                peak[c] = Math.Max(peak[c], w);
                hottest[c] = Math.Max(hottest[c], cell);
                dark[c] = w <= 0 ? dark[c] + 1 : 0;
                longest[c] = Math.Max(longest[c], dark[c]);
            }
        }

        var days = Math.Max(length * hours / 24, 1e-9);
        var all = configs.Select((cfg, c) => new MountEstimate(cfg.Mount, cfg.Az, energy[c] / 1000 / days, peak[c], longest[c] * hours, hottest[c] - 273.15)).ToList();
        var fixedBest = all.Where(m => m.Mount == PanelMount.FixedVertical).OrderByDescending(m => m.KWhPerDay).First();
        var fixedWorst = all.Where(m => m.Mount == PanelMount.FixedVertical).OrderBy(m => m.KWhPerDay).First();
        return [all[0], fixedBest, fixedWorst, all[1], all[2]];
    }
}
