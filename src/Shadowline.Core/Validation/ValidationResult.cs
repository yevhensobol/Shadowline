namespace Shadowline.Core.Validation;

public sealed record ValidationRow(
    ValidationFixture Fixture,
    double ComputedAzimuthDeg,
    double ComputedElevationDeg,
    double AzimuthErrorDeg,
    double ElevationErrorDeg);

public sealed record ValidationSummary(int Count, double MaxAzimuthErrorDeg, double MaxElevationErrorDeg, double MeanElevationErrorDeg);

public sealed record ValidationResult(IReadOnlyList<ValidationRow> Rows, int Skipped)
{
    public ValidationSummary Summary()
    {
        if (Rows.Count == 0)
        {
            return new ValidationSummary(0, 0, 0, 0);
        }

        return new ValidationSummary(
            Rows.Count,
            Rows.Max(row => Math.Abs(row.AzimuthErrorDeg)),
            Rows.Max(row => Math.Abs(row.ElevationErrorDeg)),
            Rows.Average(row => Math.Abs(row.ElevationErrorDeg)));
    }
}
