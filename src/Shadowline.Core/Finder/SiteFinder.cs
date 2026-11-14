namespace Shadowline.Core.Finder;

[Flags]
public enum FinderFail
{
    None = 0,
    Domain = 1,
    Slope = 2,
    Darkness = 4,
    Sun = 8,
    Earth = 16,
    Battery = 32,
}

public readonly record struct CellUtility(double Sun, double Earth, double Water, double Slope, double Target);

public readonly record struct WindowStats(double SunShare, double EarthShare, double LongestDarkHours, int StartStep, double MinCharge = 1);

public sealed record FinderZone(
    int Rank,
    int BestCell,
    IReadOnlyList<int> Cells,
    double AreaKm2,
    double MeanScore,
    double MinScore,
    double MaxScore,
    double LatitudeDeg,
    double LongitudeEastDeg,
    WindowStats Window,
    double WaterKm,
    double SlopeHighDeg);

public sealed record FilterCount(string Key, double Value, int Zones, double AreaKm2);

public sealed record MissPoint(double RadiusKm, double PassShare, double MeanScore);

public sealed record FinderResult(
    MissionProfile Profile,
    FinderFail[] Fail,
    double[] Score,
    WindowStats[] Windows,
    CellUtility[] Utilities,
    IReadOnlyList<FinderZone> Zones,
    int[] ZoneOf,
    int MinZoneCells)
{
    public bool Passes(int cell) => Fail[cell] == FinderFail.None;

    public int PassingCells => Fail.Count(fail => fail == FinderFail.None);
}

public static class SiteFinder
{
    public const double ZoneScore = 0.5;

    public static readonly double[] SlopeOptions = [5, 8, 12];

    public static readonly double[] EarthOptions = [0.25, 0.5, 0.75];

    public static FinderResult Evaluate(FinderGrid grid, MissionProfile profile, int minZoneCells)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(profile);
        var count = grid.Count;
        var fail = new FinderFail[count];
        var score = new double[count];
        var windows = new WindowStats[count];
        var utilities = new CellUtility[count];
        var weights = profile.Weights.Clamp();
        var target = TargetCell(grid, profile);
        var (first, last, length) = StartRange(grid, profile);

        for (var cell = 0; cell < count; cell++)
        {
            if (!grid.InDomain(cell))
            {
                fail[cell] = FinderFail.Domain;
                continue;
            }

            var window = BestWindow(grid, cell, first, last, length, profile, weights);
            windows[cell] = window;
            var utility = Utility(grid, cell, window, profile, target);
            utilities[cell] = utility;
            score[cell] = Score(utility, weights);
            fail[cell] = Check(grid, cell, window, profile, profile.MaxSlopeDeg, profile.MinEarthShare);
        }

