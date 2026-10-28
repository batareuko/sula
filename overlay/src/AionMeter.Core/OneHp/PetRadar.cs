using System.Text.Json;
using AionMeter.Core.Events;

namespace AionMeter.Core.OneHp;

/// <summary>
/// 1 HP: pet radar — the monsters in view that drop a pet's soul (data/pet_souls.json, from Aion2Data's pet map). Only
/// monsters the game shows you count: a monster is listed from its spawn packet until it dies, the zone changes or it
/// has not been seen for <see cref="ForgetAfterMs"/>.
/// </summary>
public sealed class PetRadar
{
    public const long ForgetAfterMs = 3 * 60_000;

    /// <param name="Areas">Territories with this soul monster and their number of spawn points.</param>
    public sealed record Soul(int PetId, string Pet, string Family, string Monster, IReadOnlyList<(string Area, int Spots)> Areas);

    /// <param name="Count">Monsters of this soul in view.</param>
    public sealed record Nearby(Soul Soul, int Count, long LastSeenMs);

    /// <summary>A soul monster on the radar: where it came into view (game units, centimetres).</summary>
    public sealed record Dot(uint ActorId, Soul Soul, float X, float Y, float Z);

    /// <param name="Center">The monster you hit last (you stand next to it), or null when you have not hit one lately.</param>
    public sealed record Picture((float X, float Y)? Center, IReadOnlyList<Dot> Dots);

    private readonly Dictionary<int, Soul> _byNpc = new();
    private readonly Dictionary<uint, (Soul Soul, long SeenMs)> _inView = new();
    private readonly Dictionary<uint, (float X, float Y, float Z)> _where = new(); // every NPC in view: where it appeared
    private uint? _self;
    private (float X, float Y, long AtMs)? _me; // where you are (your own moves)

    /// <summary>Your actor id when it is known from elsewhere (the meter's tracker, restored from the cache).</summary>
    public Func<uint?>? SelfId { get; set; }

    private uint? Self => _self ?? SelfId?.Invoke();
    private (float X, float Y, long AtMs)? _anchor; // the monster you hit last
    private readonly object _gate = new();

    public IReadOnlyCollection<Soul> All { get; }

    public PetRadar(IEnumerable<(Soul Soul, IEnumerable<int> NpcCodes)> souls)
    {
        var all = new List<Soul>();
        foreach (var (soul, codes) in souls)
        {
            all.Add(soul);
            foreach (var code in codes) _byNpc[code] = soul;
        }
        All = all;
    }

    public static PetRadar Load(string path)
    {
        var souls = new List<(Soul, IEnumerable<int>)>();
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            foreach (var p in doc.RootElement.GetProperty("pets").EnumerateArray())
            {
                var areas = p.GetProperty("areas").EnumerateArray().Select(a => (a[0].GetString() ?? "", a[1].GetInt32())).ToList();
                var soul = new Soul(p.GetProperty("id").GetInt32(), p.GetProperty("pet").GetString() ?? "", p.GetProperty("family").GetString() ?? "",
                    p.GetProperty("monster").GetString() ?? "", areas);
                souls.Add((soul, p.GetProperty("npcs").EnumerateArray().Select(n => n.GetInt32()).ToList()));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            // no pet table: the radar stays empty
        }
        return new PetRadar(souls);
    }

    public Soul? ForNpc(int npcCode) => _byNpc.GetValueOrDefault(npcCode);

    public void Observe(GameEvent e)
    {
        lock (_gate)
        {
            switch (e)
            {
                case NpcSeenEvent n:
                    if (n.X != 0 || n.Y != 0)
                    {
                        if (_where.Count > 4_000) _where.Clear();
                        _where[n.ActorId] = (n.X, n.Y, n.Z);
                    }
                    else _where.Remove(n.ActorId);
                    if (_byNpc.TryGetValue(n.NpcCode, out var soul)) _inView[n.ActorId] = (soul, n.TimeMs);
                    else _inView.Remove(n.ActorId); // the id now belongs to another monster
                    break;
                case SelfIdentifiedEvent s:
                    _self = s.ActorId;
                    break;
                case MoveEvent m:
                    if (m.ActorId == Self) _me = (m.X, m.Y, m.TimeMs);
                    else if (_where.ContainsKey(m.ActorId)) _where[m.ActorId] = (m.X, m.Y, m.Z); // monsters walk: follow them
                    break;
                case DamageEvent d when d.SourceId == Self && _where.TryGetValue(d.TargetId, out var hit):
                    _anchor = (hit.X, hit.Y, d.TimeMs);
                    break;
                case NpcHpEvent hp when _inView.TryGetValue(hp.ActorId, out var seen):
                    _inView[hp.ActorId] = (seen.Soul, hp.TimeMs);
                    break;
                case DeathEvent d:
                    _inView.Remove(d.ActorId);
                    break;
                case ZoneChangedEvent:
                    _inView.Clear();
                    _where.Clear();
                    _anchor = null;
                    _me = null;
                    break;
            }
        }
    }

    /// <summary>The radar's picture: soul monsters with a known place, centred on the monster you hit in the last 2 minutes.</summary>
    public Picture Map(long nowMs)
    {
        lock (_gate)
        {
            foreach (var id in _inView.Where(kv => nowMs - kv.Value.SeenMs > ForgetAfterMs).Select(kv => kv.Key).ToList())
                _inView.Remove(id);
            var dots = _inView.Where(kv => _where.ContainsKey(kv.Key))
                .Select(kv => { var p = _where[kv.Key]; return new Dot(kv.Key, kv.Value.Soul, p.X, p.Y, p.Z); })
                .ToList();
            // you (your last move, 30 s), else the monster you hit (2 min), else unknown
            var center = _me is { } me && nowMs - me.AtMs < 30_000 ? (me.X, me.Y)
                : _anchor is { } a && nowMs - a.AtMs < 120_000 ? (a.X, a.Y) : ((float, float)?)null;
            return new Picture(center, dots);
        }
    }

    /// <summary>Souls in view, most monsters first; <paramref name="skip"/> holds the pets you no longer need.</summary>
    public IReadOnlyList<Nearby> InView(long nowMs, IReadOnlySet<int>? skip = null)
    {
        lock (_gate)
        {
            foreach (var id in _inView.Where(kv => nowMs - kv.Value.SeenMs > ForgetAfterMs).Select(kv => kv.Key).ToList())
                _inView.Remove(id);
            return _inView.Values
                .Where(v => skip is null || !skip.Contains(v.Soul.PetId))
                .GroupBy(v => v.Soul.PetId)
                .Select(g => new Nearby(g.First().Soul, g.Count(), g.Max(v => v.SeenMs)))
                .OrderByDescending(n => n.Count).ThenBy(n => n.Soul.Pet, StringComparer.Ordinal)
                .ToList();
        }
    }
}
