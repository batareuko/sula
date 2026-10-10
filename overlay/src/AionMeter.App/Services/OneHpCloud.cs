using System.Collections.Concurrent;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using AionMeter.Core.Combat;
using AionMeter.Core.OneHp;

namespace AionMeter.App.Services;

/// <summary>Place among 1 HP members on this boss (each member's best kill), or why the upload failed.</summary>
public sealed record UploadResult(int Place, int Total, int? TopPct, bool PersonalBest, string? Error);

/// <summary>
/// 1 HP: the overlay's link to guide.sulaslova.com (Supabase Edge Functions, public anon key — the same one the site ships).
/// <list type="bullet">
/// <item>Party gear: item level and combat power of players in the fight, looked up by name through the site's
/// <c>aion-lookup</c> function (which reads the official character pages). One request every few seconds, cached.</item>
/// <item>Guild rating: after a boss kill, YOUR result only (character, class, DPS, damage, fight length, place) goes to
/// <c>dps-upload</c> with the personal overlay key the member created on the site. Nothing about other players is sent.</item>
/// </list>
/// </summary>
public sealed class OneHpCloud : IDisposable
{
    public const string SupabaseUrl = "https://mbwulcbfjtdmcywiakmn.supabase.co";
    private const string AnonKey = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6Im1id3VsY2JmanRkbWN5d2lha21uIiwicm9sZSI6ImFub24iLCJpYXQiOjE3OTExOTc4NDAsImV4cCI6MjEwNjc3Mzg0MH0.yX-vh6aOZV3V1FjYfJsFlEnj1ubvLGlTlmkUzH1WSQQ";
    private static readonly TimeSpan GearTtl = TimeSpan.FromHours(6);
    private static readonly TimeSpan MissTtl = TimeSpan.FromMinutes(30);

    private readonly AppSettings _settings;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private readonly ConcurrentDictionary<string, (GearInfo? Info, DateTime At)> _gear = new();
    private readonly ConcurrentQueue<(string Name, int Server)> _queue = new();
    private readonly ConcurrentDictionary<string, byte> _queued = new();
    private readonly ConcurrentDictionary<Guid, UploadResult> _results = new();
    private readonly CancellationTokenSource _cts = new();
    private Task? _worker;

    public OneHpCloud(AppSettings settings)
    {
        _settings = settings;
        _http.DefaultRequestHeaders.Add("apikey", AnonKey);
        _http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", AnonKey);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("1HP-Overlay/" + (typeof(OneHpCloud).Assembly.GetName().Version?.ToString(3) ?? "1"));
    }

    private static string GearKey(string name, int server) => name.ToLowerInvariant() + "@" + server;

    /// <summary>Known gear of a player, or null (then a lookup is queued). "#123" players have no name yet.</summary>
    public GearInfo? GearFor(string name, int serverId)
    {
        if (!_settings.OneHpGear || string.IsNullOrWhiteSpace(name) || name.StartsWith('#')) return null;
        var key = GearKey(name, serverId);
        if (_gear.TryGetValue(key, out var hit) && DateTime.UtcNow - hit.At < (hit.Info is null ? MissTtl : GearTtl)) return hit.Info;
        if (_queued.TryAdd(key, 0))
        {
            _queue.Enqueue((name, serverId));
            _worker ??= Task.Run(() => GearLoop(_cts.Token));
        }
        return hit.Info; // stale value until refreshed
    }

