using Shadowline.Core.Ephemeris;
using Shadowline.Core.Geometry;
using Shadowline.Core.Horizon;
using Shadowline.Core.Power;
using Shadowline.Core.Sites;
using Shadowline.Core.Thermal;
using Shadowline.Core.Time;
using Shadowline.Core.Tracks;

namespace Shadowline.Web.Services;

public sealed class SiteView
{
    private readonly SiteFrame _frame;
    private readonly BodyEphemeris _ephemeris;

    public SiteView(Site site, BodyEphemeris ephemeris, HorizonProfile horizon, bool hasTerrain = false)
    {
        Site = site;
        Horizon = horizon;
        HasTerrain = hasTerrain;
        _frame = new SiteFrame(site);
        _ephemeris = ephemeris;
        Tracks = SkyTracks.Build(site, ephemeris, horizon);
    }

    public Site Site { get; }

    public HorizonProfile Horizon { get; }

    public bool HasTerrain { get; }

    public SkyTracks Tracks { get; }

    public SkySnapshot At(int index) => SkySnapshot.At(_frame, _ephemeris, index);

    public PowerEstimate Power(TimeGrid grid, int start, int length) =>
        PowerModel.Evaluate(_frame, _ephemeris, Horizon, grid, start, length);

    private RegolithResult? _regolith;

    public RegolithResult Regolith => _regolith ??= Regolith1D.Run(_frame, _ephemeris, Horizon, 0, _ephemeris.Grid.Count);

    public ThermalEstimate Thermal(TimeGrid grid, int start, int length)
    {
        ArgumentNullException.ThrowIfNull(grid);
        var series = Regolith.SurfaceK;
        var first = Math.Clamp(start, 0, series.Count - 1);
        var last = Math.Clamp(start + length, first + 1, series.Count);
        double max = double.MinValue, min = double.MaxValue;
        for (var i = first; i < last; i++)
        {
            max = Math.Max(max, series[i]);
            min = Math.Min(min, series[i]);
        }

        return new ThermalEstimate(series[first], max, min);
    }
}
