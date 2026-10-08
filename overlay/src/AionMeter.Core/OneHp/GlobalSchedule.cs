namespace AionMeter.Core.OneHp;

/// <summary>
/// 1 HP: fixed Global schedule (same as guide.sulaslova.com). The Spacetime Rift opens every 3 hours from 00:00 UTC
/// and stays open 10 minutes (player-reported); daily reset at 07:00 UTC, weekly reset on Wednesday at 07:00 UTC.
/// </summary>
public static class GlobalSchedule
{
    public static readonly TimeSpan RiftEvery = TimeSpan.FromHours(3);
    public static readonly TimeSpan RiftOpen = TimeSpan.FromMinutes(10);
    public const int ResetUtcHour = 7;
    public const DayOfWeek WeeklyResetDay = DayOfWeek.Wednesday;

    /// <summary>Open = the portal is open now and closes at <see cref="Closes"/>; otherwise it opens at <see cref="Opens"/>.</summary>
    public readonly record struct RiftState(bool IsOpen, DateTimeOffset Opens, DateTimeOffset Closes);

    public static RiftState Rift(DateTimeOffset now)
    {
        now = now.ToUniversalTime();
        var midnight = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero);
        var last = midnight + RiftEvery * Math.Floor((now - midnight) / RiftEvery);
        if (now < last + RiftOpen) return new RiftState(true, last, last + RiftOpen);
        var next = last + RiftEvery;
        return new RiftState(false, next, next + RiftOpen);
    }

    public static DateTimeOffset NextDailyReset(DateTimeOffset now)
    {
        now = now.ToUniversalTime();
        var today = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero).AddHours(ResetUtcHour);
        return now < today ? today : today.AddDays(1);
    }

    public static DateTimeOffset NextWeeklyReset(DateTimeOffset now)
    {
        now = now.ToUniversalTime();
        var day = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero).AddHours(ResetUtcHour);
        var ahead = ((int)WeeklyResetDay - (int)day.DayOfWeek + 7) % 7;
        var reset = day.AddDays(ahead);
        return now < reset ? reset : reset.AddDays(7);
    }
}
