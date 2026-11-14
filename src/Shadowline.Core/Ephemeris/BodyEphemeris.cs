using Shadowline.Core.Geometry;
using Shadowline.Core.Time;

namespace Shadowline.Core.Ephemeris;

public sealed class BodyEphemeris
{
    public const int FloatsPerRow = 6;

    private readonly float[] _values;

    public BodyEphemeris(TimeGrid grid, float[] values)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(values);
        if (values.Length != grid.Count * FloatsPerRow)
        {
            throw new ArgumentException(
                $"Expected {grid.Count * FloatsPerRow} values for {grid.Count} rows, got {values.Length}",
                nameof(values));
        }

        Grid = grid;
        _values = values;
    }

    public TimeGrid Grid { get; }

    public Vec3 Sun(int index)
    {
        var offset = Offset(index);
        return new Vec3(_values[offset], _values[offset + 1], _values[offset + 2]);
    }

    public Vec3 Earth(int index)
    {
        var offset = Offset(index);
        return new Vec3(_values[offset + 3], _values[offset + 4], _values[offset + 5]);
    }

    private int Offset(int index)
    {
        if ((uint)index >= (uint)Grid.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        return index * FloatsPerRow;
    }
}
