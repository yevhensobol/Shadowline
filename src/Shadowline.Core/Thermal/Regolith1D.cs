using Shadowline.Core.Ephemeris;
using Shadowline.Core.Geometry;
using Shadowline.Core.Horizon;

namespace Shadowline.Core.Thermal;

public sealed record RegolithParameters(
    double SurfaceDensity = 1100,
    double DeepDensity = 1800,
    double HParameterM = 0.06,
    double SurfaceConductivity = 7.4e-4,
    double DeepConductivity = 3.4e-3,
    double Chi = 2.7,
    double Albedo0 = 0.12,
    double AlbedoA = 0.06,
    double AlbedoB = 0.25,
    double Emissivity = 0.95,
    double GeothermalFlux = 0.018)
{
    public static RegolithParameters Hayne2017 { get; } = new();
}

public sealed record RegolithResult(double StartK, double MaxK, double MinK, double MeanK, double SunlitMinK, double DarkMinK, double SubsurfaceK, IReadOnlyList<double> SurfaceK, double Mean2cmK = double.NaN);

public static class Regolith1D
{
    public const double SynodicSeconds = 29.530589 * 86400;

    private const double Sigma = 5.670374419e-8;

    public static double HeatCapacity(double t) => -3.6125 + t * (2.7431 + t * (2.3616e-3 + t * (-1.2340e-5 + t * 8.9093e-9)));

    public static double Albedo(double incidenceRad, RegolithParameters p)
    {
        ArgumentNullException.ThrowIfNull(p);
        return p.Albedo0 + p.AlbedoA * Math.Pow(incidenceRad / (Math.PI / 4), 3) + p.AlbedoB * Math.Pow(incidenceRad / (Math.PI / 2), 8);
    }

    public static double AbsorbedFlux(SkySnapshot sky, HorizonProfile horizon, RegolithParameters p, double slopeDeg = 0, double aspectDeg = 0)
    {
        ArgumentNullException.ThrowIfNull(horizon);
        var el = sky.Sun.ElevationDeg * Math.PI / 180;
        var irradiance = PowerModel.Irradiance(sky, horizon);
        if (irradiance <= 0)
        {
            return 0;
        }

        var s = slopeDeg * Math.PI / 180;
        var cos = Math.Cos(s) * Math.Sin(el) + Math.Sin(s) * Math.Cos(el) * Math.Cos((sky.Sun.AzimuthDeg - aspectDeg) * Math.PI / 180);
        if (cos <= 0)
        {
            return 0;
        }

        var incidence = Math.Acos(Math.Min(1, cos));
        return irradiance * (1 - Albedo(incidence, p)) * cos;
    }

    public const int YearCycles = 2;

    public static RegolithResult Run(SiteFrame frame, BodyEphemeris ephemeris, HorizonProfile horizon, int start, int length, RegolithParameters? parameters = null, int spinUpLunations = 4, int substeps = 2, double slopeDeg = 0, double aspectDeg = 0, int yearCycles = YearCycles)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(ephemeris);
        ArgumentNullException.ThrowIfNull(horizon);
        var p = parameters ?? RegolithParameters.Hayne2017;
        var grid = ephemeris.Grid;
        var first = Math.Clamp(start, 0, grid.Count - 1);
        var steps = Math.Clamp(length, 1, grid.Count - first);
        var stepSeconds = grid.Step.TotalSeconds;
        var lunationSteps = (int)Math.Round(SynodicSeconds / stepSeconds);
        var wholeYear = yearCycles > 0 && grid.Count * stepSeconds >= 330 * 86400.0;
        var fluxFrom = wholeYear ? 0 : first;
        var fluxCount = wholeYear ? grid.Count : Math.Min(Math.Max(steps, lunationSteps), grid.Count - first);
        var flux = new double[fluxCount + 1];
        for (var i = 0; i < fluxCount; i++)
        {
            flux[i] = AbsorbedFlux(SkySnapshot.At(frame, ephemeris, fluxFrom + i), horizon, p, slopeDeg, aspectDeg);
        }

        flux[fluxCount] = flux[fluxCount - 1];
        var model = new Column(p);
        var spinSteps = wholeYear ? fluxCount : Math.Min(lunationSteps, fluxCount);
        var meanFlux = flux.Take(spinSteps).Average();
        var geothermal = Math.Pow(p.GeothermalFlux / (p.Emissivity * Sigma), 0.25);
        model.Initialize(Math.Max(geothermal, Math.Pow(Math.Max(meanFlux, 0) / (p.Emissivity * Sigma), 0.25) * 0.885));

        void Advance(int from, int count, int wrap)
        {
            for (var i = from; i < from + count; i++)
            {
                for (var s = 0; s < substeps; s++)
                {
                    var next = i + 1 < wrap ? i + 1 : (wrap == fluxCount ? i : 0);
                    var f = flux[i] + (flux[next] - flux[i]) * s / substeps;
                    model.Step(f, stepSeconds / substeps);
                }
            }
        }

        var cycles = wholeYear ? yearCycles : spinUpLunations;
        for (var pass = 0; pass < cycles; pass++)
        {
            Advance(0, spinSteps, spinSteps);
        }

        var offset = wholeYear ? first : 0;
        if (offset > 0)
        {
            Advance(0, offset, fluxCount);
        }

        var series = new double[steps];
        double sum2cm = 0;
        for (var i = 0; i < steps; i++)
        {
            series[i] = model.Surface;
            sum2cm += model.At(0.02);
            var k = offset + i;
            for (var s = 0; s < substeps; s++)
            {
                var f = flux[k] + (flux[Math.Min(k + 1, offset + steps - 1)] - flux[k]) * s / substeps;
                model.Step(f, stepSeconds / substeps);
            }
        }

