using System.Text.Json;
using AionMeter.Core.Events;

namespace AionMeter.Core.OneHp;

/// <summary>Which kind of Hidden Cube this is: the game shows red and yellow cubes.</summary>
public enum CubeKind { Red, Yellow }

/// <summary>
/// 1 HP: the Hidden Cube radar — the cubes in view around you, from the packet stream. A cube is tracked from its
/// spawn packet (matched by the NPC codes in data/cubes.json, filled in by the packet research mode) until it dies,
/// the zone changes, or it has not been seen for <see cref="ForgetAfterMs"/>. The game sends your own position only
/// with movement corrections (skill / dash), so without a fresh position the caller shows "cube detected" instead of
/// a direction — <see cref="Arrow.SelfFresh"/> tells them apart.
/// </summary>
public sealed class CubeRadar
{
    public const long ForgetAfterMs = 30_000;
    /// <summary>Your own position counts as fresh for this long (the game sends it only on skill / dash).</summary>
    public const long SelfMaxAgeMs = 30_000;

    /// <param name="NpcCode">The NPC template id that identifies the cube (from data/cubes.json).</param>
    public sealed record Cube(int NpcCode, CubeKind Kind);

    /// <param name="SeenMs">When the cube last showed itself (spawn or a position update).</param>
    public sealed record Dot(uint ActorId, Cube Cube, float X, float Y, float Z, long SeenMs);

    /// <summary>The arrow to draw: the vector from you to the cube (centimetres), its distance, and whether the direction is reliable.</summary>
    public sealed record Arrow(
        uint ActorId, Cube Cube,
        float X, float Y, float Z,
        float Dx, float Dy, float Dz,
        float DistanceM,
        bool SelfFresh,
        bool Gone,
        long AtMs);

    private readonly Dictionary<int, Cube> _byNpc = new();
    private readonly Dictionary<uint, (Cube Cube, long SeenMs)> _inView = new();
    private readonly Dictionary<uint, (float X, float Y, float Z)> _where = new(); // every NPC in view: where it appeared
    private readonly Dictionary<uint, (Cube Cube, long GoneMs)> _gone = new();     // cubes that disappeared: for the fade-out
    private uint? _self;
    private (float X, float Y, float Z, long AtMs)? _me;
    private readonly object _gate = new();

    /// <summary>Your actor id when it is known from elsewhere (the meter's tracker, restored from the cache).</summary>
    public Func<uint?>? SelfId { get; set; }

    private uint? Self => _self ?? SelfId?.Invoke();

    public IReadOnlyCollection<Cube> All { get; }

    public CubeRadar(IEnumerable<Cube> cubes)
    {
        var all = new List<Cube>();
        foreach (var cube in cubes)
        {
            all.Add(cube);
            _byNpc[cube.NpcCode] = cube;
        }
        All = all;
    }

    /// <summary>Reads data/cubes.json: <c>{ "red": [codes], "yellow": [codes] }</c> — the codes the research mode confirmed.</summary>
    public static CubeRadar Load(string path)
    {
        var cubes = new List<Cube>();
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            Add("red", CubeKind.Red);
            Add("yellow", CubeKind.Yellow);

            void Add(string prop, CubeKind kind)
            {
                if (!doc.RootElement.TryGetProperty(prop, out var arr) || arr.ValueKind != JsonValueKind.Array) return;
                foreach (var n in arr.EnumerateArray())
                    if (n.ValueKind == JsonValueKind.Number && n.TryGetInt32(out var code) && code > 0)
                        cubes.Add(new Cube(code, kind));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // no cube table: the radar stays empty
        }
        return new CubeRadar(cubes);
    }

