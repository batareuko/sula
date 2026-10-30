using System.Text.Json;

namespace AionMeter.Core.OneHp;

/// <summary>
/// 1 HP: the daily and weekly checklist (data/checklist.json): what to do, how many times, and what the game itself
/// tells — a ticket's count (<see cref="Task.Resource"/>) or this week's runs of a dungeon (<see cref="Task.Runs"/>).
/// Daily tasks reset at 07:00 UTC, weekly ones on Wednesday at 07:00 UTC (AION 2 Global).
/// </summary>
public sealed class Checklist
{
    public const int ResetHourUtc = 7;
    public const DayOfWeek WeeklyResetDay = DayOfWeek.Wednesday;

    /// <param name="Names">By language code (en, uk, ru).</param>
    public sealed record Task(string Id, bool Weekly, int Max, IReadOnlyDictionary<string, string> Names,
        IReadOnlyDictionary<string, string> Hints, int Resource, string? Runs)
    {
        public string Name(string lang) => Names.GetValueOrDefault(lang) ?? Names.GetValueOrDefault("en") ?? Id;
        public string? Hint(string lang) => Hints.GetValueOrDefault(lang) ?? Hints.GetValueOrDefault("en");
    }

    public IReadOnlyList<Task> Daily { get; }
    public IReadOnlyList<Task> Weekly { get; }

    public Checklist(IReadOnlyList<Task> daily, IReadOnlyList<Task> weekly)
    {
        Daily = daily;
        Weekly = weekly;
    }

    /// <summary>When the current day (or week) of tasks began.</summary>
    public static DateTimeOffset PeriodStart(bool weekly, DateTimeOffset now)
    {
        var utc = now.ToUniversalTime();
        var day = new DateTimeOffset(utc.Year, utc.Month, utc.Day, ResetHourUtc, 0, 0, TimeSpan.Zero);
        if (utc < day) day = day.AddDays(-1);
        if (!weekly) return day;
        while (day.DayOfWeek != WeeklyResetDay) day = day.AddDays(-1);
        return day;
    }

    /// <summary>When the next reset comes.</summary>
    public static DateTimeOffset NextReset(bool weekly, DateTimeOffset now) => PeriodStart(weekly, now).AddDays(weekly ? 7 : 1);

    /// <summary>
    /// A ticked count is kept with the start of the period it was ticked in (minutes since 1970), so a new day or week
    /// starts from zero by itself.
    /// </summary>
    public static int Count(int[]? saved, bool weekly, DateTimeOffset now) =>
        saved is [var count, var period] && period == Minutes(PeriodStart(weekly, now)) ? count : 0;

    public static int[] Save(int count, bool weekly, DateTimeOffset now) => [count, Minutes(PeriodStart(weekly, now))];

    private static int Minutes(DateTimeOffset t) => (int)(t.ToUnixTimeSeconds() / 60);

    public static Checklist Load(string path)
    {
        var daily = new List<Task>();
        var weekly = new List<Task>();
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            foreach (var (key, list) in new[] { ("daily", daily), ("weekly", weekly) })
                foreach (var t in doc.RootElement.GetProperty(key).EnumerateArray())
                {
                    var names = new Dictionary<string, string>();
                    foreach (var lang in new[] { "en", "uk", "ru" })
                        if (t.TryGetProperty(lang, out var n) && n.GetString() is { Length: > 0 } s) names[lang] = s;
                    var hints = new Dictionary<string, string>();
                    if (t.TryGetProperty("hint", out var h))
                        foreach (var p in h.EnumerateObject())
                            if (p.Value.GetString() is { Length: > 0 } s) hints[p.Name] = s;
                    list.Add(new Task(t.GetProperty("id").GetString() ?? "", key == "weekly", Math.Max(1, t.GetProperty("max").GetInt32()), names, hints,
                        t.TryGetProperty("res", out var r) ? r.GetInt32() : 0, t.TryGetProperty("runs", out var d) ? d.GetString() : null));
                }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            // no table: the checklist window says so
        }
        return new Checklist(daily, weekly);
    }
}
