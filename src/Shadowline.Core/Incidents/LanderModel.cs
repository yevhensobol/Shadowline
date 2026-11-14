using Shadowline.Core.Ephemeris;
using Shadowline.Core.Geometry;
using Shadowline.Core.Horizon;

namespace Shadowline.Core.Incidents;

public sealed record LanderAttitude(double TiltDeg, double TiltAzimuthDeg, double SpinDeg)
{
    public static LanderAttitude Upright { get; } = new(0, 0, 0);

    public Vec3 ToLocal(Vec3 body)
    {
        var spin = SpinDeg * Math.PI / 180;
        var (s, c) = Math.SinCos(spin);
        var v = new Vec3(body.X * c + body.Y * s, -body.X * s + body.Y * c, body.Z);
        var tilt = TiltDeg * Math.PI / 180;
        var t = TiltAzimuthDeg * Math.PI / 180;
        var k = new Vec3(-Math.Cos(t), Math.Sin(t), 0);
        var cross = new Vec3(k.Y * v.Z - k.Z * v.Y, k.Z * v.X - k.X * v.Z, k.X * v.Y - k.Y * v.X);
        var (st, ct) = Math.SinCos(tilt);
        return v * ct + cross * st + k * (Vec3.Dot(k, v) * (1 - ct));
    }
}

public sealed record LanderPanel(string Id, double AzimuthDeg, double ElevationDeg, double AreaM2)
{
    public Vec3 BodyNormal => LanderGeometry.Direction(AzimuthDeg, ElevationDeg);
}

public sealed record LanderBattery(double CapacityWh, double StartFraction, double FloorFraction, double ChargeEfficiency = 0.95, double DischargeEfficiency = 0.95);

public sealed record LanderSpec(
    IReadOnlyList<LanderPanel> Panels,
    double Efficiency,
    double SystemFactor,
    double DustFactor,
    LanderBattery Battery,
    double LoadW,
    double SleepLoadW,
    double WakeW,
    double LocalHorizonDeg = 0)
{
    public double PeakW => Panels.Sum(p => p.AreaM2) * PowerModel.SolarConstantW * Efficiency * SystemFactor * DustFactor;
}

public enum LanderState
{
    Awake,
    Asleep,
}

public sealed record LanderEvent(DateTimeOffset Utc, LanderState State);

public sealed record LanderRun(
    DateTimeOffset StartUtc,
    double StepHours,
    double[] PowerW,
    double[] Charge,
    bool[] Awake,
    double[] SunElevationDeg,
    double[] HorizonDeg,
    IReadOnlyList<LanderEvent> Events)
{
    public double EnergyKWh => PowerW.Sum() * StepHours / 1000;

    public double AwakeShare => Awake.Length == 0 ? 0 : (double)Awake.Count(a => a) / Awake.Length;

    public double PeakWatts() => PowerW.Length == 0 ? 0 : PowerW.Max();
}

public static class LanderGeometry
{
    public static Vec3 Direction(double azimuthDeg, double elevationDeg)
    {
        var a = azimuthDeg * Math.PI / 180;
        var e = elevationDeg * Math.PI / 180;
        return new Vec3(Math.Sin(a) * Math.Cos(e), Math.Cos(a) * Math.Cos(e), Math.Sin(e));
    }

    public static (double AzimuthDeg, double ElevationDeg) Angles(Vec3 local)
    {
        var n = local.Normalized();
        var az = Math.Atan2(n.X, n.Y) * 180 / Math.PI;
        return ((az + 360) % 360, Math.Asin(Math.Clamp(n.Z, -1, 1)) * 180 / Math.PI);
    }

    public static double CraterHorizonDeg(double diameterM, double depthRatio, double heightM, double offsetM = 0)
    {
        var depth = depthRatio * diameterM;
        var run = Math.Max(0.1, diameterM / 2 - offsetM);
        return Math.Atan2(Math.Max(0, depth - heightM), run) * 180 / Math.PI;
    }
}

public sealed class SunTrack
{
    private SunTrack(DateTimeOffset start, double stepHours, Vec3[] direction, double[] irradiance, double[] elevation, double[] horizon)
    {
        StartUtc = start;
        StepHours = stepHours;
        Direction = direction;
        Irradiance = irradiance;
        ElevationDeg = elevation;
        HorizonDeg = horizon;
    }

    public DateTimeOffset StartUtc { get; }

    public double StepHours { get; }

    public Vec3[] Direction { get; }

    public double[] Irradiance { get; }

    public double[] ElevationDeg { get; }

    public double[] HorizonDeg { get; }

    public int Count => Direction.Length;

    public int IndexOf(DateTimeOffset utc) => (int)Math.Round((utc - StartUtc).TotalHours / StepHours);

