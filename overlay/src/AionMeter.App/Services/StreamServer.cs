using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using AionMeter.Core;
using AionMeter.Core.Combat;

namespace AionMeter.App.Services;

/// <summary>
/// 1 HP: the meter as a web page for a stream — OBS (or any streaming app) adds http://127.0.0.1:7799/ as a browser
/// source. Only this computer can open it. <c>/</c> shows the overlay itself, pixel for pixel: the overlay renders its
/// card to <c>/frame.png</c> while a page asks for it. <c>/card</c> is a lighter HTML card built from <c>/data</c>
/// (the live fight, or your name, combat power and ping out of combat).
/// </summary>
public sealed class StreamServer : IDisposable
{
    public const int DefaultPort = 7799;

    private readonly MeterService _meter;
    private HttpListener? _listener;
    private int _port;
    private volatile bool _streamEnabled;
    private volatile bool _stateEnabled;

    // Set by the overlay (UI thread): what its one-line card shows out of combat.
    private volatile string[] _idle = ["", "", ""];

    // 1 HP: the overlay's last frame (PNG) and when a page last asked for one.
    private volatile byte[] _frame = EmptyPng;
    private long _frameAskedAt = long.MinValue / 2;

    /// <summary>A page showed the overlay in the last few seconds: the overlay keeps rendering frames for it.</summary>
    public bool WantsFrame => Running && Environment.TickCount64 - Interlocked.Read(ref _frameAskedAt) < 3_000;

    /// <summary>The overlay's card as it is on screen; <see cref="EmptyPng"/> while the overlay is hidden.</summary>
    public void SetFrame(byte[] png) => _frame = png;

    // 1 HP: frames of the other windows a stream can show (by name: "party" → /party, /party.png) and when one was asked for.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte[]> _frames = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, long> _framesAsked = new();

    /// <summary>A page showed the window <paramref name="name"/> in the last few seconds: it keeps rendering frames.</summary>
    public bool WantsWindowFrame(string name) => Running && Environment.TickCount64 - _framesAsked.GetValueOrDefault(name, long.MinValue / 2) < 3_000;

    public void SetWindowFrame(string name, byte[] png) => _frames[name] = png;

    /// <summary>A 1×1 transparent PNG.</summary>
    public static readonly byte[] EmptyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGNgYGBgAAAABQABpfZFQAAAAABJRU5ErkJggg==");

    public StreamServer(MeterService meter) => _meter = meter;

    public string Url => $"http://127.0.0.1:{(_port > 0 ? _port : DefaultPort)}/";

    public bool Running => _listener?.IsListening == true;

    /// <summary>
    /// Starts, stops or moves the page per the settings. <paramref name="stateEnabled"/> adds the JSON state endpoint
    /// (<c>/state</c>) to the same listener: the two need one listener, not two on one port.
    /// </summary>
    public void Apply(bool streamEnabled, bool stateEnabled, int port)
    {
        port = port is > 1024 and < 65536 ? port : DefaultPort;
        if ((streamEnabled || stateEnabled) && Running && port == _port)
        {
            _streamEnabled = streamEnabled;
            _stateEnabled = stateEnabled;
            return;
        }
        Stop();
        _streamEnabled = streamEnabled;
        _stateEnabled = stateEnabled;
        if (!streamEnabled && !stateEnabled) return;
        try
        {
            var listener = new HttpListener();
            // 1 HP: loopback only — the "localhost" prefix also binds the :: (all interfaces) wildcard on Windows.
            listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            listener.Prefixes.Add($"http://[::1]:{port}/");
            listener.Start();
            _listener = listener;
            _port = port;
            _ = Task.Run(() => Serve(listener));
            Log.Info($"Stream page on http://127.0.0.1:{port}/");
        }
        catch (Exception ex) when (ex is HttpListenerException or InvalidOperationException)
        {
            Log.Error($"Stream page could not start on port {port}", ex);
        }
    }

    public void SetIdleLine(string name, string power, string net) => _idle = [name, power, net];

