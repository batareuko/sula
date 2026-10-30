using AionMeter.Core.Events;

namespace AionMeter.Core.OneHp;

/// <summary>
/// 1 HP: Hidden Cube radar — the "Cube detected" state machine. The game announces a hidden cube's whole life by id:
/// <see cref="CubePhase.Near"/> (<c>38 36</c>) and <see cref="CubePhase.SpawnRecord"/> (<c>37 36</c>) bring it up,
/// <see cref="CubePhase.Interact"/>/<see cref="CubePhase.Focus"/> mean you are collecting it, and
/// <see cref="CubePhase.Gone"/>/<see cref="CubePhase.Collected"/> (<c>05 8d</c>/<c>35 36</c>) take it down. The cube's
/// coordinates are not in these packets (it is "hidden" by design), so this radar tracks presence only; the direction
/// arrow is a stub until the position packet is confirmed. Everything clears on a zone change.
/// </summary>
public sealed class CubeRadar
{
    /// <summary>Keep "detected" for this long after the last cube disappears, so the indicator does not flicker.</summary>
    public long FadeAfterMs { get; set; } = 500;

    /// <param name="CubeId">The dynamic varint id the game hands the cube (unique per cube).</param>
    /// <param name="FirstSeenMs">When the cube came near.</param>
    /// <param name="LastSeenMs">The last packet that mentioned it.</param>
    public sealed record Cube(uint CubeId, long FirstSeenMs, long LastSeenMs);

    private readonly Dictionary<uint, (long First, long Last)> _cubes = new();
    private long _lastGoneMs = long.MinValue / 2;
    private uint? _self;
    private readonly object _gate = new();

    /// <summary>Your actor id when it is known from elsewhere (the meter's tracker); the cube packets also carry it.</summary>
    public Func<uint?>? SelfId { get; set; }

    /// <summary>Your actor id as the cube packets or the tracker know it; the arrow will aim from here once cube positions arrive.</summary>
    public uint? Self => _self ?? SelfId?.Invoke();

    /// <summary>Raised (on the capture thread) when a cube is first seen (true) or collected/gone (false).</summary>
    public event Action<Cube, bool>? Changed;

    public void Observe(GameEvent e)
    {
        List<(Cube Cube, bool Appeared)>? changes = null;
        lock (_gate)
        {
            switch (e)
            {
                case CubeEvent c:
                    if (c.PlayerId != 0) _self = c.PlayerId;
                    switch (c.Phase)
                    {
                        case CubePhase.Near:
                        case CubePhase.SpawnRecord:
                            if (!_cubes.ContainsKey(c.CubeId)) (changes ??= new()).Add((new Cube(c.CubeId, c.TimeMs, c.TimeMs), true));
                            _cubes[c.CubeId] = (c.TimeMs, c.TimeMs);
                            break;
                        case CubePhase.Interact:
                        case CubePhase.Focus:
                            if (!_cubes.ContainsKey(c.CubeId)) (changes ??= new()).Add((new Cube(c.CubeId, c.TimeMs, c.TimeMs), true));
                            if (_cubes.TryGetValue(c.CubeId, out var known)) _cubes[c.CubeId] = (known.First, c.TimeMs);
                            else _cubes[c.CubeId] = (c.TimeMs, c.TimeMs);
                            break;
                        case CubePhase.Gone:
                        case CubePhase.Collected:
                            if (_cubes.Remove(c.CubeId, out var gone))
                            {
                                _lastGoneMs = c.TimeMs;
                                (changes ??= new()).Add((new Cube(c.CubeId, gone.First, c.TimeMs), false));
                            }
                            break;
                    }
                    break;
                case SelfIdentifiedEvent s:
                    _self = s.ActorId;
                    break;
                case ZoneChangedEvent:
                    _cubes.Clear();
                    _lastGoneMs = long.MinValue / 2;
                    break;
            }
        }
        if (changes is not null)
            foreach (var (cube, appeared) in changes) Changed?.Invoke(cube, appeared);
    }

    /// <summary>The cubes near you, the earliest first. Each stays until its Gone/Collected packet or a zone change.</summary>
    public IReadOnlyList<Cube> Active(long nowMs)
    {
        lock (_gate) return _cubes.Select(kv => new Cube(kv.Key, kv.Value.First, kv.Value.Last)).OrderBy(c => c.FirstSeenMs).ToList();
    }

    /// <summary>True while a cube is near, and for <see cref="FadeAfterMs"/> after the last one disappeared.</summary>
    public bool Detected(long nowMs)
    {
        lock (_gate) return _cubes.Count > 0 || nowMs - _lastGoneMs < FadeAfterMs;
    }
}
