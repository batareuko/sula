using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using AionMeter.Core;
using AionMeter.Core.Combat;

namespace AionMeter.App.Services;

/// <summary>
/// 1 HP: the meter as a web page for a stream — OBS (or any streaming app) adds http://localhost:7799/ as a browser
/// source. Only this computer can open it. The page asks for <c>/data</c> twice a second: the live fight, or your name,
/// combat power and ping out of combat.
/// </summary>
public sealed class StreamServer : IDisposable
{
    public const int DefaultPort = 7799;

    private readonly MeterService _meter;
    private HttpListener? _listener;
    private int _port;

    // Set by the overlay (UI thread): what its one-line card shows out of combat.
    private volatile string[] _idle = ["", "", ""];

    public StreamServer(MeterService meter) => _meter = meter;

    public string Url => $"http://localhost:{(_port > 0 ? _port : DefaultPort)}/";

    public bool Running => _listener?.IsListening == true;

    /// <summary>Starts, stops or moves the page per the settings.</summary>
    public void Apply(bool enabled, int port)
    {
        port = port is > 1024 and < 65536 ? port : DefaultPort;
        if (enabled && Running && port == _port) return;
        Stop();
        if (!enabled) return;
        try
        {
            var listener = new HttpListener();
            listener.Prefixes.Add($"http://localhost:{port}/");
            listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            listener.Start();
            _listener = listener;
            _port = port;
            _ = Task.Run(() => Serve(listener));
            Log.Info($"Stream page on http://localhost:{port}/");
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
                var (body, type) = path switch
                {
                    "/data" => (Data(), "application/json"),
                    "/" or "/index.html" => (Page, "text/html"),
                    _ => ((string?)null, "text/plain"),
                };
                if (body is null) ctx.Response.StatusCode = 404;
                else
                {
                    var bytes = Encoding.UTF8.GetBytes(body);
                    ctx.Response.ContentType = type + "; charset=utf-8";
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

    private const string Page = """
<!doctype html>
<html lang="uk"><head><meta charset="utf-8"><title>1 HP DPS</title>
<style>
  html,body{margin:0;background:transparent;font-family:"Segoe UI",system-ui,sans-serif;color:#EEF2F8;overflow:hidden}
  .card{margin:6px;padding:10px 12px 8px;border-radius:12px;background:rgba(14,19,32,.86);border:1px solid rgba(255,255,255,.15);
        transition:opacity .4s;width:calc(100% - 12px);box-sizing:border-box}
  .hidden{opacity:0}
  .head{display:flex;align-items:center;gap:8px;font-weight:600}
  .title{font-size:17px;flex:1;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
  .clock{font-variant-numeric:tabular-nums;font-weight:700;font-size:16px}
  .dot{width:8px;height:8px;border-radius:50%;background:#EF4444}
  .done .dot{background:#64748B}
  .run{font-size:12px;color:#BFF5D6;margin-top:3px}
  .run .neg{color:#FCA5A5}.run .pos{color:#86EFAC}
  .hp{position:relative;height:18px;border-radius:6px;background:#2A1420;margin-top:7px;overflow:hidden;font-size:12px}
  .hp i{position:absolute;inset:0 auto 0 0;background:linear-gradient(90deg,#E11D48,#FB7185)}
  .hp span{position:relative;padding:0 8px;line-height:18px}
  .rows{margin-top:7px}
  .row{position:relative;height:26px;margin-bottom:2px;border-radius:6px;background:rgba(255,255,255,.08);overflow:hidden;display:flex;
       align-items:center;font-size:14px;font-weight:700}
  .row i{position:absolute;inset:0 auto 0 0;opacity:.9}
  .row b{position:relative;flex:1;padding-left:9px;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
  .row b small{font-size:11px;color:#C8FFF6;font-weight:600;margin-left:6px}
  .row em{position:relative;font-style:normal;font-variant-numeric:tabular-nums;padding:0 9px;min-width:56px;text-align:right}
  .row.self{outline:1.5px solid #6E8BFF}
  .me{display:flex;align-items:center;gap:9px;font-weight:700;font-size:14px}
  .me .pw{background:rgba(45,212,191,.15);color:#BFF7EE;padding:1px 8px;border-radius:5px;font-size:13px}
  .me .net{margin-left:auto;font-weight:600;font-size:12px;color:#C9D2E0}
  .gladiator{background:#1FA2C8}.templar{background:#3B7CE6}.ranger{background:#1CAE7E}.assassin{background:#46B84E}
  .sorcerer{background:#864CE0}.elementalist{background:#C23FC4}.cleric{background:#D8A22A}.chanter{background:#EA7D2C}
  .brawler{background:#D6333E}.unknown{background:#5E6B84}
</style></head>
<body><div id="card" class="card hidden"></div>
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
    card.className = 'card' + (f.active ? '' : ' done');
    const head = el('div', 'head'); head.append(el('span', 'dot'), el('span', 'title', f.title), el('span', 'clock', f.clock)); card.append(head);
    if (f.run) {
      const r = el('div', 'run', f.run.label + (f.run.best ? ' · ★' : ''));
      for (const [k, v] of [['рекорд', f.run.vsBest], ['попер.', f.run.vsLast]])
        if (v != null) { r.append(' · ' + k + ' '); r.append(el('span', v < 0 ? 'neg' : 'pos', pct(v))); }
      card.append(r);
    }
    if (f.boss) { const hp = el('div', 'hp'); const i = el('i'); i.style.width = (f.boss.hp * 100) + '%'; hp.append(i, el('span', null, f.boss.name + ' · ' + Math.round(f.boss.hp * 100) + '%')); card.append(hp); }
    const rows = el('div', 'rows');
    for (const r of f.rows) {
      const row = el('div', 'row' + (r.self ? ' self' : '')); const fill = el('i', r.cls); fill.style.width = (r.fill * 100) + '%';
      const name = el('b', null, r.name); if (r.power) name.append(el('small', null, r.power));
      row.append(fill, name, el('em', null, r.dps), el('em', null, r.damage), el('em', null, r.share)); rows.append(row);
    }
    card.append(rows);
  } else if (keepIdle && d.me.name) {
    card.className = 'card';
    const me = el('div', 'me'); me.append(el('span', null, d.me.name));
    if (d.me.power) me.append(el('span', 'pw', '⚡ ' + d.me.power));
    me.append(el('span', 'net', d.me.net)); card.append(me);
  } else card.className = 'card hidden';
}
async function tick() { try { render(await (await fetch('/data', { cache: 'no-store' })).json()); } catch { card.className = 'card hidden'; } }
tick(); setInterval(tick, 500);
</script></body></html>
""";
}
