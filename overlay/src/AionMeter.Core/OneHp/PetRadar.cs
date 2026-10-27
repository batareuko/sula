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

    private readonly Dictionary<int, Soul> _byNpc = new();
    private readonly Dictionary<uint, (Soul Soul, long SeenMs)> _inView = new();
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
                case NpcSeenEvent n when _byNpc.TryGetValue(n.NpcCode, out var soul):
                    _inView[n.ActorId] = (soul, n.TimeMs);
                    break;
                case NpcSeenEvent n:
                    _inView.Remove(n.ActorId); // the id now belongs to another monster
                    break;
                case NpcHpEvent hp when _inView.TryGetValue(hp.ActorId, out var seen):
                    _inView[hp.ActorId] = (seen.Soul, hp.TimeMs);
                    break;
                case DeathEvent d:
                    _inView.Remove(d.ActorId);
                    break;
                case ZoneChangedEvent { IsTeleport: false }:
                    _inView.Clear();
                    break;
            }
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