    private async Task GearLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            if (!_queue.TryDequeue(out var job))
            {
                try { await Task.Delay(1_000, ct).ConfigureAwait(false); } catch (OperationCanceledException) { break; }
                continue;
            }
            var key = GearKey(job.Name, job.Server);
            GearInfo? info = null;
            try
            {
                using var res = await _http.PostAsJsonAsync(SupabaseUrl + "/functions/v1/aion-lookup", new { name = job.Name }, ct).ConfigureAwait(false);
                if (res.IsSuccessStatusCode)
                    info = GuildData.ParseGear(await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false), job.Server);
                else if ((int)res.StatusCode == 429)
                {
                    _queued.TryRemove(key, out _); // try again later
                    await Task.Delay(20_000, ct).ConfigureAwait(false);
                    continue;
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { Log.Info("Gear lookup " + job.Name + ": " + ex.Message); }
            _gear[key] = (info, DateTime.UtcNow);
            _queued.TryRemove(key, out _);
            try { await Task.Delay(3_000, ct).ConfigureAwait(false); } catch (OperationCanceledException) { break; }
        }
    }

    public UploadResult? ResultFor(Guid encounterId) => _results.TryGetValue(encounterId, out var r) ? r : null;

    public async Task UploadAsync(FightRecord record)
    {
        var key = _settings.OneHpKey?.Trim();
        if (!_settings.OneHpUpload || string.IsNullOrEmpty(key) || GuildData.BuildRecord(record.Summary) is not { } payload) return;
        try
        {
            using var res = await _http.PostAsJsonAsync(SupabaseUrl + "/functions/v1/dps-upload", new { key, record = payload }).ConfigureAwait(false);
            var text = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
            using var doc = JsonDocument.Parse(text);
            var r = doc.RootElement;
            if (!res.IsSuccessStatusCode)
            {
                var error = r.TryGetProperty("error", out var e) ? e.GetString() : res.StatusCode.ToString();
                _results[record.Summary.Id] = new UploadResult(0, 0, null, false, error);
                Log.Info("1 HP upload: " + error);
                return;
            }
            _results[record.Summary.Id] = new UploadResult(
                r.TryGetProperty("place", out var p) ? p.GetInt32() : 0,
                r.TryGetProperty("total", out var t) ? t.GetInt32() : 0,
                r.TryGetProperty("topPct", out var top) && top.ValueKind == JsonValueKind.Number ? top.GetInt32() : null,
                r.TryGetProperty("personalBest", out var pb) && pb.ValueKind == JsonValueKind.True,
                null);
        }
        catch (Exception ex)
        {
            Log.Info("1 HP upload failed: " + ex.Message);
            _results[record.Summary.Id] = new UploadResult(0, 0, null, false, "network");
        }
    }

    // ------------------------------------------------------------ field boss times → the site (per server)

    private readonly object _bossGate = new();
    private string? _bossSent;            // what the site last got: nothing is sent twice
    private object? _bossPending;
    private string? _bossPendingSig;
    private long _bossLastMs;
    private bool _bossScheduled;

    /// <summary>
    /// 1 HP: the in-game boss list's times for this server go to the site (boss_kills, source 'game'), so the site's
    /// and Discord's timers follow the game instead of "killed + cycle". Only bosses the list itself describes; at
    /// most once a minute, and only when something changed.
    /// </summary>
    public void SyncBosses(int server, string? by, IReadOnlyList<BossTimer> timers)
    {
        var key = _settings.OneHpKey?.Trim();
        if (!_settings.OneHpBossSync || string.IsNullOrEmpty(key) || server < 1000) return;
        var list = timers
            .Where(t => t.NpcCode > 0 && t.FromGame)
            .Select(t => t.ListedAlive
                ? t.ListedTime is { } since ? new { code = t.NpcCode, alive = true, at = since.ToUnixTimeMilliseconds() } : null
                : t.NextSpawn is { } back ? new { code = t.NpcCode, alive = false, at = back.ToUnixTimeMilliseconds() } : null)
            .Where(x => x is not null)
            .OrderBy(x => x!.code)
            .Take(120)
            .ToList();
        if (list.Count == 0) return;
        var sig = server + ":" + string.Join(",", list.Select(x => $"{x!.code}{(x.alive ? "+" : "-")}{x.at / 60_000}"));
        lock (_bossGate)
        {
            if (sig == _bossSent || sig == _bossPendingSig) return;
            _bossPending = new { key, bosses = new { serverId = server, by, list } };
            _bossPendingSig = sig;
            if (_bossScheduled) return;
            _bossScheduled = true;
        }
        _ = SendBossesAsync();
    }

    private async Task SendBossesAsync()
    {
        try
        {
            var wait = 60_000 - (Environment.TickCount64 - _bossLastMs);
            if (wait > 0) await Task.Delay(TimeSpan.FromMilliseconds(wait), _cts.Token).ConfigureAwait(false);
            object? body;
            string? sig;
            lock (_bossGate)
            {
                body = _bossPending;
                sig = _bossPendingSig;
                _bossPending = null;
                _bossPendingSig = null;
                _bossScheduled = false;
            }
            if (body is null) return;
            _bossLastMs = Environment.TickCount64;
            using var res = await _http.PostAsJsonAsync(SupabaseUrl + "/functions/v1/dps-upload", body, _cts.Token).ConfigureAwait(false);
            if (res.IsSuccessStatusCode) lock (_bossGate) _bossSent = sig;
            else Log.Info("1 HP boss times: " + (int)res.StatusCode + " " + await res.Content.ReadAsStringAsync().ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Log.Info("1 HP boss times not sent: " + ex.Message);
            lock (_bossGate) _bossScheduled = false;
        }
    }

    /// <summary>1 HP: a field boss time another guild overlay read from the in-game list on this server.</summary>
    public sealed record SharedBoss(int Code, bool Alive, DateTimeOffset At, DateTimeOffset RecordedAt, string? By);

    private sealed record SharedRow(string boss_id, DateTimeOffset respawn_at, bool alive, DateTimeOffset killed_at, string? by_name);
    private sealed record SharedReply(List<SharedRow>? bosses);

    /// <summary>
    /// 1 HP shared timers: the boss times of <paramref name="server"/> that guild overlays sent (needs a key). For a living
    /// boss At is when it appeared; for a dead one At is when it comes back and RecordedAt when the list was read.
    /// </summary>
    public async Task<IReadOnlyList<SharedBoss>?> FetchBossesAsync(int server)
    {
        var key = _settings.OneHpKey?.Trim();
        if (!_settings.OneHpBossSync || string.IsNullOrEmpty(key) || server < 1000) return null;
        try
        {
            using var res = await _http.PostAsJsonAsync(SupabaseUrl + "/functions/v1/dps-upload",
                new { key, bossesGet = new { serverId = server } }, _cts.Token).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode)
            {
                Log.Info("1 HP shared boss times: " + (int)res.StatusCode);
                return null;
            }
            var reply = await res.Content.ReadFromJsonAsync<SharedReply>(_cts.Token).ConfigureAwait(false);
            return reply?.bosses?
                .Where(r => int.TryParse(r.boss_id, out _))
                .Select(r => new SharedBoss(int.Parse(r.boss_id), r.alive, r.respawn_at, r.alive ? r.respawn_at : r.killed_at, r.by_name))
                .ToList();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Info("1 HP shared boss times not read: " + ex.Message);
            return null;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    /// <summary>Settings → "Check": does the site know this key?</summary>
    public async Task<bool?> CheckKeyAsync(string key)
    {
        try
        {
            using var res = await _http.PostAsJsonAsync(SupabaseUrl + "/functions/v1/dps-upload", new { key = key.Trim(), check = true }).ConfigureAwait(false);
            return res.IsSuccessStatusCode;
        }
        catch { return null; }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _http.Dispose();
    }
}
