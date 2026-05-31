using System.Globalization;

namespace ScanGo.Api.Common;

/// <summary>
/// Free-tier quota periods = ISO weeks in Asia/Ho_Chi_Minh. The week rolls over
/// Monday 00:00 VN (= "Sunday midnight"), which is the reset boundary — so quota
/// resets automatically by period key, no cron required.
/// </summary>
public static class QuotaPeriod
{
    private static readonly TimeZoneInfo Vn = ResolveVnTimeZone();

    private static TimeZoneInfo ResolveVnTimeZone()
    {
        foreach (var id in new[] { "Asia/Ho_Chi_Minh", "SE Asia Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
        }
        return TimeZoneInfo.CreateCustomTimeZone("VN", TimeSpan.FromHours(7), "VN", "VN");
    }

    public static (string Key, DateTime StartUtc, DateTime EndUtc) CurrentWeek(DateTime utcNow)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(utcNow, Vn);
        var daysSinceMonday = ((int)local.DayOfWeek + 6) % 7;        // Mon=0 .. Sun=6
        var weekStartLocal = local.Date.AddDays(-daysSinceMonday);   // Monday 00:00 local
        var weekEndLocal = weekStartLocal.AddDays(7);
        var key = $"{ISOWeek.GetYear(local)}-W{ISOWeek.GetWeekOfYear(local):D2}";
        return (
            key,
            TimeZoneInfo.ConvertTimeToUtc(weekStartLocal, Vn),
            TimeZoneInfo.ConvertTimeToUtc(weekEndLocal, Vn));
    }
}
