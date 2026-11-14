namespace Shadowline.Core.Finder;

public static class PoleNow
{
    public static byte[]? Build(FinderGrid grid, DateTimeOffset moment)
    {
        ArgumentNullException.ThrowIfNull(grid);
        var step = grid.StepAt(moment);
        if (step < 0 || step >= grid.Steps)
        {
            return null;
        }

        var data = new byte[grid.Count * 3];
        for (var cell = 0; cell < grid.Count; cell++)
        {
            if (!grid.InDomain(cell))
            {
                continue;
            }

            data[cell * 3] = grid.Sun(cell, step) ? (byte)255 : (byte)0;
            data[cell * 3 + 1] = grid.Earth(cell, step) ? (byte)255 : (byte)0;
            data[cell * 3 + 2] = 255;
        }

        return data;
    }
}
