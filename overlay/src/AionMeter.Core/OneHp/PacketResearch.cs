using AionMeter.Core.Events;
using AionMeter.Core.Protocol;

namespace AionMeter.Core.OneHp;

/// <summary>What the player marked: the cube appeared, moved, or disappeared.</summary>
public enum CubeMark { Appeared, Moved, Gone }

/// <summary>
/// 1 HP packet research: figures out which NPC code (or opcode) the Hidden Cubes use. While enabled it keeps a rolling
/// window of the raw packets and the decoded spawn/move/death events, and logs every opcode the parser does not know
/// into <see cref="LogPath"/>. When you press the "cube appeared" / "cube gone" hotkey, a marker is written with the
/// packets around it, and <see cref="Candidates"/> proposes NPC codes seen near those markers. The final codes go into
/// data/cubes.json by hand — nothing here decides them on its own.
/// </summary>
public sealed class PacketResearch
{
    private const int MaxPackets = 4_000;
    private const int MaxEvents = 2_000;
    private const long MaxLogBytes = 16L * 1024 * 1024;
    private const long MarkerWindowMs = 8_000;
    private const long CandidateWindowMs = 4_000;

    private readonly object _gate = new();
    private readonly Queue<(long TimeMs, ushort Opcode, string Hex)> _packets = new();
    private readonly Queue<(long TimeMs, string Kind, string Info)> _events = new();
    private readonly List<(long TimeMs, CubeMark Mark)> _marks = new();
    private readonly Dictionary<ushort, long> _unknown = new();

    /// <summary>Where cube-research-*.log goes (set by the app; the meter's data folder by default).</summary>
    public string LogPath { get; set; }

    /// <summary>Only logs and buffers while true; the packet tap and the event hook are cheap no-ops otherwise.</summary>
    public bool Enabled { get; set; }

    public PacketResearch(string logPath)
    {
        LogPath = logPath;
    }

    /// <summary>The packet tap: call with every packet the parser sees (PacketParser.Tap).</summary>
    public void OnPacket(long timeMs, ushort opcode, ReadOnlySpan<byte> body)
    {
        if (!Enabled) return;
        lock (_gate)
        {
            var hex = Convert.ToHexString(body.Length > 256 ? body[..256] : body);
            _packets.Enqueue((timeMs, opcode, hex));
            while (_packets.Count > MaxPackets) _packets.Dequeue();

            if (Opcodes.Known.Contains(opcode)) return;
            if (_unknown.TryGetValue(opcode, out var count) && count >= 3) return; // seen enough: one line per new opcode
            _unknown[opcode] = count + 1;
            WriteLine($"UNKNOWN {opcode:X4}  {hex}");
        }
    }

    /// <summary>The decoded events worth diffing (spawns, moves, deaths) — from the same stream the tracker consumes.</summary>
    public void Observe(GameEvent e)
    {
        if (!Enabled) return;
        lock (_gate)
        {
            switch (e)
            {
                case NpcSeenEvent n:
                    _events.Enqueue((n.TimeMs, "Spawn", $"#{n.ActorId} code={n.NpcCode} name={n.Name ?? ""} x={n.X:0} y={n.Y:0} z={n.Z:0}"));
                    break;
                case SummonSeenEvent s:
                    _events.Enqueue((s.TimeMs, "Summon", $"#{s.ActorId} code={s.NpcCode}"));
                    break;
                case MoveEvent m:
                    _events.Enqueue((m.TimeMs, "Move", $"#{m.ActorId} x={m.X:0} y={m.Y:0} z={m.Z:0}"));
                    break;
                case DeathEvent d:
                    _events.Enqueue((d.TimeMs, "Death", $"#{d.ActorId}"));
                    break;
                case ZoneChangedEvent z:
                    _events.Enqueue((z.TimeMs, "Zone", $"map={z.MapId} {z.ZoneName}"));
                    break;
                default:
                    return; // damage, casts, party … are not what makes a cube a cube
            }
            while (_events.Count > MaxEvents) _events.Dequeue();
        }
    }