        double sunlitMin = double.MaxValue, darkMin = double.MaxValue;
        for (var i = 0; i < steps; i++)
        {
            if (flux[offset + i] > 0)
            {
                sunlitMin = Math.Min(sunlitMin, series[i]);
            }
            else
            {
                darkMin = Math.Min(darkMin, series[i]);
            }
        }

        return new RegolithResult(
            series[0],
            series.Max(),
            series.Min(),
            series.Average(),
            sunlitMin == double.MaxValue ? double.NaN : sunlitMin,
            darkMin == double.MaxValue ? double.NaN : darkMin,
            model.Deep,
            series,
            sum2cm / steps);
    }

    private sealed class Column
    {
        private readonly RegolithParameters _p;
        private readonly double[] _z;
        private readonly double[] _dz;
        private readonly double[] _rho;
        private readonly double[] _kc;
        private double[] _t = [];

        public Column(RegolithParameters p)
        {
            _p = p;
            var kappa = p.SurfaceConductivity / (p.SurfaceDensity * HeatCapacity(250));
            var skin = Math.Sqrt(kappa * SynodicSeconds / Math.PI);
            var z = new List<double> { 0 };
            var dz = skin / 10;
            while (z[^1] < 20 * skin)
            {
                z.Add(z[^1] + dz);
                dz *= 1 + 1.0 / 5;
            }

            _z = [.. z];
            _dz = new double[_z.Length - 1];
            for (var i = 0; i < _dz.Length; i++)
            {
                _dz[i] = _z[i + 1] - _z[i];
            }

            _rho = _z.Select(d => p.DeepDensity - (p.DeepDensity - p.SurfaceDensity) * Math.Exp(-d / p.HParameterM)).ToArray();
            _kc = _z.Select(d => p.DeepConductivity - (p.DeepConductivity - p.SurfaceConductivity) * Math.Exp(-d / p.HParameterM)).ToArray();
        }

        public int Count => _z.Length;

        public double Surface => _t[0];

        public double Deep => _t[^1];

        public double At(double depth)
        {
            for (var i = 1; i < _z.Length; i++)
            {
                if (_z[i] >= depth)
                {
                    var w = (depth - _z[i - 1]) / (_z[i] - _z[i - 1]);
                    return _t[i - 1] * (1 - w) + _t[i] * w;
                }
            }

            return _t[^1];
        }

        public void Initialize(double t)
        {
            _t = Enumerable.Repeat(t, _z.Length).ToArray();
        }

        private double K(int i, double t)
        {
            var x = t / 350.0;
            return _kc[i] * (1 + _p.Chi * x * x * x);
        }

        private double[] _a = [];
        private double[] _b = [];
        private double[] _c = [];
        private double[] _d = [];
        private double[] _k = [];
        private double[] _cp = [];
        private double[] _dp = [];

        public void Step(double flux, double dt)
        {
            var n = _t.Length;
            if (_a.Length != n)
            {
                _a = new double[n];
                _b = new double[n];
                _c = new double[n];
                _d = new double[n];
                _k = new double[n];
                _cp = new double[n];
                _dp = new double[n];
            }

            var a = _a;
            var b = _b;
            var c = _c;
            var d = _d;
            var k = _k;
            for (var i = 0; i < n; i++)
            {
                k[i] = K(i, _t[i]);
            }

            for (var i = 1; i < n - 1; i++)
            {
                var up = 0.5 * (k[i - 1] + k[i]) / _dz[i - 1];
                var down = 0.5 * (k[i] + k[i + 1]) / _dz[i];
                var capacity = _rho[i] * HeatCapacity(_t[i]) * 0.5 * (_dz[i - 1] + _dz[i]) / dt;
                a[i] = -up;
                b[i] = capacity + up + down;
                c[i] = -down;
                d[i] = capacity * _t[i];
            }

            a[n - 1] = -1;
            b[n - 1] = 1;
            d[n - 1] = _p.GeothermalFlux / k[n - 2] * _dz[n - 2];
            var guess = _t[0];
            var result = new double[n];
            for (var iter = 0; iter < 6; iter++)
            {
                var ks = 0.5 * (K(0, guess) + k[1]);
                var g0 = ks / _dz[0];
                var rad = _p.Emissivity * Sigma * guess * guess * guess;
                b[0] = 4 * rad + g0;
                c[0] = -g0;
                d[0] = flux + 3 * rad * guess;
                Tridiagonal(a, b, c, d, _cp, _dp, result);
                if (Math.Abs(result[0] - guess) < 1e-3)
                {
                    break;
                }

                guess = Math.Max(2.0, result[0]);
            }

            result[0] = Math.Max(2.0, result[0]);
            _t = result;
        }

        private static void Tridiagonal(double[] a, double[] b, double[] c, double[] d, double[] cp, double[] dp, double[] x)
        {
            var n = d.Length;
            cp[0] = c[0] / b[0];
            dp[0] = d[0] / b[0];
            for (var i = 1; i < n; i++)
            {
                var m = b[i] - a[i] * cp[i - 1];
                cp[i] = i < n - 1 ? c[i] / m : 0;
                dp[i] = (d[i] - a[i] * dp[i - 1]) / m;
            }

            x[n - 1] = dp[n - 1];
            for (var i = n - 2; i >= 0; i--)
            {
                x[i] = dp[i] - cp[i] * x[i + 1];
            }
        }
    }
}