    private async Task Serve(HttpListener listener)
    {
        while (listener.IsListening)
        {
            HttpListenerContext ctx;
            try
            {
                ctx = await listener.GetContextAsync();
            }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or InvalidOperationException)
            {
                return;
            }
            try
            {
                var path = ctx.Request.Url?.AbsolutePath ?? "/";
                if (path == "/frame.png") Interlocked.Exchange(ref _frameAskedAt, Environment.TickCount64);
                if (path == "/party.png") _framesAsked["party"] = Environment.TickCount64;
                var (bytes, type) = path switch
                {
                    // 1 HP: the live character / party state for another program on this computer (AutoHotkey and such).
                    "/state" when _stateEnabled => (Encoding.UTF8.GetBytes(State()), "application/json; charset=utf-8"),
                    _ when !_streamEnabled => ((byte[]?)null, "text/plain"),
                    "/frame.png" => (_frame, "image/png"),
                    "/data" => (Encoding.UTF8.GetBytes(Data()), "application/json; charset=utf-8"),
                    "/" or "/index.html" => (Encoding.UTF8.GetBytes(MirrorPage), "text/html; charset=utf-8"),
                    "/party" => (Encoding.UTF8.GetBytes(MirrorPage.Replace("/frame.png", "/party.png").Replace("1 HP DPS", "1 HP Party")), "text/html; charset=utf-8"),
                    "/party.png" => (_frames.GetValueOrDefault("party") ?? EmptyPng, "image/png"),
                    "/card" => (Encoding.UTF8.GetBytes(Page), "text/html; charset=utf-8"),
                    _ when path.StartsWith("/class/", StringComparison.Ordinal) => (ClassEmblem(path[7..]), "image/png"),
                    _ => ((byte[]?)null, "text/plain"),
                };
                if (bytes is null) ctx.Response.StatusCode = 404;
                else
                {
                    ctx.Response.ContentType = type;
                    ctx.Response.Headers["Cache-Control"] = "no-store";
                    ctx.Response.ContentLength64 = bytes.Length;
                    await ctx.Response.OutputStream.WriteAsync(bytes);
                }
                ctx.Response.Close();
            }
            catch (Exception ex) when (ex is HttpListenerException or IOException or ObjectDisposedException)
            {
            }
        }
    }

    private static readonly Dictionary<string, byte[]?> Emblems = new();

    /// <summary>1 HP: a class emblem for the stream card (/class/gladiator.png), from the app's own resources.</summary>
    private static byte[]? ClassEmblem(string file)
    {
        var cls = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
        if (!System.Text.RegularExpressions.Regex.IsMatch(cls, "^[a-z]{3,16}$")) return null;
        lock (Emblems)
        {
            if (Emblems.TryGetValue(cls, out var hit)) return hit;
            byte[]? bytes = null;
            try
            {
                var info = System.Windows.Application.GetResourceStream(new Uri($"pack://application:,,,/Assets/Classes/{cls}.png"));
                if (info is not null)
                {
                    using var ms = new MemoryStream();
                    info.Stream.CopyTo(ms);
                    bytes = ms.ToArray();
                }
            }
            catch (IOException)
            {
            }
            Emblems[cls] = bytes;
            return bytes;
        }
    }

    /// <summary>
    /// 1 HP: <c>/state</c> — the live character and party state as flat JSON for another local program. Every field is
    /// always present; what the game did not tell the meter is null. <c>{"ok":false}</c> while there is nothing live.
    /// </summary>
    private string State()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var s = _meter.Tracker.State(now);
        if (s is null) return """{"ok":false}""";

        static object Buff(LiveBuff b) => new
        {
            id = b.Id,
            name = b.Name,
            remainMs = b.RemainMs,
            stacks = b.Stacks,
            dispellable = b.Dispellable,
        };

        object? casting = s.Me.Casting is { } c
            ? new { skillId = c.SkillId, name = c.Name, chargeLevel = c.ChargeLevel, endsInMs = c.EndsInMs }
            : null;
        object? target = s.Target is { } t
            ? new { name = t.Name, hp = t.Hp, hpMax = t.HpMax, isEnemy = t.IsEnemy, distance = t.Distance }
            : null;

        return JsonSerializer.Serialize(new
        {
            ok = true,
            ts = s.TsMs,
            me = new
            {
                name = s.Me.Name,
                cls = s.Me.Class,
                alive = s.Me.Alive,
                hp = s.Me.Hp,
                hpMax = s.Me.HpMax,
                mp = s.Me.Mp,
                mpMax = s.Me.MpMax,
                inCombat = s.Me.InCombat,
                casting,
                buffs = s.Me.Buffs.Select(Buff),
                debuffs = s.Me.Debuffs.Select(Buff),
                skills = s.Me.Skills.Select(k => new
                {
                    id = k.Id,
                    name = k.Name,
                    cdRemainMs = k.CdRemainMs,
                    cdTotalMs = k.CdTotalMs,
                    charges = k.Charges,
                    chargesMax = k.ChargesMax,
                    toggleOn = k.ToggleOn,
                }),
            },
            target,
            party = s.Party.Select(m => new
            {
                name = m.Name,
                cls = m.Class,
                alive = m.Alive,
                hp = m.Hp,
                hpMax = m.HpMax,
                inRange = m.InRange,
                distance = m.Distance,
                debuffs = m.Debuffs.Select(Buff),
            }),
        });
    }

    /// <summary>The live fight (or the last one, with how long ago it ended) and the one-line card.</summary>
    private string Data()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var snap = _meter.Tracker.Snapshot(null, now);
        var t = UiText.Current;
        var idle = _idle;
        object? fight = null;
        if (snap is not null)
        {
            var endedAgo = snap.IsActive ? 0 : now - (snap.StartedAt.ToUnixTimeMilliseconds() + snap.ClockMs);
            var top = snap.Combatants.Where(c => !c.IsUnknownSummons).Select(c => c.Dps).DefaultIfEmpty(0).Max();
            var run = _meter.Dummies.Compare(snap, _meter.IsDummy);
            fight = new
            {
                title = snap.Title,
                clock = Format.ClockShort(snap.ClockMs),
                active = snap.IsActive,
                endedAgoMs = endedAgo,
                boss = snap.Boss is { } b ? new { name = b.Name, hp = b.HpFraction } : null,
                partyDps = Format.Compact(snap.PartyDps),
                run = run is null ? null : new
                {
                    n = run.Run,
                    label = string.Format(t.DummyRun, run.Run),
                    vsBest = run.VsBest,
                    vsLast = run.VsLast,
                    best = run.IsNewBest,
                },
                rows = snap.Combatants.Take(_meter.Settings.MaxRows).Select(c => new
                {
                    name = c.Name,
                    cls = c.Class.ToString().ToLowerInvariant(),
                    self = c.IsSelf,
                    dps = Format.Compact(c.Dps),
                    damage = Format.Compact(c.Damage),
                    share = Format.Share(c.Share),
                    fill = top > 0 ? c.Dps / top : 0,
                    power = c.Power > 0 ? Format.Power(c.Power) : "",
                }),
            };
        }
        return JsonSerializer.Serialize(new { fight, me = new { name = idle[0], power = idle[1], net = idle[2] } });
    }

    public void Stop()
    {
        try
        {
            _listener?.Close();
        }
        catch (ObjectDisposedException)
        {
        }
        _listener = null;
    }

    public void Dispose() => Stop();

    /// <summary>
    /// The overlay as it is on screen. <c>?fps=</c> sets how often the picture changes (1–10, 5 by default; the
    /// overlay itself redraws 5 times a second), <c>?scale=</c> its size.
    /// </summary>
    private const string MirrorPage = """
<!doctype html>
<html lang="uk"><head><meta charset="utf-8"><title>1 HP DPS</title>
<style>
  html,body{margin:0;background:transparent;overflow:hidden}
  img{display:block;transform-origin:0 0}
</style></head>
<body><img id="f" alt="">
<script>
const q = new URLSearchParams(location.search);
const fps = Math.min(10, Math.max(1, +q.get('fps') || 5)), scale = +q.get('scale') || 1;
const img = document.getElementById('f');
if (scale !== 1) img.style.transform = 'scale(' + scale + ')';
let url = null;
async function tick() {
  try {
    const r = await fetch('/frame.png', { cache: 'no-store' });
    if (r.ok) {
      const next = URL.createObjectURL(await r.blob());
      await new Promise(done => { img.onload = img.onerror = done; img.src = next; });
      if (url) URL.revokeObjectURL(url);
      url = next;
    }
  } catch { }
  setTimeout(tick, 1000 / fps);
}
tick();
</script></body></html>
""";

    /// <summary>
    /// 1 HP: the light HTML card, frameless like the overlay — a dark header with the fight and the boss's HP, then the
    /// players as floating rows (class emblem on a tile, shimmering name, combat power, DPS, share in gold).
    /// <c>?idle=1</c> keeps your name and ping up out of combat, <c>?fold=</c> how many seconds a finished fight stays.
    /// </summary>
    private const string Page = """
<!doctype html>
<html lang="uk"><head><meta charset="utf-8"><title>1 HP DPS</title>
<style>
  html,body{margin:0;background:transparent;font-family:"Segoe UI",system-ui,sans-serif;color:#EEF2F8;overflow:hidden}
  #card{margin:6px;width:calc(100% - 12px);box-sizing:border-box;transition:opacity .4s}
  .hidden{opacity:0}
  .panel{padding:8px 12px 9px;border-radius:12px;background:rgba(14,19,32,.88);box-shadow:0 2px 10px rgba(0,0,0,.35)}
  .head{display:flex;align-items:center;gap:8px;font-weight:600}
  .title{font-size:17px;font-weight:800;flex:1;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
  .clock{font-variant-numeric:tabular-nums;font-weight:700;font-size:15px;color:#C9D2E0}
  .dot{width:8px;height:8px;border-radius:50%;background:#EF4444;box-shadow:0 0 6px #EF4444}
  .done .dot{background:#64748B;box-shadow:none}
  .run{font-size:12px;color:#BFF5D6;margin-top:3px}
  .run .neg{color:#FCA5A5}.run .pos{color:#86EFAC}
  .hp{display:flex;align-items:center;gap:8px;margin-top:6px;font-size:12px;font-variant-numeric:tabular-nums;color:#C9D2E0}
  .bar{flex:1;height:6px;border-radius:3px;background:rgba(255,255,255,.12);overflow:hidden}
  .bar i{display:block;height:100%;background:linear-gradient(90deg,#22C55E,#86EFAC)}
  .hp b{color:#FFD34D}
  .rows{margin-top:6px}
  .row{position:relative;height:34px;margin-bottom:4px;border-radius:9px;background:rgba(14,19,32,.82);overflow:hidden;display:flex;
       align-items:center;font-size:15px;font-weight:800;box-shadow:0 1px 6px rgba(0,0,0,.3)}
  .row>i{position:absolute;inset:0 auto 0 0;opacity:.85;border-radius:9px}
  .tile{position:relative;width:28px;height:28px;margin-left:4px;border-radius:7px;background:rgba(8,11,20,.6);display:flex;
        align-items:center;justify-content:center}
  .tile img{width:24px;height:24px}
  .row b{position:relative;flex:1;padding-left:8px;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
  .row b span{background:linear-gradient(90deg,var(--a),#fff,var(--b),var(--a));background-size:300% 100%;-webkit-background-clip:text;
              background-clip:text;color:transparent;animation:shine 4.5s linear infinite}
  .row.self b span{--a:#FFE07A;--b:#FF8AD0;background-image:linear-gradient(90deg,#FFE07A,#FF8AD0,#A98BFF,#FFE07A);animation-duration:3.2s}
  @keyframes shine{from{background-position:0 0}to{background-position:300% 0}}
  .row b small{font-size:11px;color:#C8FFF6;font-weight:600;margin-left:7px}
  .row em{position:relative;font-style:normal;font-variant-numeric:tabular-nums;padding:0 10px;text-align:right}
  .row em.share{color:#FFD34D;min-width:48px}
  .row.self{outline:1.5px solid rgba(110,139,255,.9)}
  .me{display:flex;align-items:center;gap:9px;font-weight:700;font-size:14px}
  .me .pw{background:rgba(45,212,191,.15);color:#BFF7EE;padding:1px 8px;border-radius:5px;font-size:13px}
  .me .net{margin-left:auto;font-weight:600;font-size:12px;color:#C9D2E0}
  .gladiator{background:linear-gradient(90deg,#1FA2C8,rgba(31,162,200,.55))}.templar{background:linear-gradient(90deg,#3B7CE6,rgba(59,124,230,.55))}
  .ranger{background:linear-gradient(90deg,#1CAE7E,rgba(28,174,126,.55))}.assassin{background:linear-gradient(90deg,#46B84E,rgba(70,184,78,.55))}
  .sorcerer{background:linear-gradient(90deg,#864CE0,rgba(134,76,224,.55))}.elementalist{background:linear-gradient(90deg,#C23FC4,rgba(194,63,196,.55))}
  .cleric{background:linear-gradient(90deg,#D8A22A,rgba(216,162,42,.55))}.chanter{background:linear-gradient(90deg,#EA7D2C,rgba(234,125,44,.55))}
  .brawler{background:linear-gradient(90deg,#D6333E,rgba(214,51,62,.55))}.unknown{background:linear-gradient(90deg,#5E6B84,rgba(94,107,132,.55))}
  .n-gladiator{--a:#9FE3F5;--b:#5CC6E4}.n-templar{--a:#A9C6FF;--b:#6E9DF0}.n-ranger{--a:#9EEBCF;--b:#4FCB9E}.n-assassin{--a:#B5EDB8;--b:#6FD176}
  .n-sorcerer{--a:#CDB3FF;--b:#A47BEF}.n-elementalist{--a:#F1B3F2;--b:#DB6EDC}.n-cleric{--a:#FFE3A3;--b:#E8BD58}.n-chanter{--a:#FFCCA3;--b:#F29A57}
  .n-brawler{--a:#FFB0B5;--b:#E9646D}.n-unknown{--a:#D5DBE6;--b:#9AA6BC}
</style></head>
<body><div id="card" class="hidden"></div>
<script>
const q = new URLSearchParams(location.search);
const keepIdle = q.get('idle') === '1', foldMs = (+q.get('fold') || 15) * 1000;
const card = document.getElementById('card');
const el = (tag, cls, text) => { const e = document.createElement(tag); if (cls) e.className = cls; if (text != null) e.textContent = text; return e; };
const pct = v => v == null ? '—' : (v >= 0 ? '+' : '−') + Math.abs(v).toFixed(1) + '%';
function render(d) {
  const f = d.fight, open = f && (f.active || f.endedAgoMs < foldMs);
  card.replaceChildren();
  if (open) {
    card.className = f.active ? '' : 'done';
    const panel = el('div', 'panel');
    const head = el('div', 'head'); head.append(el('span', 'dot'), el('span', 'title', f.title), el('span', 'clock', f.clock)); panel.append(head);
    if (f.run) {
      const r = el('div', 'run', f.run.label + (f.run.best ? ' · ★' : ''));
      for (const [k, v] of [['рекорд', f.run.vsBest], ['попер.', f.run.vsLast]])
        if (v != null) { r.append(' · ' + k + ' '); r.append(el('span', v < 0 ? 'neg' : 'pos', pct(v))); }
      panel.append(r);
    }
    if (f.boss) {
      const hp = el('div', 'hp'), bar = el('div', 'bar'), i = el('i'); i.style.width = (f.boss.hp * 100) + '%'; bar.append(i);
      if (f.boss.name !== f.title) hp.append(el('span', null, f.boss.name)); hp.append(bar, el('b', null, (f.boss.hp * 100).toFixed(1) + '%')); panel.append(hp);
    }
    card.append(panel);
    const rows = el('div', 'rows');
    for (const r of f.rows) {
      const row = el('div', 'row' + (r.self ? ' self' : '')); const fill = el('i', r.cls); fill.style.width = Math.max(4, r.fill * 100) + '%';
      const tile = el('div', 'tile'); if (r.cls !== 'unknown') { const img = el('img'); img.src = '/class/' + r.cls + '.png'; tile.append(img); }
      const name = el('b', 'n-' + r.cls); name.append(el('span', null, r.name)); if (r.power) name.append(el('small', null, '⚡ ' + r.power));
      row.append(fill, tile, name, el('em', null, r.dps + '/s'), el('em', 'share', r.share)); rows.append(row);
    }
    card.append(rows);
  } else if (keepIdle && d.me.name) {
    card.className = '';
    const panel = el('div', 'panel'), me = el('div', 'me'); me.append(el('span', null, d.me.name));
    if (d.me.power) me.append(el('span', 'pw', '⚡ ' + d.me.power));
    me.append(el('span', 'net', d.me.net)); panel.append(me); card.append(panel);
  } else card.className = 'hidden';
}
async function tick() { try { render(await (await fetch('/data', { cache: 'no-store' })).json()); } catch { card.className = 'hidden'; } }
tick(); setInterval(tick, 500);
</script></body></html>
""";
}