    /// <summary>A marker from the player: the cube appeared / moved / disappeared just now.</summary>
    public void Mark(CubeMark mark, long nowMs)
    {
        if (!Enabled) return;
        lock (_gate)
        {
            _marks.Add((nowMs, mark));
            if (_marks.Count > 64) _marks.RemoveAt(0);
            WriteLine($"===== MARK {mark.ToString().ToUpperInvariant()}  {Time(nowMs)} =====");
            Dump("events before / after", _events.Where(x => Math.Abs(x.TimeMs - nowMs) <= MarkerWindowMs).Select(x => $"{x.Kind} {x.Info}").ToArray());
            Dump("packets before / after", _packets.Where(x => Math.Abs(x.TimeMs - nowMs) <= MarkerWindowMs).Select(x => $"{x.Opcode:X4} {x.Hex}").ToArray());
            WriteLine("");
        }
    }

    /// <summary>NPC codes worth checking: seen in spawns near the markers, rarest first. A suggestion, not the answer.</summary>
    public IReadOnlyList<Candidate> Candidates(long nowMs)
    {
        lock (_gate)
        {
            var totals = new Dictionary<int, (string Name, int Count)>();
            foreach (var (_, kind, info) in _events)
            {
                if (kind != "Spawn") continue;
                var code = Code(info);
                if (code == 0) continue;
                totals[code] = (Name(info), totals.GetValueOrDefault(code).Count + 1);
            }

            var near = new Dictionary<int, (int NearMarks, string Name)>();
            foreach (var (time, mark) in _marks)
            {
                if (mark is CubeMark.Moved) continue;
                foreach (var (etime, kind, info) in _events)
                {
                    if (kind != "Spawn" || Math.Abs(etime - time) > CandidateWindowMs) continue;
                    var code = Code(info);
                    if (code == 0) continue;
                    near[code] = (near.GetValueOrDefault(code).NearMarks + 1, Name(info));
                }
            }

            return near
                .Select(kv => new Candidate(kv.Key, kv.Value.Name, kv.Value.NearMarks, totals.GetValueOrDefault(kv.Key).Count))
                .OrderByDescending(c => c.NearMarks)
                .ThenBy(c => c.TotalSeen)
                .ToList();
        }
    }

    private static int Code(string info)
    {
        var i = info.IndexOf("code=", StringComparison.Ordinal);
        if (i < 0) return 0;
        var end = info.IndexOf(' ', i + 5);
        var s = end < 0 ? info[(i + 5)..] : info[(i + 5)..end];
        return int.TryParse(s, out var code) ? code : 0;
    }

    private static string Name(string info)
    {
        var i = info.IndexOf("name=", StringComparison.Ordinal);
        if (i < 0) return "";
        var end = info.IndexOf(' ', i + 5);
        return end < 0 ? info[(i + 5)..] : info[(i + 5)..end];
    }

    private void Dump(string title, string[] lines)
    {
        WriteLine($"--- {title} ---");
        foreach (var l in lines) WriteLine(l);
    }

    private void WriteLine(string line)
    {
        try
        {
            Directory.CreateDirectory(LogPath);
            var file = Path.Combine(LogPath, $"cube-research-{DateTime.Now:yyyyMMdd}.log");
            if (File.Exists(file) && new FileInfo(file).Length > MaxLogBytes) return;
            File.AppendAllText(file, $"{DateTime.Now:HH:mm:ss.fff} {line}{Environment.NewLine}");
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static string Time(long ms) => DateTimeOffset.FromUnixTimeMilliseconds(ms).ToLocalTime().ToString("HH:mm:ss.fff");
}

/// <param name="NpcCode">The NPC code seen in spawn packets near the markers.</param>
public sealed record Candidate(int NpcCode, string Name, int NearMarks, int TotalSeen);