    public DateTimeOffset At(int index) => StartUtc + TimeSpan.FromHours(index * StepHours);

    public static SunTrack Build(SiteFrame frame, BodyEphemeris ephemeris, HorizonProfile horizon, double localHorizonDeg = 0, double horizonOffsetDeg = 0)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(ephemeris);
        ArgumentNullException.ThrowIfNull(horizon);
        var n = ephemeris.Grid.Count;
        var direction = new Vec3[n];
        var irradiance = new double[n];
        var elevation = new double[n];
        var hz = new double[n];
        for (var i = 0; i < n; i++)
        {
            var sky = SkySnapshot.At(frame, ephemeris, i);
            direction[i] = LanderGeometry.Direction(sky.Sun.AzimuthDeg, sky.Sun.ElevationDeg);
            var terrain = horizon.ElevationAt(sky.Sun.AzimuthDeg) + horizonOffsetDeg;
            hz[i] = localHorizonDeg > 0 ? Math.Max(terrain, localHorizonDeg) : terrain;
            elevation[i] = sky.Sun.ElevationDeg;
            var fraction = PowerModel.VisibleFraction(sky.Sun.ElevationDeg, hz[i], sky.SunRadiusDeg);
            var au = PowerModel.AuKm / sky.Sun.RangeKm;
            irradiance[i] = PowerModel.SolarConstantW * au * au * fraction;
        }

        return new SunTrack(ephemeris.Grid.Start, ephemeris.Grid.Step.TotalHours, direction, irradiance, elevation, hz);
    }
}

public static class LanderModel
{
    public static double CosIncidence(SunTrack track, int index, Vec3 normal)
    {
        ArgumentNullException.ThrowIfNull(track);
        return track.Irradiance[index] <= 0 ? 0 : Math.Max(0, Vec3.Dot(track.Direction[index], normal));
    }

    public static double Power(SunTrack track, int index, LanderSpec spec, IReadOnlyList<Vec3> normals)
    {
        ArgumentNullException.ThrowIfNull(track);
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(normals);
        if (track.Irradiance[index] <= 0)
        {
            return 0;
        }

        var sum = 0.0;
        for (var p = 0; p < normals.Count; p++)
        {
            sum += spec.Panels[p].AreaM2 * Math.Max(0, Vec3.Dot(track.Direction[index], normals[p]));
        }

        return sum * track.Irradiance[index] * spec.Efficiency * spec.SystemFactor * spec.DustFactor;
    }

    public static IReadOnlyList<Vec3> Normals(LanderSpec spec, LanderAttitude attitude)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(attitude);
        return spec.Panels.Select(p => attitude.ToLocal(p.BodyNormal)).ToList();
    }

    public static LanderRun Run(SunTrack track, LanderSpec spec, LanderAttitude attitude, DateTimeOffset fromUtc, DateTimeOffset? toUtc = null)
    {
        ArgumentNullException.ThrowIfNull(track);
        ArgumentNullException.ThrowIfNull(spec);
        var normals = Normals(spec, attitude);
        var first = Math.Clamp(track.IndexOf(fromUtc), 0, track.Count - 1);
        var last = toUtc is { } end ? Math.Clamp(track.IndexOf(end), first + 1, track.Count) : track.Count;
        var n = last - first;
        var power = new double[n];
        var charge = new double[n];
        var awake = new bool[n];
        var elevation = new double[n];
        var horizon = new double[n];
        var events = new List<LanderEvent>();
        var capacity = spec.Battery.CapacityWh;
        var floor = capacity * spec.Battery.FloorFraction;
        var stored = capacity * spec.Battery.StartFraction;
        var on = true;
        events.Add(new LanderEvent(track.At(first), LanderState.Awake));
        for (var k = 0; k < n; k++)
        {
            var i = first + k;
            var p = Power(track, i, spec, normals);
            power[k] = p;
            elevation[k] = track.ElevationDeg[i];
            horizon[k] = track.HorizonDeg[i];
            if (!on && p >= spec.WakeW)
            {
                on = true;
                events.Add(new LanderEvent(track.At(i), LanderState.Awake));
            }

            var net = p - (on ? spec.LoadW : spec.SleepLoadW);
            var change = net >= 0 ? net * spec.Battery.ChargeEfficiency : net / spec.Battery.DischargeEfficiency;
            stored = Math.Clamp(stored + change * track.StepHours, 0, capacity);
            if (on && stored <= floor && p < spec.LoadW)
            {
                on = false;
                stored = Math.Max(stored, 0);
                events.Add(new LanderEvent(track.At(i), LanderState.Asleep));
            }

            charge[k] = capacity <= 0 ? 0 : stored / capacity;
            awake[k] = on;
        }

        return new LanderRun(track.At(first), track.StepHours, power, charge, awake, elevation, horizon, events);
    }
}
