namespace AionMeter.Core.OneHp;

/// <summary>
/// 1 HP: recurring Global events (UTC), as questlog.gg lists them for the Global client (2026-10): the Spacetime Rift
/// every 3 h from 00:00 (entry open 10 min), the Shugo Festival every hour (10 min), Watcher Kaira in Lower Reshanta
/// every 3 h from 01:00, the Artifact Siege on Monday, Thursday and Saturday at 12:00 with the siege bosses at 12:30,
/// Guardian Lord Nahma on Friday and Sunday at 12:00, and the daily / weekly (Wednesday) resets at 07:00.
/// </summary>
public static class EventSchedule
{
    public sealed record GameEvent(string Id, int[] TimesUtcMinutes, DayOfWeek[]? Days = null, int DurationMinutes = 0)
    {
        /// <summary>The next start at or after <paramref name="now"/>, or the current one if it is still on.</summary>
        public DateTimeOffset Next(DateTimeOffset now, bool includeRunning = false)
        {
            now = now.ToUniversalTime();
            var day = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero);
            for (var d = -1; d <= 8; d++)
            {
                var date = day.AddDays(d);
                if (Days is not null && !Days.Contains(date.DayOfWeek)) continue;
                foreach (var m in TimesUtcMinutes)
                {
                    var start = date.AddMinutes(m);
                    if (start >= now) return start;
                    if (includeRunning && DurationMinutes > 0 && now < start.AddMinutes(DurationMinutes)) return start;
                }
            }
            return DateTimeOffset.MaxValue;
        }
    }

    private static int[] Every(int startMinute, int stepMinutes)
    {
        var list = new List<int>();
        for (var m = startMinute; m < 1440; m += stepMinutes) list.Add(m);
        return list.ToArray();
    }

    public static readonly GameEvent Rift = new("rift", Every(0, 180), DurationMinutes: 10);
    public static readonly GameEvent Shugo = new("shugo", Every(0, 60), DurationMinutes: 10);
    public static readonly GameEvent Kaira = new("kaira", Every(60, 180));
    public static readonly GameEvent Siege = new("siege", [12 * 60], [DayOfWeek.Monday, DayOfWeek.Thursday, DayOfWeek.Saturday], 30);
    public static readonly GameEvent SiegeBosses = new("siege-bosses", [12 * 60 + 30], [DayOfWeek.Monday, DayOfWeek.Thursday, DayOfWeek.Saturday], 30);
    public static readonly GameEvent Nahma = new("nahma", [12 * 60], [DayOfWeek.Friday, DayOfWeek.Sunday], 30);
    public static readonly GameEvent DailyReset = new("daily-reset", [7 * 60]);
    public static readonly GameEvent WeeklyReset = new("weekly-reset", [7 * 60], [DayOfWeek.Wednesday]);

    public static readonly IReadOnlyList<GameEvent> All = [Rift, Shugo, Kaira, Siege, SiegeBosses, Nahma, DailyReset, WeeklyReset];

    /// <summary>
    /// Events of <paramref name="enabled"/> that start within <paramref name="leadMinutes"/> of <paramref name="now"/>
    /// and were not announced yet (<paramref name="announced"/> remembers start times per event).
    /// </summary>
    public static List<(GameEvent Event, DateTimeOffset Start)> Due(DateTimeOffset now, IEnumerable<string> enabled,
        Func<string, int> leadMinutes, IDictionary<string, DateTimeOffset> announced)
    {
        var due = new List<(GameEvent, DateTimeOffset)>();
        foreach (var e in All.Where(e => enabled.Contains(e.Id)))
        {
            var start = e.Next(now);
            if (start == DateTimeOffset.MaxValue || start - now > TimeSpan.FromMinutes(leadMinutes(e.Id))) continue;
            if (announced.TryGetValue(e.Id, out var last) && last == start) continue;
            announced[e.Id] = start;
            due.Add((e, start));
        }
        return due;
    }
}
