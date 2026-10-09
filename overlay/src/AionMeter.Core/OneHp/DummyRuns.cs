using System.Text.Json;
using AionMeter.Core.Combat;

namespace AionMeter.Core.OneHp;

/// <summary>
/// 1 HP: training dummy runs. Every fight on a dummy of at least <see cref="MinRunMs"/> is a run: its number, your DPS
/// against your best run on that dummy and against the run before (per character and dummy, kept across restarts).
/// </summary>
public sealed class DummyRuns
{
    public const long MinRunMs = 10_000;

    public sealed class Stats
    {
        public int Runs { get; set; }
        public double Best { get; set; }
        public double Last { get; set; }
        public DateTimeOffset BestAt { get; set; }
    }

    /// <param name="Run">The number this run has (the running one) or had (a finished one).</param>
    /// <param name="VsBest">Percent against the best earlier run; null before the first one.</param>
    /// <param name="VsLast">Percent against the run before this one; null before the first one.</param>
    public sealed record Comparison(int Run, double Dps, double? VsBest, double? VsLast, bool IsNewBest);

    private readonly object _gate = new();
    private readonly string? _path;
    private Dictionary<string, Stats> _stats = new();
    private readonly Dictionary<Guid, Comparison> _finished = new(); // a run's result as it was when it ended

    public DummyRuns(string? path = null)
    {
        _path = path;
        if (path is null || !File.Exists(path)) return;
        try
        {
            _stats = JsonSerializer.Deserialize<Dictionary<string, Stats>>(File.ReadAllText(path)) ?? new();
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
        }
    }

    private static string Key(string self, int dummy) => $"{self.Trim().ToLowerInvariant()}|{dummy}";

    /// <summary>The dummy fought in <paramref name="snap"/> with your row in it, or null when it is not a dummy run.</summary>
    private static (int Dummy, CombatantSnapshot Self)? RunOf(EncounterSnapshot snap, Func<int, bool> isDummy)
    {
        if (snap.Boss is not { NpcCode: > 0 } boss || !isDummy(boss.NpcCode)) return null;
        var self = snap.Combatants.FirstOrDefault(c => c.IsSelf);
        return self is null || self.Dps <= 0 ? null : (boss.NpcCode, self);
    }

    /// <summary>A fight ended: a dummy run long enough counts, and becomes the new best when it beat it.</summary>
    public Comparison? Finish(EncounterSnapshot snap, Func<int, bool> isDummy)
    {
        if (RunOf(snap, isDummy) is not var (dummy, self) || snap.CombatMs < MinRunMs) return null;
        Comparison result;
        lock (_gate)
        {
            if (_finished.TryGetValue(snap.Id, out var done)) return done;
            var key = Key(self.Name, dummy);
            var s = _stats.GetValueOrDefault(key) ?? new Stats();
            result = Compare(s, self.Dps, s.Runs + 1);
            s.Runs++;
            s.Last = self.Dps;
            if (self.Dps > s.Best)
            {
                s.Best = self.Dps;
                s.BestAt = DateTimeOffset.UtcNow;
            }
            _stats[key] = s;
            _finished[snap.Id] = result;
            if (_finished.Count > 200) _finished.Clear();
        }
        Save();
        return result;
    }

    /// <summary>The run on screen against your best and your last run (a finished one as it was when it ended).</summary>
    public Comparison? Compare(EncounterSnapshot snap, Func<int, bool> isDummy)
    {
        if (RunOf(snap, isDummy) is not var (dummy, self)) return null;
        lock (_gate)
        {
            if (_finished.TryGetValue(snap.Id, out var done)) return done;
            if (!snap.IsActive) return null; // too short to count, or from before this start
            var s = _stats.GetValueOrDefault(Key(self.Name, dummy)) ?? new Stats();
            return Compare(s, self.Dps, s.Runs + 1);
        }
    }

    private static Comparison Compare(Stats s, double dps, int run) =>
        new(run, dps,
            s.Best > 0 ? Math.Round((dps - s.Best) / s.Best * 100, 1) : null,
            s.Last > 0 ? Math.Round((dps - s.Last) / s.Last * 100, 1) : null,
            s.Best > 0 && dps > s.Best);

    private void Save()
    {
        if (_path is null) return;
        try
        {
            string json;
            lock (_gate) json = JsonSerializer.Serialize(_stats);
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, _path, overwrite: true);
        }
        catch (IOException)
        {
        }
    }
}
