using System.Globalization;
using System.IO;
using System.Text;
using Monity.App.Helpers;
using Monity.Infrastructure.Persistence;

namespace Monity.App.Services;

public class ReportExportService : IReportExportService
{
    private readonly IUsageRepository _repository;

    public ReportExportService(IUsageRepository repository)
    {
        _repository = repository;
    }

    public async Task ExportStatisticsToCsvAsync(
        DateTime startDate,
        DateTime endDate,
        string? categoryName,
        string filePath,
        CancellationToken ct = default)
    {
        var ignoredStr = await _repository.GetSettingAsync("ignored_processes") ?? "";
        var excluded = ignoredStr
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        var totalTask = _repository.GetRangeTotalAsync(startDate, endDate, excludeIdle: true, excludedProcessNames: excluded, categoryName: categoryName);
        var appsTask = _repository.GetWeeklyUsageAsync(startDate, endDate, excludeIdle: true, excludedProcessNames: excluded, categoryName: categoryName);
        var dailyTask = _repository.GetDailyTotalsInRangeAsync(startDate, endDate, excludeIdle: true, excludedProcessNames: excluded, categoryName: categoryName);

        await Task.WhenAll(totalTask, appsTask, dailyTask);

        var total = await totalTask;
        var apps = await appsTask;
        var dailyTotals = await dailyTask;

        var dayCount = Math.Max(1, (endDate.Date - startDate.Date).Days + 1);

        using var writer = new StreamWriter(filePath, false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        // Summary section
        await writer.WriteLineAsync("Monity - " + (Strings.Get("Stats_Title") ?? "Statistics") + " - " + startDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + " - " + endDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        await writer.WriteLineAsync();
        await writer.WriteLineAsync("Section;Label;Value");
        await writer.WriteLineAsync("Summary;DateRange;" + startDate.ToString("d", CultureInfo.InvariantCulture) + " - " + endDate.ToString("d", CultureInfo.InvariantCulture));
        await writer.WriteLineAsync("Summary;TotalDuration;" + DurationAndPeriodHelper.FormatDuration(total.TotalSeconds));
        await writer.WriteLineAsync("Summary;SessionCount;" + total.SessionCount);
        var avgSeconds = dayCount > 0 ? total.TotalSeconds / dayCount : 0L;
        await writer.WriteLineAsync("Summary;DailyAverage;" + DurationAndPeriodHelper.FormatDuration(avgSeconds));
        if (!string.IsNullOrEmpty(categoryName))
            await writer.WriteLineAsync("Summary;Category;" + EscapeCsv(categoryName));
        await writer.WriteLineAsync();

        // Applications
        await writer.WriteLineAsync("Application;TotalSeconds;TotalFormatted;AverageSeconds;AverageFormatted;Percent");
        foreach (var a in apps)
        {
            var appAvg = dayCount > 0 ? a.TotalSeconds / dayCount : 0L;
            var pct = total.TotalSeconds > 0 ? (double)a.TotalSeconds / total.TotalSeconds * 100 : 0;
            var name = a.DisplayName ?? a.ProcessName;
            await writer.WriteLineAsync(
                EscapeCsv(name) + ";" +
                a.TotalSeconds + ";" +
                EscapeCsv(DurationAndPeriodHelper.FormatDuration(a.TotalSeconds)) + ";" +
                appAvg + ";" +
                EscapeCsv(DurationAndPeriodHelper.FormatDuration(appAvg)) + ";" +
                pct.ToString("F2", CultureInfo.InvariantCulture));
        }
        await writer.WriteLineAsync();

        // Daily breakdown
        await writer.WriteLineAsync("Date;TotalSeconds;TotalFormatted");
        foreach (var d in dailyTotals)
            await writer.WriteLineAsync(EscapeCsv(d.Date) + ";" + d.TotalSeconds + ";" + EscapeCsv(DurationAndPeriodHelper.FormatDuration(d.TotalSeconds)));
    }

    private static string EscapeCsv(string value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        if (value.Contains(';') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        return value;
    }
}
