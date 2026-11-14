namespace Shadowline.Core.Missions;

public sealed record LandingMiss(string Key, string Name, double DistanceM, bool UpperBound, string ReferenceId);

public static class LandingRecords
{
    public static IReadOnlyList<LandingMiss> Misses { get; } =
    [
        new("slim", "SLIM", 55, false, "jaxa2024"),
        new("blueghost1", "Blue Ghost M1", 100, true, "firefly2025"),
        new("apollo12", "Apollo 12", 164, false, "nasa-apollo12"),
        new("im2", "IM-2", 250, false, "im2025"),
        new("im1", "IM-1", 1500, false, "spo2024"),
        new("apollo11", "Apollo 11", 6400, false, "nasa-apollo11"),
    ];

    public static IReadOnlyList<LandingMiss> Within(double radiusM) => Misses.Where(item => item.DistanceM <= radiusM).ToList();
}
