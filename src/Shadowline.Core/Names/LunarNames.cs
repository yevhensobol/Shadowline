using System.Text.Json;

namespace Shadowline.Core.Names;

public sealed record LunarFeature(string Name, double LatitudeDeg, double LongitudeEastDeg, double DiameterKm, string Type);

public sealed class LunarNames
{
    public const double RadiusKm = 1737.4;

    private static readonly HashSet<string> AreaTypes =
    [
        "crater", "satellite feature", "mare", "lacus", "sinus", "palus", "oceanus", "planitia", "mons", "terra",
    ];

    private readonly LunarFeature[] features;
    private readonly double[] x;
    private readonly double[] y;
    private readonly double[] z;
    private readonly double[] cosRadius;

    private LunarNames(IReadOnlyList<LunarFeature> list)
    {
        features = list.Where(feature => feature.DiameterKm > 0 && AreaTypes.Contains(feature.Type)).ToArray();
        x = new double[features.Length];
        y = new double[features.Length];
        z = new double[features.Length];
        cosRadius = new double[features.Length];
        for (var index = 0; index < features.Length; index++)
        {
            (x[index], y[index], z[index]) = Unit(features[index].LatitudeDeg, features[index].LongitudeEastDeg);
            cosRadius[index] = Math.Cos(Math.Min(Math.PI, features[index].DiameterKm / 2 / RadiusKm));
        }
    }

    public int Count => features.Length;

    public static LunarNames Parse(Stream stream)
    {
        using var document = JsonDocument.Parse(stream);
        var root = document.RootElement;
        var types = root.GetProperty("types").EnumerateArray().Select(item => item.GetString() ?? string.Empty).ToArray();
        var list = new List<LunarFeature>();
        foreach (var item in root.GetProperty("features").EnumerateArray())
        {
            list.Add(new LunarFeature(
                item[0].GetString() ?? string.Empty,
                item[1].GetDouble(),
                item[2].GetDouble(),
                item[3].GetDouble(),
                types[item[4].GetInt32()]));
        }

        return new LunarNames(list);
    }

    public LunarFeature? Containing(double latitudeDeg, double longitudeEastDeg)
    {
        var (px, py, pz) = Unit(latitudeDeg, longitudeEastDeg);
        LunarFeature? best = null;
        for (var index = 0; index < features.Length; index++)
        {
            if (px * x[index] + py * y[index] + pz * z[index] >= cosRadius[index]
                && (best is null || features[index].DiameterKm < best.DiameterKm))
            {
                best = features[index];
            }
        }

        return best;
    }

    private static (double X, double Y, double Z) Unit(double latitudeDeg, double longitudeEastDeg)
    {
        var lat = latitudeDeg * Math.PI / 180;
        var lon = longitudeEastDeg * Math.PI / 180;
        return (Math.Cos(lat) * Math.Cos(lon), Math.Cos(lat) * Math.Sin(lon), Math.Sin(lat));
    }
}
