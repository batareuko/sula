using AionMeter.Core.Events;

namespace AionMeter.Core.OneHp;

/// <summary>
/// 1 HP: boss mechanics on the card — what each NPC casts and how often. Every NPC skill a monster casts is kept for the
/// fight; the gap between two casts of the same skill by the same kind of monster is learnt (and kept across restarts),
/// so the card can say what comes next and in about how long.
/// </summary>
public sealed class BossCasts
{
    /// <summary>A gap shorter than this is one combo, not a cycle; longer is a different phase.</summary>
    public const long MinGapMs = 3_000, MaxGapMs = 300_000;

    public sealed record Cast(int SkillCode, long AtMs);

    /// <param name="DueMs">When it should come next (last cast + learnt gap).</param>
    public sealed record Upcoming(int SkillCode, long DueMs, long GapMs);

    /// <summary>Learnt gap per (NPC code, skill code): average and how many gaps it is made of.</summary>
    public sealed class Timing
    {
        public double AvgMs { get; set; }
        public int Samples { get; set; }
    }

    private readonly object _gate = new();
    private readonly Dictionary<uint, int> _codes = new();                  // actor → NPC code
    private readonly Dictionary<uint, List<Cast>> _casts = new();           // actor → casts this fight
    private readonly Dictionary<(int Npc, int Skill), Timing> _timings = new();
    private readonly Func<int, bool> _isNpcSkill;

    /// <param name="isNpcSkill">True for skills worth showing (not a plain "Attack", not a class skill).</param>
    public BossCasts(Func<int, bool> isNpcSkill) => _isNpcSkill = isNpcSkill;

    public bool Changed { get; private set; }

    public void Observe(GameEvent e)
    {
        lock (_gate)
        {
            switch (e)
            {
                case NpcSeenEvent n when n.NpcCode != 0:
                    _codes[n.ActorId] = n.NpcCode;
                    _casts.Remove(n.ActorId);
                    break;
                case CastEvent c when _codes.TryGetValue(c.ActorId, out var npc) && _isNpcSkill(c.SkillCode):
                    if (!_casts.TryGetValue(c.ActorId, out var list)) _casts[c.ActorId] = list = new List<Cast>();
                    var last = list.LastOrDefault(x => x.SkillCode == c.SkillCode);
                    if (last is not null && c.TimeMs - last.AtMs is var gap and >= MinGapMs and <= MaxGapMs)
                    {
                        if (!_timings.TryGetValue((npc, c.SkillCode), out var t)) _timings[(npc, c.SkillCode)] = t = new Timing();
                        // a running average that settles after a few fights but still follows a patch
                        var n = Math.Min(t.Samples, 19);
                        t.AvgMs = (t.AvgMs * n + gap) / (n + 1);
                        t.Samples++;
                        Changed = true;
                    }
                    if (last is null || c.TimeMs - last.AtMs >= 500) list.Add(new Cast(c.SkillCode, c.TimeMs)); // repeats of one cast
                    if (list.Count > 200) list.RemoveRange(0, list.Count - 200);
                    break;
                case DeathEvent d:
                    _casts.Remove(d.ActorId);
                    break;
                case ZoneChangedEvent { IsTeleport: false }:
                    _casts.Clear();
                    _codes.Clear();
                    break;
            }
        }
    }

    /// <summary>The monster's casts at or after <paramref name="sinceMs"/>, oldest first.</summary>
    public IReadOnlyList<Cast> Recent(uint actorId, long sinceMs)
    {
        lock (_gate)
            return _casts.TryGetValue(actorId, out var list) ? list.Where(c => c.AtMs >= sinceMs).ToList() : [];
    }

    /// <summary>
    /// What the monster should cast next, soonest first: every skill it cast in this fight that has a learnt gap
    /// (two gaps or more), due at its last cast + the gap. Skills overdue by more than half a gap are left out.
    /// </summary>
    public IReadOnlyList<Upcoming> Next(uint actorId, long nowMs)
    {
        lock (_gate)
        {
            if (!_codes.TryGetValue(actorId, out var npc) || !_casts.TryGetValue(actorId, out var list)) return [];
            return list.GroupBy(c => c.SkillCode)
                .Select(g => (Skill: g.Key, Last: g.Max(c => c.AtMs), Timing: _timings.GetValueOrDefault((npc, g.Key))))
                .Where(x => x.Timing is { Samples: >= 2 })
                .Select(x => new Upcoming(x.Skill, x.Last + (long)x.Timing!.AvgMs, (long)x.Timing.AvgMs))
                .Where(u => nowMs - u.DueMs < u.GapMs / 2)
                .OrderBy(u => u.DueMs)
                .ToList();
        }
    }

    /// <summary>For saving: "npc:skill" → timing.</summary>
    public Dictionary<string, Timing> Export()
    {
        lock (_gate)
        {
            Changed = false;
            return _timings.ToDictionary(kv => $"{kv.Key.Npc}:{kv.Key.Skill}", kv => new Timing { AvgMs = kv.Value.AvgMs, Samples = kv.Value.Samples });
        }
    }

    public void Import(IReadOnlyDictionary<string, Timing> saved)
    {
        lock (_gate)
        {
            foreach (var (key, t) in saved)
            {
                var parts = key.Split(':');
                if (parts.Length == 2 && int.TryParse(parts[0], out var npc) && int.TryParse(parts[1], out var skill) && t.Samples > 0)
                    _timings[(npc, skill)] = t;
            }
        }
    }
}
