using System.Globalization;
using Shadowline.Core.Geometry;
using Shadowline.Core.Horizon;
using Shadowline.Core.Sites;
using Shadowline.Core.Thermal;
using Shadowline.Data;

if (args.Length < 4)
{
    Console.Error.WriteLine("usage: RegolithCheck <data-dir> <cells.bin> <out.csv> <first> [count] [slopes.bin]");
    return 1;
}

var dataDir = args[0];
await using var manifestStream = File.OpenRead(Path.Combine(dataDir, "manifest.json"));
var manifest = await ManifestReader.ReadAsync(manifestStream);
var ephemeris = EphemerisReader.Read(await File.ReadAllBytesAsync(Path.Combine(dataDir, "ephemeris.bin")), manifest.Grid);
var bytes = await File.ReadAllBytesAsync(args[1]);
var total = BitConverter.ToInt32(bytes, 0);
var first = int.Parse(args[3], CultureInfo.InvariantCulture);
var count = args.Length > 4 ? int.Parse(args[4], CultureInfo.InvariantCulture) : total - first;
var record = (3 + HorizonProfile.SampleCount) * 4;
var slopes = args.Length > 5 ? await File.ReadAllBytesAsync(args[5]) : null;
using var writer = new StreamWriter(args[2], append: first > 0);
if (first == 0)
{
    writer.WriteLine("index,lat,lon,max_k,mean2cm_k,min_k,mean_k,simple_max_k,slope_deg,aspect_deg,flat_max_k,flat_mean2cm_k");
}

for (var c = first; c < Math.Min(total, first + count); c++)
{
    var offset = 4 + c * record;
    var lat = BitConverter.ToSingle(bytes, offset);
    var lon = BitConverter.ToSingle(bytes, offset + 4);
    var elevation = BitConverter.ToSingle(bytes, offset + 8);
    var horizon = new float[HorizonProfile.SampleCount];
    Buffer.BlockCopy(bytes, offset + 12, horizon, 0, horizon.Length * 4);
    var site = new Site("c" + c, "cell", null, lat, lon, elevation);
    var frame = new SiteFrame(site);
    var profile = new HorizonProfile(horizon);
    var slope = slopes is null ? 0 : BitConverter.ToSingle(slopes, c * 8);
    var aspect = slopes is null ? 0 : BitConverter.ToSingle(slopes, c * 8 + 4);
    var result = Regolith1D.Run(frame, ephemeris, profile, 0, manifest.Grid.Count, null, 4, 2, slope, aspect);
    var flat = slopes is null ? result : Regolith1D.Run(frame, ephemeris, profile, 0, manifest.Grid.Count);
    var simple = ThermalModel.Evaluate(frame, ephemeris, profile, manifest.Grid, 0, manifest.Grid.Count, lat).DayMaxK;
    writer.WriteLine(string.Join(',', c, lat.ToString("F5", CultureInfo.InvariantCulture), lon.ToString("F5", CultureInfo.InvariantCulture), F(result.MaxK), F(result.Mean2cmK), F(result.MinK), F(result.MeanK), F(simple), F(slope), F(aspect), F(flat.MaxK), F(flat.Mean2cmK)));
}

return 0;

static string F(double value) => value.ToString("F2", CultureInfo.InvariantCulture);
