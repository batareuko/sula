using System.Text.Json;

namespace AionMeter.Core.OneHp;

/// <summary>
/// 1 HP: big mechanic warnings for raid bosses (data/boss_alerts.json) — what is coming, what to do and what comes after.
/// An alert fires on the boss's HP (a few per cent early as "soon"), on the fight clock (the enrage), on a skill the boss
/// casts (and early, when the gap between its casts has been learnt), or once at the pull.
/// </summary>
public sealed class BossAlerts
{
    public sealed record Alert(string Id, string Icon, string Title, string Do, string? Then,
        double? Hp, int? AtSec, IReadOnlyList<string> Skills, bool Pull, double SoonPct, int SoonSec);

    public enum Stage { Soon, Now }

    /// <param name="LeftSec">Seconds until it comes (a clock or a learnt cast), when known.</param>
    public sealed record Banner(Alert Alert, Stage Stage, double? LeftSec, double? AtHp);

    /// <summary>One look at the fight: the boss, its HP in per cent (null when unknown) and its casts by name.</summary>
    public sealed record Fight(Guid Id, int NpcCode, double? HpPercent, long ElapsedMs, long NowMs,
        IReadOnlyList<(string Skill, long AtMs)> Casts, IReadOnlyList<(string Skill, long DueMs)> Upcoming);

    /// <summary>How long an alert stays up once it fires.</summary>
    public const long ShowMs = 7_000;

    /// <summary>A skill alert fires again only after this long (one cast is seen as several packets).</summary>
    public const long RepeatMs = 15_000;

    private readonly Dictionary<int, IReadOnlyList<Alert>> _byNpc;
    private Guid _fight;
    private readonly Dictionary<string, long> _firedAt = new();   // alert id → when it last turned "now"

    public BossAlerts(Dictionary<int, IReadOnlyList<Alert>> byNpc) => _byNpc = byNpc;

    public bool Knows(int npcCode) => _byNpc.ContainsKey(npcCode);

    public IReadOnlyList<Alert> For(int npcCode) => _byNpc.GetValueOrDefault(npcCode) ?? [];

    /// <summary>
    /// Reads data/boss_alerts.json: { "bosses": { "English boss name": [ { id, icon, hp?, at?, skills?, pull?, soon?,
    /// soonSec?, "uk": { title, do, then? }, "en": {…} } ] } }. Every NPC code with that English name gets the alerts;
    /// the text is in <paramref name="lang"/>, else the first of <paramref name="fallbacks"/> that is there.
    /// </summary>
    public static BossAlerts Load(string json, IEnumerable<(int Code, string Name)> npcs, string lang, params string[] fallbacks)
    {
        using var doc = JsonDocument.Parse(json);
        var byName = new Dictionary<string, List<Alert>>(StringComparer.OrdinalIgnoreCase);
        foreach (var boss in doc.RootElement.GetProperty("bosses").EnumerateObject())
        {
            var list = new List<Alert>();
            foreach (var a in boss.Value.EnumerateArray())
            {
                JsonElement text = default;
                foreach (var l in new[] { lang }.Concat(fallbacks))
                    if (a.TryGetProperty(l, out text)) break;
                if (text.ValueKind != JsonValueKind.Object) continue;
                list.Add(new Alert(
                    Str(a, "id") ?? $"{boss.Name}#{list.Count}", Str(a, "icon") ?? "warn",
                    Str(text, "title") ?? "", Str(text, "do") ?? "", Str(text, "then"),
                    a.TryGetProperty("hp", out var hp) ? hp.GetDouble() : null,
                    a.TryGetProperty("at", out var at) ? at.GetInt32() : null,
                    a.TryGetProperty("skills", out var sk) ? sk.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => x.Length > 0).ToList() : [],
                    a.TryGetProperty("pull", out var pull) && pull.GetBoolean(),
                    a.TryGetProperty("soon", out var soon) ? soon.GetDouble() : 4,
                    a.TryGetProperty("soonSec", out var soonSec) ? soonSec.GetInt32() : 10));
            }
            byName[boss.Name] = list;
        }
        var byNpc = new Dictionary<int, IReadOnlyList<Alert>>();
        foreach (var (code, name) in npcs)
            if (byName.TryGetValue(name, out var alerts) && alerts.Count > 0) byNpc[code] = alerts;
        return new BossAlerts(byNpc);
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    /// <summary>
    /// What to show now, the most urgent first ("now" before "soon", then the nearest), and the alerts that have just
    /// turned "now" (for a sound or the voice). A new fight starts over.
    /// </summary>
    public (IReadOnlyList<Banner> Shown, IReadOnlyList<Alert> Fired) Tick(Fight f)
    {
        if (f.Id != _fight) { _fight = f.Id; _firedAt.Clear(); }
        if (!_byNpc.TryGetValue(f.NpcCode, out var alerts)) return ([], []);
        var shown = new List<Banner>();
        var fired = new List<Alert>();
        foreach (var a in alerts)
        {
            var last = _firedAt.GetValueOrDefault(a.Id, long.MinValue);
            var done = last != long.MinValue;
            // an HP alert takes its skill only around its own threshold (Bingo at 50 % and again at 25 %)
            var hpNear = a.Hp is not { } h || f.HpPercent is not { } p || p <= h + a.SoonPct;
            var cast = a.Skills.Count > 0 && hpNear
                ? f.Casts.Where(c => a.Skills.Contains(c.Skill, StringComparer.OrdinalIgnoreCase)).Select(c => c.AtMs).DefaultIfEmpty(long.MinValue).Max()
                : long.MinValue;
            var now = false;
            if (a.Pull && !done && f.ElapsedMs < 20_000) now = true;
            if (a.Hp is { } hp && f.HpPercent is { } pct && pct <= hp && !done) now = true;
            if (a.AtSec is { } at && f.ElapsedMs >= at * 1000L && !done) now = true;
            var repeat = a.Hp is null && a.AtSec is null && !a.Pull;          // a skill-only alert comes back with the skill
            if (cast != long.MinValue && f.NowMs - cast < ShowMs && (!done || (repeat && cast - last >= RepeatMs))) now = true;
            if (now)
            {
                _firedAt[a.Id] = last = f.NowMs;
                fired.Add(a);
            }
            if (last != long.MinValue && f.NowMs - last < ShowMs)
            {
                shown.Add(new Banner(a, Stage.Now, null, a.Hp));
                continue;
            }
            if (last != long.MinValue && !repeat) continue;                   // this one is over
            // soon: a few per cent above the threshold, the last seconds before the clock, a learnt cast close by
            if (a.Hp is { } h2 && f.HpPercent is { } p2 && p2 <= h2 + a.SoonPct)
                shown.Add(new Banner(a, Stage.Soon, null, h2));
            else if (a.AtSec is { } at2 && f.ElapsedMs >= (at2 - a.SoonSec) * 1000L)
                shown.Add(new Banner(a, Stage.Soon, (at2 * 1000L - f.ElapsedMs) / 1000.0, null));
            else if (a.Skills.Count > 0 && hpNear &&
                     f.Upcoming.Where(u => a.Skills.Contains(u.Skill, StringComparer.OrdinalIgnoreCase)).Select(u => (double?)(u.DueMs - f.NowMs) / 1000.0)
                         .Where(s => s is > -1 and var x && x <= Math.Min(a.SoonSec, 8)).Min() is { } left)
                shown.Add(new Banner(a, Stage.Soon, Math.Max(0, left), null));
        }
        return (shown.OrderBy(b => b.Stage == Stage.Now ? 0 : 1).ThenBy(b => b.LeftSec ?? 0).ToList(), fired);
    }
}