    public Cube? ForNpc(int npcCode) => _byNpc.GetValueOrDefault(npcCode);

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
                    if (_byNpc.TryGetValue(n.NpcCode, out var cube))
                    {
                        _inView[n.ActorId] = (cube, n.TimeMs);
                        _gone.Remove(n.ActorId); // a respawn after a fade
                    }
                    else _inView.Remove(n.ActorId); // the id now belongs to another NPC
                    break;
                case SelfIdentifiedEvent s:
                    _self = s.ActorId;
                    break;
                case MoveEvent m:
                    if (m.ActorId == Self) _me = (m.X, m.Y, m.Z, m.TimeMs);
                    else if (_where.ContainsKey(m.ActorId)) _where[m.ActorId] = (m.X, m.Y, m.Z); // cubes and monsters walk: follow them
                    break;
                case DeathEvent d:
                    if (_inView.Remove(d.ActorId, out var seen)) _gone[d.ActorId] = (seen.Cube, d.TimeMs);
                    break;
                case ZoneChangedEvent:
                    _inView.Clear();
                    _where.Clear();
                    _gone.Clear();
                    _me = null;
                    break;
            }
        }
    }

    /// <summary>The cubes in view with a known place; <paramref name="kind"/> filters to red / yellow (null = all).</summary>
    public IReadOnlyList<Dot> InView(long nowMs, CubeKind? kind = null)
    {
        lock (_gate)
        {
            Prune(nowMs);
            return _inView.Where(kv => kind is null || kv.Value.Cube.Kind == kind)
                .Where(kv => _where.ContainsKey(kv.Key))
                .Select(kv => { var p = _where[kv.Key]; return new Dot(kv.Key, kv.Value.Cube, p.X, p.Y, p.Z, kv.Value.SeenMs); })
                .ToList();
        }
    }

    /// <summary>
    /// The arrow to draw: the nearest cube in view, or — for <paramref name="fadeMs"/> — the cube that just
    /// disappeared (its <see cref="Arrow.Gone"/> is true so the overlay can fade the arrow out). The direction comes
    /// from your last known position; <see cref="Arrow.SelfFresh"/> is false when it is older than
    /// <see cref="SelfMaxAgeMs"/>, so the caller shows "cube detected" instead of a precise arrow.
    /// </summary>
    public Arrow? Nearest(long nowMs, CubeKind? kind = null, long fadeMs = 500)
    {
        lock (_gate)
        {
            Prune(nowMs);
            var me = _me;
            var selfFresh = me is { } m && nowMs - m.AtMs <= SelfMaxAgeMs;

            (uint Id, Cube Cube, float X, float Y, float Z, long AtMs)? best = null;
            long bestDist = long.MaxValue;
            foreach (var (id, seen) in _inView)
            {
                if (kind is not null && seen.Cube.Kind != kind) continue;
                if (!_where.TryGetValue(id, out var p)) continue;
                var d = Dist2(p.X, p.Y, p.Z, me);
                if (d < bestDist) { bestDist = d; best = (id, seen.Cube, p.X, p.Y, p.Z, seen.SeenMs); }
            }

            var gone = false;
            if (best is null)
            {
                foreach (var (id, g) in _gone)
                {
                    if (nowMs - g.GoneMs > fadeMs) continue;
                    if (kind is not null && g.Cube.Kind != kind) continue;
                    if (!_where.TryGetValue(id, out var p)) continue;
                    best = (id, g.Cube, p.X, p.Y, p.Z, g.GoneMs);
                    gone = true;
                    break;
                }
            }
            if (best is not { } b) return null;

            float dx = 0, dy = 0, dz = 0;
            if (me is { } p)
            {
                dx = b.X - p.X;
                dy = b.Y - p.Y;
                dz = b.Z - p.Z;
            }
            var distance = (float)Math.Sqrt((double)dx * dx + (double)dy * dy + (double)dz * dz) / 100f;
            return new Arrow(b.Id, b.Cube, b.X, b.Y, b.Z, dx, dy, dz, distance, selfFresh, gone, b.AtMs);
        }
    }

    private static long Dist2(float x, float y, float z, (float X, float Y, float Z, long AtMs)? me)
    {
        if (me is not { } p) return 0;
        var dx = x - p.X;
        var dy = y - p.Y;
        var dz = z - p.Z;
        return (long)((double)dx * dx + (double)dy * dy + (double)dz * dz);
    }

    private void Prune(long nowMs)
    {
        foreach (var id in _inView.Where(kv => nowMs - kv.Value.SeenMs > ForgetAfterMs).Select(kv => kv.Key).ToList())
            _inView.Remove(id);
        foreach (var id in _gone.Where(kv => nowMs - kv.Value.GoneMs > 60_000).Select(kv => kv.Key).ToList())
            _gone.Remove(id);
    }
}
