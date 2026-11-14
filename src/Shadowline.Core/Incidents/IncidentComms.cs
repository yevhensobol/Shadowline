using Shadowline.Core.Comms;
using Shadowline.Core.Ephemeris;
using Shadowline.Core.Geometry;
using Shadowline.Core.Horizon;

namespace Shadowline.Core.Incidents;

public sealed record ContactCheck(LanderObservation Observation, double MaxElevationDeg, double VisibleShare, string? BestStation);

public sealed record CommsSummary(double[] StationElevationDeg, double[] EarthElevationDeg, double[] EarthHorizonDeg, double BaseRate, IReadOnlyList<ContactCheck> Contacts, double ChanceAllByLuck);

public static class IncidentComms
{
    public const string JaxaSource = "https://ddor.nav.isas.jaxa.jp/station_coordinates";

    public static IReadOnlyList<GroundStation> Jaxa { get; } =
    [
        new("misasa", "Misasa 54 m", 36.140984502, 138.35212867, 1.655993),
        new("usuda", "Usuda 64 m", 36.132403748, 138.362773862, 1.5328122),
        new("uchinoura", "Uchinoura 34 m", 31.25430804, 131.078392333, 0.3757827),
    ];

    public static CommsSummary Analyze(SiteFrame frame, BodyEphemeris ephemeris, HorizonProfile horizon, IReadOnlyList<GroundStation> stations, IReadOnlyList<LanderObservation> observations, DateTimeOffset fromUtc, DateTimeOffset toUtc)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(ephemeris);
        ArgumentNullException.ThrowIfNull(horizon);
        ArgumentNullException.ThrowIfNull(stations);
        ArgumentNullException.ThrowIfNull(observations);
        var n = ephemeris.Grid.Count;
        var station = new double[n];
        var best = new int[n];
        var earth = new double[n];
        var earthHorizon = new double[n];
        for (var i = 0; i < n; i++)
        {
            var moon = DeepSpaceNetwork.MoonFromEarthFixed(ephemeris.Earth(i), ephemeris.Grid.At(i));
            station[i] = double.NegativeInfinity;
            for (var s = 0; s < stations.Count; s++)
            {
                var e = DeepSpaceNetwork.ElevationDeg(stations[s], moon);
                if (e > station[i])
                {
                    station[i] = e;
                    best[i] = s;
                }
            }

            var sky = frame.Observe(ephemeris.Earth(i));
            earth[i] = sky.ElevationDeg;
            earthHorizon[i] = horizon.ElevationAt(sky.AzimuthDeg);
        }

        bool Up(int i) => station[i] >= DeepSpaceNetwork.MinElevationDeg && earth[i] > earthHorizon[i];
        var from = Math.Clamp(ephemeris.Grid.IndexOf(fromUtc), 0, n - 1);
        var to = Math.Clamp(ephemeris.Grid.IndexOf(toUtc), from, n - 1);
        var baseRate = Enumerable.Range(from, to - from + 1).Average(i => Up(i) ? 1.0 : 0.0);
        var contacts = new List<ContactCheck>();
        foreach (var o in observations.Where(o => o.Kind == ObservationKind.Power))
        {
            var a = Math.Clamp(ephemeris.Grid.IndexOf(o.FromUtc), 0, n - 1);
            var b = Math.Clamp(ephemeris.Grid.IndexOf(o.ToUtc), a, n - 1);
            var range = Enumerable.Range(a, b - a + 1).ToList();
            var top = range.MaxBy(i => station[i]);
            contacts.Add(new ContactCheck(o, station[top], range.Average(i => Up(i) ? 1.0 : 0.0), stations[best[top]].Name));
        }

        var chance = contacts.Count == 0 ? double.NaN : 1.0;
        foreach (var c in contacts)
        {
            var length = Math.Clamp(ephemeris.Grid.IndexOf(c.Observation.ToUtc), 0, n - 1) - Math.Clamp(ephemeris.Grid.IndexOf(c.Observation.FromUtc), 0, n - 1);
            var starts = Math.Max(1, to - from - length + 1);
            var hit = 0;
            for (var s = from; s < from + starts; s++)
            {
                for (var i = s; i <= Math.Min(s + length, to); i++)
                {
                    if (Up(i))
                    {
                        hit++;
                        break;
                    }
                }
            }

            chance *= (double)hit / starts;
        }
        return new CommsSummary(station, earth, earthHorizon, baseRate, contacts, chance);
    }
}
