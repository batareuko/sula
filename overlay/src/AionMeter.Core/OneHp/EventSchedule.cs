namespace AionMeter.Core.OneHp;

/// <summary>
/// 1 HP: recurring Global events, on the clock of the server's region (Europe: Berlin time, summer time included —
/// the region comes from the server id: 1x1x North America east, 1x2x west, 1x3x Europe, 1x4x South America, 1x5x
/// Asia). Server time: the Spacetime Rift at 02, 05, …, 23 (entry open 10 min), the Shugo Festival every hour (10 min),
/// the invasion every hour at :30, Watcher Kaira in Lower Reshanta at 01, 04, …, 22, the Arena of Strategy 11–14 and
/// 19–21, the Artifact Siege on Monday, Thursday and Saturday at 21:00 with its bosses at 21:30, Guardian Lord Nahma on
/// Friday and Sunday at 21:00. The daily / weekly (Wednesday) resets are at 07:00 UTC.
/// </summary>
public static class EventSchedule
{
    /// <param name="ServerTime">Times are the server region's wall clock (false: UTC).</param>
    public sealed record GameEvent(string Id, int[] TimesMinutes, DayOfWeek[]? Days = null, int DurationMinutes = 0, bool ServerTime = true)
    {
        /// <summary>The next start at or after <paramref name="now"/>, or the current one if it is still on.</summary>
        public DateTimeOffset Next(DateTimeOffset now, bool includeRunning = false, int serverId = 0)
        {
            var zone = ServerTime ? ZoneOf(serverId) : TimeZoneInfo.Utc;
            var today = TimeZoneInfo.ConvertTime(now, zone).Date;
            for (var d = -1; d <= 8; d++)
            {
                var date = today.AddDays(d);
                if (Days is not null && !Days.Contains(date.DayOfWeek)) continue;
                foreach (var m in TimesMinutes)
                {
                    if (At(zone, date.AddMinutes(m)) is not { } start) continue;
                    if (start >= now) return start;
                    if (includeRunning && DurationMinutes > 0 && now < start.AddMinutes(DurationMinutes)) return start;
                }
            }
            return DateTimeOffset.MaxValue;
        }
    }

    /// <summary>A wall-clock time in <paramref name="zone"/>; none in the hour skipped when the clocks go forward.</summary>
    private static DateTimeOffset? At(TimeZoneInfo zone, DateTime wall)
    {
        wall = DateTime.SpecifyKind(wall, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(wall)) return null;
        var offset = zone.IsAmbiguousTime(wall) ? zone.GetAmbiguousTimeOffsets(wall).Max() : zone.GetUtcOffset(wall);
        return new DateTimeOffset(wall, offset);
    }

    private static readonly Dictionary<int, TimeZoneInfo> Zones = new();

    /// <summary>The time zone of a server's region; Europe when the server is not known yet (the 1 HP guild's).</summary>
    public static TimeZoneInfo ZoneOf(int serverId)
    {
        var region = serverId is >= 1000 and < 3000 ? serverId / 100 % 10 : 3;
        lock (Zones)
        {
            if (Zones.TryGetValue(region, out var z)) return z;
            var (iana, windows) = region switch
            {
                1 => ("America/New_York", "Eastern Standard Time"),
                2 => ("America/Los_Angeles", "Pacific Standard Time"),
                4 => ("America/Sao_Paulo", "E. South America Standard Time"),
                5 => ("Asia/Seoul", "Korea Standard Time"),
                _ => ("Europe/Berlin", "W. Europe Standard Time"),
            };
            if (!TimeZoneInfo.TryFindSystemTimeZoneById(iana, out z) && !TimeZoneInfo.TryFindSystemTimeZoneById(windows, out z))
                z = TimeZoneInfo.Utc;
            return Zones[region] = z;
        }
    }

    private static int[] Every(int startMinute, int stepMinutes)
    {
        var list = new List<int>();
        for (var m = startMinute; m < 1440; m += stepMinutes) list.Add(m);
        return list.ToArray();
    }

    private static readonly DayOfWeek[] SiegeDays = [DayOfWeek.Monday, DayOfWeek.Thursday, DayOfWeek.Saturday];

    public static readonly GameEvent Rift = new("rift", Every(2 * 60, 180), DurationMinutes: 10);
    public static readonly GameEvent Shugo = new("shugo", Every(0, 60), DurationMinutes: 10);
    public static readonly GameEvent Invasion = new("invasion", Every(30, 60));
    public static readonly GameEvent Kaira = new("kaira", Every(60, 180));
    public static readonly GameEvent Arena = new("arena", [11 * 60, 19 * 60], DurationMinutes: 120);
    public static readonly GameEvent Siege = new("siege", [21 * 60], SiegeDays, 30);
    public static readonly GameEvent SiegeBosses = new("siege-bosses", [21 * 60 + 30], SiegeDays, 30);
    public static readonly GameEvent Nahma = new("nahma", [21 * 60], [DayOfWeek.Friday, DayOfWeek.Sunday], 30);
    public static readonly GameEvent DailyReset = new("daily-reset", [7 * 60], ServerTime: false);
    public static readonly GameEvent WeeklyReset = new("weekly-reset", [7 * 60], [DayOfWeek.Wednesday], ServerTime: false);

    public static readonly IReadOnlyList<GameEvent> All = [Rift, Shugo, Invasion, Kaira, Arena, Siege, SiegeBosses, Nahma, DailyReset, WeeklyReset];

    /// <summary>
    /// Events of <paramref name="enabled"/> that start within <paramref name="leadMinutes"/> of <paramref name="now"/>
    /// and were not announced yet (<paramref name="announced"/> remembers start times per event).
    /// </summary>
    public static List<(GameEvent Event, DateTimeOffset Start)> Due(DateTimeOffset now, IEnumerable<string> enabled,
        Func<string, int> leadMinutes, IDictionary<string, DateTimeOffset> announced, int serverId = 0)
    {
        var due = new List<(GameEvent, DateTimeOffset)>();
        foreach (var e in All.Where(e => enabled.Contains(e.Id)))
        {
            var start = e.Next(now, serverId: serverId);
            if (start == DateTimeOffset.MaxValue || start - now > TimeSpan.FromMinutes(leadMinutes(e.Id))) continue;
            if (announced.TryGetValue(e.Id, out var last) && last == start) continue;
            announced[e.Id] = start;
            due.Add((e, start));
        }
        return due;
    }
}
