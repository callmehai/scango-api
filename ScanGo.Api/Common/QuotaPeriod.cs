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

    /// <summary>Length of one rolling quota period.</summary>
    public static readonly TimeSpan RollingLength = TimeSpan.FromDays(7);

    /// <summary>
    /// Rolling per-user quota period: a fixed 7-day window anchored at
    /// <paramref name="anchorUtc"/> (the user's signup time). Period N covers
    /// [anchor + 7N, anchor + 7(N+1)); quota resets the moment one window ends,
    /// so every user gets a full 7 days from when they joined — not a shared
    /// calendar week. Key "r7-0042" = the 43rd 7-day window since signup.
    /// No cron needed: the key changes automatically when the window rolls over.
    /// </summary>
    public static (string Key, DateTime StartUtc, DateTime EndUtc) CurrentRolling(
        DateTime utcNow, DateTime anchorUtc)
    {
        anchorUtc = DateTime.SpecifyKind(anchorUtc, DateTimeKind.Utc);
        var elapsed = utcNow - anchorUtc;
        var index = elapsed <= TimeSpan.Zero
            ? 0L
            : elapsed.Ticks / RollingLength.Ticks;

        var startUtc = anchorUtc.AddTicks(index * RollingLength.Ticks);
        var endUtc = startUtc.Add(RollingLength);
        return ($"r7-{index:D4}", startUtc, endUtc);
    }
}
