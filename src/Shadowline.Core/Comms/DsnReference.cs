namespace Shadowline.Core.Comms;

public sealed record DsnReferenceCase(DateTimeOffset TimeUtc, string StationId, double ElevationDeg);

public static class DsnReference
{
    public const string Source = "JPL Horizons";

    public static IReadOnlyList<DsnReferenceCase> Cases { get; } = Build();

    private static List<DsnReferenceCase> Build()
    {
        var rows = new (int Hour, double Goldstone, double Madrid, double Canberra)[]
        {
            (0, 6.734503, -72.807779, 58.193884),
            (6, -60.358944, -23.922546, 42.662680),
            (12, -43.388404, 22.236472, -19.338903),
            (18, 17.645618, -6.247388, -15.110437),
        };

        var list = new List<DsnReferenceCase>();
        foreach (var (hour, goldstone, madrid, canberra) in rows)
        {
            var time = new DateTimeOffset(2026, 11, 10, hour, 0, 0, TimeSpan.Zero);
            list.Add(new(time, "goldstone", goldstone));
            list.Add(new(time, "madrid", madrid));
            list.Add(new(time, "canberra", canberra));
        }

        return list;
    }
}
