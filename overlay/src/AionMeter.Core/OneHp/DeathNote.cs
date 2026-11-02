using System.Text.Json;
using System.Text.Json.Serialization;
using AionMeter.Core.Events;

namespace AionMeter.Core.OneHp;

/// <summary>
/// 1 HP: the PvP "death note" — every time another player killed you (the server's kill notice), who, from which server
/// and class, their combat power and item level when known, and where. Kept in %AppData%\1HP-Overlay\pvp-deaths.json;
/// <see cref="Nemeses"/> lists the ones who did it again and again.
/// </summary>
public sealed class DeathNote
{
    public const int Keep = 3000;

    /// <param name="Victim">Your character then (several characters share one note).</param>
    public sealed record Death(DateTimeOffset At, string Victim, int VictimServer, string Killer, int KillerServer, GameClass KillerClass,
        long KillerPower, int KillerItemLevel, string? Zone);

    /// <summary>A player who killed you: how often, first and last time, the latest known numbers and the places.</summary>
    public sealed record Nemesis(string Name, int Server, GameClass Class, int Kills, DateTimeOffset First, DateTimeOffset Last,
        long Power, int ItemLevel, IReadOnlyList<string> Zones, IReadOnlyList<string> Victims);

    private static readonly JsonSerializerOptions Json = new() { Converters = { new JsonStringEnumConverter() } };
    private readonly string? _path;
    private readonly List<Death> _deaths = new();
    private readonly object _gate = new();

    public DeathNote(string? path)
    {
        _path = path;
        if (path is null || !File.Exists(path)) return;
        try
        {
            _deaths.AddRange(JsonSerializer.Deserialize<List<Death>>(File.ReadAllText(path), Json) ?? []);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // a damaged file: start again
        }
    }

    public event Action? Changed;

    public IReadOnlyList<Death> All
    {
        get { lock (_gate) return _deaths.ToList(); }
    }

    /// <summary>Notes a death; the same kill told twice (within 5 s, same killer) counts once. False when it was a repeat.</summary>
    public bool Add(Death d)
    {
        if (string.IsNullOrWhiteSpace(d.Killer) || d.Killer.StartsWith('#')) return false;
        lock (_gate)
        {
            if (_deaths.Any(x => string.Equals(x.Killer, d.Killer, StringComparison.OrdinalIgnoreCase) && x.KillerServer == d.KillerServer &&
                                 Math.Abs((x.At - d.At).TotalSeconds) < 5))
                return false;
            _deaths.Add(d);
            if (_deaths.Count > Keep) _deaths.RemoveRange(0, _deaths.Count - Keep);
            Save();
        }
        Changed?.Invoke();
        return true;
    }

    /// <summary>Later numbers of a killer (combat power, item level, class), for the rows already there.</summary>
    public void Update(string killer, int server, GameClass cls, long power, int itemLevel)
    {
        var changed = false;
        lock (_gate)
            for (var i = 0; i < _deaths.Count; i++)
            {
                var d = _deaths[i];
                if (!string.Equals(d.Killer, killer, StringComparison.OrdinalIgnoreCase) || d.KillerServer != server) continue;
                var next = d with
                {
                    KillerClass = cls != GameClass.Unknown ? cls : d.KillerClass,
                    KillerPower = power > 0 ? power : d.KillerPower,
                    KillerItemLevel = itemLevel > 0 ? itemLevel : d.KillerItemLevel,
                };
                if (next == d) continue;
                _deaths[i] = next;
                changed = true;
            }
        if (!changed) return;
        lock (_gate) Save();
        Changed?.Invoke();
    }

    /// <summary>Who killed you at least <paramref name="min"/> times, the most kills first (then the latest).</summary>
    public IReadOnlyList<Nemesis> Nemeses(int min = 2)
    {
        lock (_gate)
            return _deaths
                .GroupBy(d => (Name: d.Killer.ToLowerInvariant(), d.KillerServer))
                .Where(g => g.Count() >= min)
                .Select(g =>
                {
                    var last = g.MaxBy(d => d.At)!;
                    return new Nemesis(last.Killer, last.KillerServer,
                        g.Select(d => d.KillerClass).LastOrDefault(c => c != GameClass.Unknown),
                        g.Count(), g.Min(d => d.At), last.At,
                        g.OrderByDescending(d => d.At).Select(d => d.KillerPower).FirstOrDefault(p => p > 0),
                        g.OrderByDescending(d => d.At).Select(d => d.KillerItemLevel).FirstOrDefault(p => p > 0),
                        g.Where(d => !string.IsNullOrEmpty(d.Zone)).GroupBy(d => d.Zone!).OrderByDescending(z => z.Count()).Select(z => z.Key).ToList(),
                        g.Select(d => d.Victim).Distinct(StringComparer.OrdinalIgnoreCase).ToList());
                })
                .OrderByDescending(n => n.Kills).ThenByDescending(n => n.Last)
                .ToList();
    }

    private void Save()
    {
        if (_path is null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(_deaths, Json));
            File.Move(tmp, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // kept in memory; the next death tries again
        }
    }
}