        var (zones, zoneOf) = Zones(grid, fail, score, windows, minZoneCells);
        return new FinderResult(profile, fail, score, windows, utilities, zones, zoneOf, minZoneCells);
    }

    public static (int First, int Last, int Length) StartRange(FinderGrid grid, MissionProfile profile)
    {
        var perDay = grid.StepsPerDay;
        var length = Math.Max(1, profile.DurationDays * perDay);
        var center = grid.StepAt(profile.LandingUtc);
        var flex = Math.Max(0, profile.FlexDays) * perDay;
        var first = Math.Clamp(center - flex, 0, Math.Max(0, grid.Steps - length));
        var last = Math.Clamp(center + flex, first, Math.Max(0, grid.Steps - length));
        return (first, last, length);
    }

    public static WindowStats Window(FinderGrid grid, int cell, int start, int length, LanderSpec? lander = null)
    {
        var sun = 0;
        var earth = 0;
        var run = 0;
        var longest = 0;
        for (var step = start; step < start + length; step++)
        {
            if (grid.Sun(cell, step))
            {
                sun++;
                run = 0;
            }
            else
            {
                run++;
                longest = Math.Max(longest, run);
            }

            if (grid.Earth(cell, step))
            {
                earth++;
            }
        }

        return new WindowStats((double)sun / length, (double)earth / length, longest * grid.Step.TotalHours, start, lander is null ? 1 : MinCharge(grid, cell, start, length, lander));
    }

    public static double MinCharge(FinderGrid grid, int cell, int start, int length, LanderSpec lander)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(lander);
        var capacity = lander.BatteryWh * LanderSpec.UsableFraction;
        var hours = grid.Step.TotalHours;
        var energy = capacity;
        var lowest = capacity;
        for (var step = start; step < start + length; step++)
        {
            var net = (grid.Sun(cell, step) ? lander.PanelW : 0) - lander.LoadW;
            energy += net > 0 ? net * hours * LanderSpec.ChargeEfficiency : net * hours / LanderSpec.DischargeEfficiency;
            if (energy < 0)
            {
                return 0;
            }

            energy = Math.Min(energy, capacity);
            lowest = Math.Min(lowest, energy);
        }

        return capacity <= 0 ? 1 : lowest / capacity;
    }

    private static WindowStats BestWindow(FinderGrid grid, int cell, int first, int last, int length, MissionProfile profile, CriteriaWeights weights)
    {
        var span = last - first + length;
        var sunPrefix = new int[span + 1];
        var earthPrefix = new int[span + 1];
        var darkRuns = new List<(int Start, int End)>();
        var runStart = -1;
        for (var i = 0; i < span; i++)
        {
            var lit = grid.Sun(cell, first + i);
            sunPrefix[i + 1] = sunPrefix[i] + (lit ? 1 : 0);
            earthPrefix[i + 1] = earthPrefix[i] + (grid.Earth(cell, first + i) ? 1 : 0);
            if (!lit && runStart < 0)
            {
                runStart = i;
            }
            else if (lit && runStart >= 0)
            {
                darkRuns.Add((runStart, i));
                runStart = -1;
            }
        }

        if (runStart >= 0)
        {
            darkRuns.Add((runStart, span));
        }

        var stride = Math.Max(1, grid.StepsPerDay / 2);
        WindowStats best = default;
        var bestKey = double.NegativeInfinity;
        for (var offset = 0; offset <= last - first; offset += stride)
        {
            var end = offset + length;
            var sun = (double)(sunPrefix[end] - sunPrefix[offset]) / length;
            var earth = (double)(earthPrefix[end] - earthPrefix[offset]) / length;
            var longest = 0;
            foreach (var (s, e) in darkRuns)
            {
                var overlap = Math.Min(e, end) - Math.Max(s, offset);
                longest = Math.Max(longest, overlap);
            }

            var stats = new WindowStats(sun, earth, longest * grid.Step.TotalHours, first + offset);
            var ok = stats.LongestDarkHours <= profile.MaxDarkHours && stats.SunShare >= profile.RequiredSunShare && stats.EarthShare >= profile.MinEarthShare;
            if (ok)
            {
                stats = stats with { MinCharge = MinCharge(grid, cell, first + offset, length, profile.Lander) };
                ok = stats.MinCharge > 0;
            }

            var key = (ok ? 10.0 : 0.0) + (weights.Sun * sun + weights.Earth * earth + 1e-3 * sun) / Math.Max(1e-9, weights.Sun + weights.Earth + 1e-3);
            if (key > bestKey)
            {
                bestKey = key;
                best = stats;
            }
        }

        return best.LongestDarkHours <= profile.MaxDarkHours && best.SunShare >= profile.RequiredSunShare
            ? best with { MinCharge = MinCharge(grid, cell, best.StartStep, length, profile.Lander) }
            : best;
    }

    private static int? TargetCell(FinderGrid grid, MissionProfile profile) =>
        profile.Target == TargetKind.Point && profile.TargetLatitudeDeg is { } lat && profile.TargetLongitudeDeg is { } lon
            ? grid.CellAt(lat, lon)
            : null;

    public static CellUtility Utility(FinderGrid grid, int cell, WindowStats window, MissionProfile profile, int? target)
    {
        var required = profile.RequiredSunShare;
        var sun = required >= 1 ? (window.SunShare >= 1 ? 1 : 0) : Math.Clamp((window.SunShare - required) / (1 - required), 0, 1);
        var range = Math.Max(0.1, profile.RangeKm);
        var water = profile.Target == TargetKind.ColdTrap || profile.Weights.Water > 0
            ? Math.Clamp(1 - grid.WaterDistanceKm(cell) / range, 0, 1)
            : 0;
        var targetUtility = target is { } t ? Math.Clamp(1 - grid.DistanceKm(cell, t) / range, 0, 1) : 0;
        var slope = profile.MaxSlopeDeg <= 0 ? 0 : Math.Clamp(1 - grid.SlopeHighDeg(cell) / profile.MaxSlopeDeg, 0, 1);
        return new CellUtility(sun, window.EarthShare, water, slope, targetUtility);
    }

    public static double Score(CellUtility u, CriteriaWeights w)
    {
        var total = w.Total;
        if (total <= 0)
        {
            return 1;
        }

        return (w.Sun * u.Sun + w.Earth * u.Earth + w.Water * u.Water + w.Slope * u.Slope + w.Target * u.Target) / total;
    }

    public static FinderFail Check(FinderGrid grid, int cell, WindowStats window, MissionProfile profile, double maxSlope, double minEarth)
    {
        var fail = FinderFail.None;
        if (!grid.InDomain(cell))
        {
            return FinderFail.Domain;
        }

        if (grid.SlopeHighDeg(cell) > maxSlope)
        {
            fail |= FinderFail.Slope;
        }

        if (window.LongestDarkHours > profile.MaxDarkHours)
        {
            fail |= FinderFail.Darkness;
        }

        if (window.SunShare < profile.RequiredSunShare)
        {
            fail |= FinderFail.Sun;
        }

        if (window.EarthShare < minEarth)
        {
            fail |= FinderFail.Earth;
        }

        if (window.MinCharge <= 0 && !fail.HasFlag(FinderFail.Darkness) && !fail.HasFlag(FinderFail.Sun))
        {
            fail |= FinderFail.Battery;
        }

        return fail;
    }

    public static (IReadOnlyList<FinderZone> Zones, int[] ZoneOf) Zones(FinderGrid grid, FinderFail[] fail, double[] score, WindowStats[] windows, int minZoneCells)
    {
        var n = grid.Size;
        var zoneOf = new int[grid.Count];
        Array.Fill(zoneOf, -1);
        var raw = new List<List<int>>();
        var queue = new Queue<int>();
        for (var cell = 0; cell < grid.Count; cell++)
        {
            if (zoneOf[cell] != -1 || fail[cell] != FinderFail.None || score[cell] < ZoneScore)
            {
                continue;
            }

            var members = new List<int>();
            var id = raw.Count;
            zoneOf[cell] = id;
            queue.Enqueue(cell);
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                members.Add(current);
                var row = current / n;
                var col = current % n;
                for (var dr = -1; dr <= 1; dr++)
                {
                    for (var dc = -1; dc <= 1; dc++)
                    {
                        var r = row + dr;
                        var c = col + dc;
                        if ((dr == 0 && dc == 0) || r < 0 || c < 0 || r >= n || c >= n)
                        {
                            continue;
                        }

                        var next = r * n + c;
                        if (zoneOf[next] == -1 && fail[next] == FinderFail.None && score[next] >= ZoneScore)
                        {
                            zoneOf[next] = id;
                            queue.Enqueue(next);
                        }
                    }
                }
            }

            raw.Add(members);
        }

        var cellArea = grid.CellKm * grid.CellKm;
        var kept = raw
            .Select((members, id) => (members, id))
            .Where(item => item.members.Count >= Math.Max(1, minZoneCells))
            .Select(item =>
            {
                var best = item.members.MaxBy(cell => score[cell]);
                var (lat, lon) = grid.LatLon(best);
                return (item.id, Zone: new FinderZone(0, best, item.members, item.members.Count * cellArea,
                    item.members.Average(cell => score[cell]), item.members.Min(cell => score[cell]), score[best],
                    lat, lon, windows[best], grid.WaterDistanceKm(best), grid.SlopeHighDeg(best)));
            })
            .OrderByDescending(item => item.Zone.MaxScore + 0.02 * Math.Log(1 + item.Zone.AreaKm2))
            .ToList();

        var remap = new Dictionary<int, int>();
        var zones = new List<FinderZone>(kept.Count);
        for (var i = 0; i < kept.Count; i++)
        {
            remap[kept[i].id] = i;
            zones.Add(kept[i].Zone with { Rank = i + 1 });
        }

        for (var cell = 0; cell < zoneOf.Length; cell++)
        {
            zoneOf[cell] = zoneOf[cell] >= 0 && remap.TryGetValue(zoneOf[cell], out var index) ? index : -1;
        }

        return (zones, zoneOf);
    }

    public static IReadOnlyList<FilterCount> SlopeCounts(FinderGrid grid, FinderResult result) =>
        SlopeOptions.Select(value => Count(grid, result, "slope", value, value, result.Profile.MinEarthShare)).ToList();

    public static IReadOnlyList<FilterCount> EarthCounts(FinderGrid grid, FinderResult result) =>
        EarthOptions.Select(value => Count(grid, result, "earth", value, result.Profile.MaxSlopeDeg, value)).ToList();

    private static FilterCount Count(FinderGrid grid, FinderResult result, string key, double value, double maxSlope, double minEarth)
    {
        var fail = new FinderFail[grid.Count];
        for (var cell = 0; cell < grid.Count; cell++)
        {
            fail[cell] = Check(grid, cell, result.Windows[cell], result.Profile, maxSlope, minEarth);
        }

        var (zones, _) = Zones(grid, fail, result.Score, result.Windows, result.MinZoneCells);
        return new FilterCount(key, value, zones.Count, zones.Sum(zone => zone.AreaKm2));
    }

    public static IReadOnlyList<MissPoint> MissCurve(FinderGrid grid, FinderResult result, int center, double maxKm = 10)
    {
        var points = new List<MissPoint>();
        var (cx, cy) = grid.CellCenter(center);
        var reach = (int)Math.Ceiling(maxKm * 1000 / grid.CellM) + 1;
        var row0 = center / grid.Size;
        var col0 = center % grid.Size;
        for (var radius = 0.0; radius <= maxKm + 1e-9; radius += grid.CellKm / 2)
        {
            var total = 0;
            var pass = 0;
            var sum = 0.0;
            for (var r = Math.Max(0, row0 - reach); r <= Math.Min(grid.Size - 1, row0 + reach); r++)
            {
                for (var c = Math.Max(0, col0 - reach); c <= Math.Min(grid.Size - 1, col0 + reach); c++)
                {
                    var cell = r * grid.Size + c;
                    var (x, y) = grid.CellCenter(cell);
                    var d = Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) / 1000.0;
                    if (d > radius + 1e-9 || !grid.InDomain(cell))
                    {
                        continue;
                    }

                    total++;
                    if (result.Passes(cell))
                    {
                        pass++;
                        sum += result.Score[cell];
                    }
                }
            }

            points.Add(new MissPoint(radius, total == 0 ? 0 : (double)pass / total, total == 0 ? 0 : sum / total));
        }

        return points;
    }

    public static IReadOnlyList<(DateTimeOffset Start, double Score, bool Pass)> DateCurve(FinderGrid grid, FinderResult result, int cell, int strideDays = 2)
    {
        var profile = result.Profile;
        var length = Math.Max(1, profile.DurationDays * grid.StepsPerDay);
        var target = TargetCell(grid, profile);
        var weights = profile.Weights.Clamp();
        var output = new List<(DateTimeOffset, double, bool)>();
        for (var start = 0; start + length <= grid.Steps; start += strideDays * grid.StepsPerDay)
        {
            var window = Window(grid, cell, start, length, profile.Lander);
            var utility = Utility(grid, cell, window, profile, target);
            var pass = Check(grid, cell, window, profile, profile.MaxSlopeDeg, profile.MinEarthShare) == FinderFail.None;
            output.Add((grid.At(start), Score(utility, weights), pass));
        }

        return output;
    }

    public static IReadOnlyDictionary<int, double> Stability(FinderResult result, int samples = 400, int top = 5, int seed = 7)
    {
        var random = new Random(seed);
        var hits = new int[result.Zones.Count];
        var w = result.Profile.Weights.Clamp();
        if (result.Zones.Count == 0)
        {
            return new Dictionary<int, double>();
        }

        for (var s = 0; s < samples; s++)
        {
            var jitter = new CriteriaWeights(w.Sun * Factor(random), w.Earth * Factor(random), w.Water * Factor(random), w.Slope * Factor(random), w.Target * Factor(random));
            var order = result.Zones
                .Select((zone, index) => (index, best: zone.Cells.Max(cell => Score(result.Utilities[cell], jitter))))
                .OrderByDescending(item => item.best)
                .Take(top);
            foreach (var (index, _) in order)
            {
                hits[index]++;
            }
        }

        return Enumerable.Range(0, hits.Length).ToDictionary(index => index, index => (double)hits[index] / samples);
    }

    private static double Factor(Random random)
    {
        var u1 = 1.0 - random.NextDouble();
        var u2 = random.NextDouble();
        var normal = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
        return Math.Exp(0.5 * normal);
    }
}
