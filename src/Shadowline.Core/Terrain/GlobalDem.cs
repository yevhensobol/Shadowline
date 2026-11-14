namespace Shadowline.Core.Terrain;

public readonly record struct DemTileKey(int Row, int Col)
{
    public string FileName => $"r{Row}c{Col}.bin";
}

public sealed class GlobalDem
{
    public const int TileDeg = 15;
    public const int PixelsPerDeg = 16;
    public const int TileSize = TileDeg * PixelsPerDeg;
    public const int Rows = 180 / TileDeg;
    public const int Cols = 360 / TileDeg;
    public const int Height = 180 * PixelsPerDeg;
    public const int Width = 360 * PixelsPerDeg;

    private readonly Func<DemTileKey, short[]?> _tile;

    public GlobalDem(Func<DemTileKey, short[]?> tile)
    {
        _tile = tile;
    }

    public static IReadOnlySet<DemTileKey> TilesAround(double latDeg, double lonDeg, double rangeM)
    {
        var spanDeg = rangeM / LocalTerrain.MoonRadiusM * 180.0 / Math.PI + 0.5;
        var south = Math.Max(-90.0, latDeg - spanDeg);
        var north = Math.Min(90.0, latDeg + spanDeg);
        var maxAbs = Math.Max(Math.Abs(south), Math.Abs(north));
        var lonSpan = maxAbs > 80 ? 180.0 : Math.Min(180.0, spanDeg / Math.Cos(maxAbs * Math.PI / 180.0));
        var keys = new HashSet<DemTileKey>();
        var rowTop = Math.Clamp((int)Math.Floor((90.0 - north) / TileDeg), 0, Rows - 1);
        var rowBottom = Math.Clamp((int)Math.Floor((90.0 - south) / TileDeg), 0, Rows - 1);
        for (var row = rowTop; row <= rowBottom; row++)
        {
            if (lonSpan >= 180.0)
            {
                for (var col = 0; col < Cols; col++)
                {
                    keys.Add(new DemTileKey(row, col));
                }

                continue;
            }

            var west = lonDeg - lonSpan;
            var east = lonDeg + lonSpan;
            for (var lon = Math.Floor(west / TileDeg) * TileDeg; lon <= east; lon += TileDeg)
            {
                var col = (int)Math.Floor((Wrap(lon) + 180.0) / TileDeg) % Cols;
                keys.Add(new DemTileKey(row, col));
            }
        }

        return keys;
    }

    public double Sample(double latDeg, double lonDeg)
    {
        var col = (Wrap(lonDeg) + 180.0) * PixelsPerDeg - 0.5;
        var row = Math.Clamp((90.0 - latDeg) * PixelsPerDeg - 0.5, 0, Height - 1.000001);
        var c0 = (int)Math.Floor(col);
        var r0 = (int)Math.Floor(row);
        var fc = col - c0;
        var fr = row - r0;
        var a = Pixel(r0, c0);
        var b = Pixel(r0, c0 + 1);
        var c = Pixel(r0 + 1, c0);
        var d = Pixel(r0 + 1, c0 + 1);
        return (a * (1 - fc) + b * fc) * (1 - fr) + (c * (1 - fc) + d * fc) * fr;
    }

    private double Pixel(int row, int col)
    {
        row = Math.Clamp(row, 0, Height - 1);
        col = ((col % Width) + Width) % Width;
        var tile = _tile(new DemTileKey(row / TileSize, col / TileSize));
        return tile is null ? 0.0 : tile[(row % TileSize) * TileSize + col % TileSize];
    }

    private static double Wrap(double lonDeg) => ((lonDeg + 180.0) % 360.0 + 360.0) % 360.0 - 180.0;
}
