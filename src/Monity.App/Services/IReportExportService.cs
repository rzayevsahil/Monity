namespace Monity.App.Services;

public interface IReportExportService
{
    /// <summary>
    /// Exports statistics for the given range to CSV (UTF-8 with BOM).
    /// Uses same filters as Statistics: excluded processes and optional category.
    /// </summary>
    Task ExportStatisticsToCsvAsync(
        DateTime startDate,
        DateTime endDate,
        string? categoryName,
        string filePath,
        CancellationToken ct = default);
}
