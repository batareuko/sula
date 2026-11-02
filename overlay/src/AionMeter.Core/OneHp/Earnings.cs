using System.Text.Json;
using AionMeter.Core.Events;

namespace AionMeter.Core.OneHp;

/// <summary>
/// 1 HP: what you earn — every change of a currency the game reports (Kinah, Abyss Points …) as a gain or a spend, per
/// character, kept for a week in %AppData%\1HP-Overlay\earnings.json. Sums for this session, the last hour and the game day
/// (from the 07:00 UTC reset), and the pace per hour.
/// </summary>
public sealed class Earnings
{
    public const int KeepDays = 8;

    /// <param name="Delta">Gained (positive) or spent (negative).</param>
    public sealed record Change(DateTimeOffset At, string Character, int Id, long Delta, long Balance);

    /// <summary>One currency: the balance now, gained / spent this session, in the last hour, today; per hour this session.</summary>
    public sealed record Sum(int Id, long Balance, long Session, long Hour, long Today, long SpentToday, double PerHour);

    private readonly string? _path;
    private readonly List<Change> _changes = new();
    private readonly Dictionary<(string, int), long> _last = new(); // the last known balance per character and currency
    private readonly object _gate = new();
    private DateTimeOffset _started;

    /// <summary>When this session began (the meter's start); the preview renderer moves it back.</summary>
    public DateTimeOffset SessionStart { get => _started; set => _started = value; }

    public Earnings(string? path, DateTimeOffset? started = null)
    {
        _path = path;
        _started = started ?? DateTimeOffset.UtcNow;
        if (path is null || !File.Exists(path)) return;
        try
        {
            _changes.AddRange(JsonSerializer.Deserialize<List<Change>>(File.ReadAllText(path)) ?? []);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // a damaged file: start again
        }
    }

    public event Action? Changed;

    /// <summary>
    /// A currency's balance as the game sent it. The first one of a character only sets the baseline (the login list);
    /// later ones add the difference.
    /// </summary>
    public void Observe(string character, int id, long balance, DateTimeOffset at)
    {
        if (string.IsNullOrEmpty(character) || balance < 0) return;
        lock (_gate)
        {
            var key = (character.ToLowerInvariant(), id);
            var had = _last.TryGetValue(key, out var before);
            _last[key] = balance;
            if (!had || before == balance) return;
            _changes.Add(new Change(at, character, id, balance - before, balance));
            var cut = at.AddDays(-KeepDays);
            _changes.RemoveAll(c => c.At < cut);
            Save();
        }
        Changed?.Invoke();
    }

    public IReadOnlyList<Sum> Sums(string character, DateTimeOffset now)
    {
        var day = Checklist.PeriodStart(false, now);
        lock (_gate)
        {
            var mine = _changes.Where(c => string.Equals(c.Character, character, StringComparison.OrdinalIgnoreCase)).ToList();
            var ids = mine.Select(c => c.Id).Concat(_last.Keys.Where(k => k.Item1 == character.ToLowerInvariant()).Select(k => k.Item2)).Distinct();
            var hours = Math.Max(1.0 / 60, (now - _started).TotalHours);
            return ids.Select(id =>
            {
                var of = mine.Where(c => c.Id == id).ToList();
                long Gained(DateTimeOffset from) => of.Where(c => c.At >= from && c.Delta > 0).Sum(c => c.Delta);
                var session = Gained(_started);
                return new Sum(id, _last.GetValueOrDefault((character.ToLowerInvariant(), id), of.LastOrDefault()?.Balance ?? 0),
                    session, Gained(now.AddHours(-1)), Gained(day), -of.Where(c => c.At >= day && c.Delta < 0).Sum(c => c.Delta), session / hours);
            }).OrderBy(s => s.Id == ResourceEvent.Kinah ? 0 : s.Id == ResourceEvent.Currency10000001 ? 1 : 2).ThenBy(s => s.Id).ToList();
        }
    }

    private void Save()
    {
        if (_path is null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(_changes));
            File.Move(tmp, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // kept in memory
        }
    }
}
