namespace Shadowline.Core.Missions;

public sealed record LanderStance(string Id, string Name, int Legs, double PadRadiusM, double CgHeightM, double GyrationM2)
{
    public double FootprintM => 2 * PadRadiusM;

    public double EdgeM => PadRadiusM * Math.Cos(Math.PI / Legs);

    public double StaticTipDeg => Math.Atan2(EdgeM, CgHeightM) * 180 / Math.PI;

    public static LanderStance NovaC { get; } = new("nova-c", "Nova-C", 6, 2.2, 2.4, 0.91);

    public static LanderStance ApolloLm { get; } = new("apollo-lm", "Apollo LM", 4, 4.256, 3.62, 3.5);

    public static LanderStance BlueGhost { get; } = new("blue-ghost", "Blue Ghost", 4, 1.5, 1.0, 0.4);

    public static IReadOnlyList<LanderStance> Presets { get; } = [NovaC, BlueGhost, ApolloLm];

    public static LanderStance Find(string? id) => Presets.FirstOrDefault(item => item.Id == id) ?? NovaC;
}

public readonly record struct TouchdownSlope(double SlopeDeg, double Weight);

public sealed record TouchdownCheck(
    string StanceId,
    double LateralSpeedMs,
    double TipProbability,
    double SiteSlopeDeg,
    double FootprintSlopeDeg,
    double TipSpeedMs,
    double BaselineM,
    int Samples);

public static class TouchdownStability
{
    public const double LunarGravity = 1.625;

    public const double HurstHighlands = 0.95;

    public const double RayleighP99 = 3.0349;

    public static double FootprintSlopeDeg(double slopeDeg, double baselineM, double footprintM, double hurst = HurstHighlands)
    {
        if (baselineM <= 0 || footprintM <= 0 || footprintM >= baselineM)
        {
            return slopeDeg;
        }

        var tan = Math.Tan(Math.Clamp(slopeDeg, 0, 89) * Math.PI / 180) * Math.Pow(footprintM / baselineM, hurst - 1);
        return Math.Atan(tan) * 180 / Math.PI;
    }

    public static double SupportM(LanderStance stance, double offsetDeg)
    {
        ArgumentNullException.ThrowIfNull(stance);
        var sector = 360.0 / stance.Legs;
        var a = ((offsetDeg % sector) + sector) % sector - sector / 2;
        return stance.EdgeM / Math.Cos(a * Math.PI / 180);
    }

    public static double TipSpeed(LanderStance stance, double slopeDeg, double headingFromDownhillDeg, double yawDeg, double gravity = LunarGravity)
    {
        ArgumentNullException.ThrowIfNull(stance);
        var h = stance.CgHeightM;
        if (h * Math.Tan(slopeDeg * Math.PI / 180) >= SupportM(stance, -yawDeg))
        {
            return 0;
        }

        var x = SupportM(stance, headingFromDownhillDeg - yawDeg);
        var tilt = Math.Atan(Math.Tan(slopeDeg * Math.PI / 180) * Math.Cos(headingFromDownhillDeg * Math.PI / 180));
        if (h * Math.Sin(tilt) >= x * Math.Cos(tilt))
        {
            return 0;
        }

        var r = Math.Sqrt(x * x + h * h);
        var y = h * Math.Cos(tilt) + x * Math.Sin(tilt);
        return Math.Sqrt(2 * gravity * (stance.GyrationM2 + r * r) * (r - y) / (y * y));
    }

    public static double WorstTipSpeed(LanderStance stance, double slopeDeg, double gravity = LunarGravity)
    {
        ArgumentNullException.ThrowIfNull(stance);
        var worst = double.PositiveInfinity;
        for (var heading = 0; heading < 360; heading += 5)
        {
            for (var yaw = 0.0; yaw < 360.0 / stance.Legs; yaw += 5)
            {
                worst = Math.Min(worst, TipSpeed(stance, slopeDeg, heading, yaw, gravity));
            }
        }

        return worst;
    }

    public static double TipProbability(LanderStance stance, IReadOnlyList<TouchdownSlope> slopes, double lateralSpeedMs, int samples = 4000, int seed = 7)
    {
        ArgumentNullException.ThrowIfNull(stance);
        ArgumentNullException.ThrowIfNull(slopes);
        var total = slopes.Sum(item => Math.Max(0, item.Weight));
        if (slopes.Count == 0 || total <= 0 || samples <= 0)
        {
            return 0;
        }

        var sigma = Math.Max(0, lateralSpeedMs) / RayleighP99;
        var random = new Random(seed);
        var tipped = 0;
        for (var i = 0; i < samples; i++)
        {
            var pick = random.NextDouble() * total;
            var slope = slopes[^1].SlopeDeg;
            foreach (var item in slopes)
            {
                pick -= Math.Max(0, item.Weight);
                if (pick <= 0)
                {
                    slope = item.SlopeDeg;
                    break;
                }
            }

            var heading = random.NextDouble() * 360;
            var yaw = random.NextDouble() * 360;
            var speed = sigma * Math.Sqrt(-2 * Math.Log(1 - random.NextDouble()));
            if (speed >= TipSpeed(stance, slope, heading, yaw))
            {
                tipped++;
            }
        }

        return (double)tipped / samples;
    }
}
