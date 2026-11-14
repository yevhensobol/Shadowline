namespace Shadowline.Core.Finder;

public enum TargetKind
{
    None,
    ColdTrap,
    Point,
}

public sealed record CriteriaWeights(double Sun, double Earth, double Water, double Slope, double Target)
{
    public static CriteriaWeights Default { get; } = new(80, 70, 90, 40, 0);

    public double Total => Sun + Earth + Water + Slope + Target;

    public CriteriaWeights Clamp() => new(Bound(Sun), Bound(Earth), Bound(Water), Bound(Slope), Bound(Target));

    private static double Bound(double value) => double.IsFinite(value) ? Math.Clamp(value, 0, 100) : 0;
}

public sealed record LanderSpec(double PanelW, double LoadW, double BatteryWh)
{
    public const double UsableFraction = 0.8;

    public const double DischargeEfficiency = 0.95;

    public const double ChargeEfficiency = 0.95;

    public static LanderSpec Default { get; } = new(400, 150, 10000);

    public double MaxDarkHours => LoadW <= 0 ? double.PositiveInfinity : BatteryWh * UsableFraction * DischargeEfficiency / LoadW;

    public double RequiredSunShare => PanelW <= 0 ? 1.0 : Math.Clamp(LoadW / PanelW, 0, 1);
}

public sealed record MissionProfile(
    DateTimeOffset LandingUtc,
    int FlexDays,
    int DurationDays,
    LanderSpec Lander,
    double MaxSlopeDeg,
    double MinEarthShare,
    TargetKind Target,
    double? TargetLatitudeDeg,
    double? TargetLongitudeDeg,
    double RangeKm,
    CriteriaWeights Weights)
{
    public double MaxDarkHours => Lander.MaxDarkHours;

    public double RequiredSunShare => Lander.RequiredSunShare;
}
