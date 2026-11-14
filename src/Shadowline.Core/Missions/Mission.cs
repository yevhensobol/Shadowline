using System.Globalization;
using Shadowline.Core.Finder;
using Shadowline.Core.Incidents;
using LanderSpec = Shadowline.Core.Finder.LanderSpec;
using Shadowline.Core.Replan;
using Shadowline.Core.Windows;
using Shadowline.Core.Power;

namespace Shadowline.Core.Missions;

public sealed record Mission(
    double PanelW,
    double LoadW,
    double BatteryWh,
    int DurationDays,
    double MinEarthShare,
    double SafeLoadW = 100,
    double MaxEarthGapHours = 168,
    double MinLandingSunDeg = 0.5,
    string StanceId = "nova-c",
    double LateralSpeedMs = 1.0)
{
    public const double CellEfficiency = 0.30;

    public static Mission Default { get; } = new(400, 150, 10000, 14, 0.5);

    public LanderSpec Lander => new(PanelW, LoadW, BatteryWh);

    public LanderStance Stance => LanderStance.Find(StanceId);

    public double MaxDarkHours => Lander.MaxDarkHours;

    public double RequiredSunShare => Lander.RequiredSunShare;

    public double SafeModeHours => SafeLoadW <= 0 ? double.PositiveInfinity : BatteryWh * LanderSpec.UsableFraction * LanderSpec.DischargeEfficiency / SafeLoadW;

    public double PanelAreaM2 => PanelW / (PowerModel.SolarConstantW * CellEfficiency * PowerModel.SystemFactorFixedEfficiency);

    public TimeSpan Duration => TimeSpan.FromDays(DurationDays);

    public Mission Clamp() => new(
        Bound(PanelW, 10, 20000),
        Bound(LoadW, 1, 10000),
        Bound(BatteryWh, 0, 1_000_000),
        Math.Clamp(DurationDays, 1, 60),
        Bound(MinEarthShare, 0, 1),
        Bound(SafeLoadW, 1, 10000),
        Bound(MaxEarthGapHours, 1, 24 * 60),
        Bound(MinLandingSunDeg, -5, 30),
        LanderStance.Find(StanceId).Id,
        Bound(LateralSpeedMs, 0, 5));

    public MissionRequirements Requirements() => new(
        Duration,
        RequiredSunShare,
        double.IsFinite(MaxDarkHours) ? TimeSpan.FromHours(MaxDarkHours) : TimeSpan.MaxValue,
        MinEarthShare,
        TimeSpan.FromHours(MaxEarthGapHours));

    public WindowCriteria Criteria(TimeSpan gridStep) => new(
        Math.Max(1, (int)Math.Round(Duration / gridStep)),
        RequiredSunShare,
        MinEarthShare,
        MinLandingSunDeg);

    public TipOptions TipOptions(TipPrior prior = TipPrior.Uniform) => new(
        PanelAreaM2,
        CellEfficiency,
        PowerModel.SystemFactorFixedEfficiency,
        BatteryWh,
        1.0,
        1.0 - LanderSpec.UsableFraction,
        SafeLoadW,
        LanderSpec.DischargeEfficiency,
        prior);

    public MissionProfile Profile(DateTimeOffset landingUtc, int flexDays, double maxSlopeDeg, TargetKind target, double? targetLat, double? targetLon, double rangeKm, CriteriaWeights weights) =>
        new(landingUtc, flexDays, DurationDays, Lander, maxSlopeDeg, MinEarthShare, target, targetLat, targetLon, rangeKm, weights);

    public string ToQueryValue() => string.Join(
        ',',
        Format(PanelW),
        Format(LoadW),
        Format(BatteryWh / 1000),
        DurationDays.ToString(CultureInfo.InvariantCulture),
        Format(MinEarthShare * 100),
        Format(SafeLoadW),
        Stance.Id,
        Format(LateralSpeedMs));

    public static Mission? ParseQueryValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var parts = value.Split(',');
        if (parts.Length is not (5 or 6 or 8))
        {
            return null;
        }

        var count = Math.Min(parts.Length, 6);
        var numbers = new double[count];
        for (var i = 0; i < count; i++)
        {
            if (!double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out numbers[i]) || !double.IsFinite(numbers[i]))
            {
                return null;
            }
        }

        var safe = numbers.Length == 6 ? numbers[5] : Default.SafeLoadW;
        var stance = Default.StanceId;
        var lateral = Default.LateralSpeedMs;
        if (parts.Length == 8)
        {
            if (LanderStance.Presets.All(item => item.Id != parts[6]) || !double.TryParse(parts[7], NumberStyles.Float, CultureInfo.InvariantCulture, out lateral) || !double.IsFinite(lateral))
            {
                return null;
            }

            stance = parts[6];
        }

        return new Mission(numbers[0], numbers[1], numbers[2] * 1000, (int)Math.Round(numbers[3]), numbers[4] / 100, safe, StanceId: stance, LateralSpeedMs: lateral).Clamp();
    }

    private static double Bound(double value, double min, double max) => double.IsFinite(value) ? Math.Clamp(value, min, max) : min;

    private static string Format(double value) => Math.Round(value, 3).ToString("0.###", CultureInfo.InvariantCulture);
}
