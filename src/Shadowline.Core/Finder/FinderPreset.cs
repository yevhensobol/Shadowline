using Shadowline.Core.Missions;

namespace Shadowline.Core.Finder;

public sealed record FinderPreset(
    string Key,
    int DurationDays,
    double MinEarthShare,
    TargetKind Target,
    CriteriaWeights Weights,
    string LanderKey,
    double PanelW,
    double LoadW,
    double BatteryWh,
    string? StanceId)
{
    public const double MaxSlopeDeg = 8;

    public const double RangeKm = 5;

    public const int FlexDays = 15;

    public const int MinZoneCells = 1;

    public const string Custom = "custom";

    public const string NovaC = "novac";

    public const string Hypothetical = "hypothetical";

    public static FinderPreset Water { get; } = new("water", 14, 0.5, TargetKind.ColdTrap, new(80, 70, 90, 40, 0), NovaC, 788, 80, 1554, "nova-c");

    public static FinderPreset Solar { get; } = new("solar", 28, 0.5, TargetKind.None, new(100, 60, 0, 50, 0), Hypothetical, 400, 150, 40000, null);

    public static FinderPreset Clps { get; } = new("clps", 10, 0.75, TargetKind.None, new(70, 90, 20, 60, 0), NovaC, 788, 80, 1554, "nova-c");

    public static FinderPreset Relay { get; } = new("relay", 28, 0.75, TargetKind.None, new(60, 100, 0, 40, 0), Hypothetical, 400, 150, 40000, null);

    public static IReadOnlyList<FinderPreset> All { get; } = [Water, Solar, Clps, Relay];

    public static FinderPreset Get(string key) => All.FirstOrDefault(preset => preset.Key == key) ?? Water;

    public static FinderPreset? Match(Mission mission)
    {
        ArgumentNullException.ThrowIfNull(mission);
        return All.FirstOrDefault(preset => preset.Fits(mission));
    }

    public bool Fits(Mission mission) =>
        mission.DurationDays == DurationDays
        && Math.Abs(mission.BatteryWh - BatteryWh) < 1
        && Math.Abs(mission.MinEarthShare - MinEarthShare) < 1e-6
        && Math.Abs(mission.PanelW - PanelW) < 1e-6
        && Math.Abs(mission.LoadW - LoadW) < 1e-6;

    public Mission Apply(Mission mission)
    {
        ArgumentNullException.ThrowIfNull(mission);
        return mission with
        {
            PanelW = PanelW,
            LoadW = LoadW,
            BatteryWh = BatteryWh,
            DurationDays = DurationDays,
            MinEarthShare = MinEarthShare,
            SafeLoadW = Math.Min(mission.SafeLoadW, LoadW),
            StanceId = StanceId ?? mission.StanceId,
        };
    }

    public static MissionProfile Profile(Mission mission, FinderPreset? preset, DateTimeOffset landingDay)
    {
        ArgumentNullException.ThrowIfNull(mission);
        var weights = (preset ?? Water).Weights;
        var target = (preset ?? Water).Target;
        return new MissionProfile(
            new DateTimeOffset(landingDay.UtcDateTime.Date.AddHours(12), TimeSpan.Zero),
            FlexDays,
            Math.Clamp(mission.DurationDays, 1, 60),
            new LanderSpec(Math.Max(0, mission.PanelW), Math.Max(0, mission.LoadW), Math.Max(0, mission.BatteryWh)),
            MaxSlopeDeg,
            mission.MinEarthShare,
            target,
            null,
            null,
            RangeKm,
            weights.Clamp());
    }
}
