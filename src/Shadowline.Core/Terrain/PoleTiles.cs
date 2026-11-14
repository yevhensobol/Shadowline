namespace Shadowline.Core.Terrain;

public sealed record PoleTileKey(int Row, int Col)
{
    public string FileName => $"r{Row}c{Col}.bin.gz";
}

public sealed class PoleGrid
{
    public PoleGrid(int sizePx, double spacingM, double halfExtentM, int tilePx, double unitM, IEnumerable<PoleTileKey> present)
    {
        if (sizePx < 2 || spacingM <= 0 || tilePx < 2 || unitM <= 0 || Math.Abs(sizePx * spacingM - 2 * halfExtentM) > 1e-3)
        {
            throw new ArgumentException("Inconsistent polar tile grid");
        }

        SizePx = sizePx;
        SpacingM = spacingM;
        HalfExtentM = halfExtentM;
        TilePx = tilePx;
        UnitM = unitM;
        Present = present.ToHashSet();
    }

    public int SizePx { get; }

    public double SpacingM { get; }

    public double HalfExtentM { get; }

    public int TilePx { get; }

    public double UnitM { get; }

    public IReadOnlySet<PoleTileKey> Present { get; }

    public bool Contains(double x, double y) => Math.Abs(x) < HalfExtentM - SpacingM && Math.Abs(y) < HalfExtentM - SpacingM;

    public (double Col, double Row) Pixel(double x, double y) => ((x + HalfExtentM) / SpacingM - 0.5, (HalfExtentM - y) / SpacingM - 0.5);

    public IReadOnlyList<PoleTileKey> TilesAround(double x, double y, double radiusM)
    {
        var (col, row) = Pixel(x, y);
        var reach = radiusM / SpacingM + 1;
        var c0 = Math.Max(0, (int)Math.Floor((col - reach) / TilePx));
        var c1 = Math.Min((SizePx - 1) / TilePx, (int)Math.Floor((col + reach) / TilePx));
        var r0 = Math.Max(0, (int)Math.Floor((row - reach) / TilePx));
        var r1 = Math.Min((SizePx - 1) / TilePx, (int)Math.Floor((row + reach) / TilePx));
        var keys = new List<PoleTileKey>();
        for (var r = r0; r <= r1; r++)
        {
            for (var c = c0; c <= c1; c++)
            {
                var key = new PoleTileKey(r, c);
                if (Present.Contains(key))
                {
                    keys.Add(key);
                }
            }
        }

        return keys;
    }

    public static short[] Decode(ReadOnlySpan<byte> raw, int rows, int cols)
    {
        if (raw.Length != rows * cols * 2)
        {
            throw new ArgumentException("Tile size does not match", nameof(raw));
        }

        var values = new short[rows * cols];
        for (var r = 0; r < rows; r++)
        {
            for (var c = 0; c < cols; c++)
            {
                var i = r * cols + c;
                var d = (short)(raw[2 * i] | raw[2 * i + 1] << 8);
                var left = c > 0 ? values[i - 1] : 0;
                var up = r > 0 ? values[i - cols] : 0;
                var diagonal = r > 0 && c > 0 ? values[i - cols - 1] : 0;
                values[i] = unchecked((short)(d + left + up - diagonal));
            }
        }

        return values;
    }

    public PoleWindow Window(double x, double y, double radiusM, Func<PoleTileKey, short[]?> tile)
    {
        var (col, row) = Pixel(x, y);
        var reach = (int)Math.Ceiling(radiusM / SpacingM) + 2;
        var c0 = Math.Max(0, (int)Math.Floor(col) - reach);
        var r0 = Math.Max(0, (int)Math.Floor(row) - reach);
        var c1 = Math.Min(SizePx - 1, (int)Math.Floor(col) + reach + 1);
        var r1 = Math.Min(SizePx - 1, (int)Math.Floor(row) + reach + 1);
        var width = c1 - c0 + 1;
        var height = r1 - r0 + 1;
        var data = new short[width * height];
        var valid = new bool[width * height];
        for (var tr = r0 / TilePx; tr <= r1 / TilePx; tr++)
        {
            for (var tc = c0 / TilePx; tc <= c1 / TilePx; tc++)
            {
                var values = tile(new PoleTileKey(tr, tc));
                if (values is null)
                {
                    continue;
                }

                var rs = Math.Max(r0, tr * TilePx);
                var re = Math.Min(r1, tr * TilePx + TilePx - 1);
                var cs = Math.Max(c0, tc * TilePx);
                var ce = Math.Min(c1, tc * TilePx + TilePx - 1);
                for (var r = rs; r <= re; r++)
                {
                    var source = (r - tr * TilePx) * TilePx + (cs - tc * TilePx);
                    var target = (r - r0) * width + (cs - c0);
                    Array.Copy(values, source, data, target, ce - cs + 1);
                    Array.Fill(valid, true, target, ce - cs + 1);
                }
            }
        }

        return new PoleWindow(this, r0, c0, width, height, data, valid);
    }
}

public sealed class PoleWindow
{
    private readonly PoleGrid _grid;
    private readonly int _row0;
    private readonly int _col0;
    private readonly short[] _data;
    private readonly bool[] _valid;

    internal PoleWindow(PoleGrid grid, int row0, int col0, int width, int height, short[] data, bool[] valid)
    {
        _grid = grid;
        _row0 = row0;
        _col0 = col0;
        Width = width;
        Height = height;
        _data = data;
        _valid = valid;
    }

    public int Width { get; }

    public int Height { get; }

    public double SpacingM => _grid.SpacingM;

    public double? Sample(double x, double y)
    {
        var (col, row) = _grid.Pixel(x, y);
        col -= _col0;
        row -= _row0;
        if (col < 0 || row < 0 || col > Width - 1.000001 || row > Height - 1.000001)
        {
            return null;
        }

        var c0 = (int)col;
        var r0 = (int)row;
        var i = r0 * Width + c0;
        if (!_valid[i] || !_valid[i + 1] || !_valid[i + Width] || !_valid[i + Width + 1])
        {
            return null;
        }

        var fc = col - c0;
        var fr = row - r0;
        var top = _data[i] * (1 - fc) + _data[i + 1] * fc;
        var bottom = _data[i + Width] * (1 - fc) + _data[i + Width + 1] * fc;
        return (top * (1 - fr) + bottom * fr) * _grid.UnitM;
    }
}
