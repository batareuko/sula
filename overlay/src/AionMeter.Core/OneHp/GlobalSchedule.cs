namespace AionMeter.Core.OneHp;

/// <summary>
/// 1 HP: fixed Global schedule. The Spacetime Rift opens every 3 hours from 02:00 server time (see
/// <see cref="EventSchedule"/>) and stays open 10 minutes; daily reset at 07:00 UTC, weekly reset on Wednesday at 07:00 UTC.
/// </summary>
public static class GlobalSchedule
{
    public static readonly TimeSpan RiftEvery = TimeSpan.FromHours(3);
    public static readonly TimeSpan RiftOpen = TimeSpan.FromMinutes(10);
    public const int ResetUtcHour = 7;
    public const DayOfWeek WeeklyResetDay = DayOfWeek.Wednesday;

    /// <summary>Open = the portal is open now and closes at <see cref="Closes"/>; otherwise it opens at <see cref="Opens"/>.</summary>
    public readonly record struct RiftState(bool IsOpen, DateTimeOffset Opens, DateTimeOffset Closes);

    public static RiftState Rift(DateTimeOffset now, int serverId = 0)
    {
        var opens = EventSchedule.Rift.Next(now, includeRunning: true, serverId: serverId);
        return new RiftState(opens <= now, opens, opens + RiftOpen);
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
