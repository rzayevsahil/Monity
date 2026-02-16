namespace Monity.App.Helpers;

/// <summary>
/// Süre formatlama ve dönem (günlük/haftalık/aylık/yıllık) aralığı hesabı.
/// </summary>
public static class DurationAndPeriodHelper
{
    public enum PeriodKind
    {
        Daily,
        Weekly,
        Monthly,
        Yearly
    }

    public static string FormatDuration(long seconds)
    {
        var h = seconds / 3600;
        var m = (seconds % 3600) / 60;
        if (h > 0)
            return $"{h} sa {m} dk";
        if (m > 0)
            return $"{m} dk";
        return $"{seconds} sn";
    }

    /// <summary>
    /// Seçilen dönem ve tarihe göre başlangıç, bitiş ve gün sayısını döndürür.
    /// Haftalık: ISO haftası (Pazartesi–Pazar).
    /// </summary>
    public static (DateTime Start, DateTime End, int DayCount) GetPeriodRange(PeriodKind period, DateTime date)
    {
        switch (period)
        {
            case PeriodKind.Daily:
                var d = date.Date;
                return (d, d, 1);
            case PeriodKind.Weekly:
                var diff = (7 + (date.DayOfWeek - DayOfWeek.Monday)) % 7;
                var monday = date.Date.AddDays(-diff);
                var sunday = monday.AddDays(6);
                return (monday, sunday, 7);
            case PeriodKind.Monthly:
                var first = new DateTime(date.Year, date.Month, 1);
                var last = first.AddMonths(1).AddDays(-1);
                return (first, last, (last - first).Days + 1);
            case PeriodKind.Yearly:
                var yFirst = new DateTime(date.Year, 1, 1);
                var yLast = new DateTime(date.Year, 12, 31);
                return (yFirst, yLast, (yLast - yFirst).Days + 1);
            default:
                return (date.Date, date.Date, 1);
        }
    }
}
