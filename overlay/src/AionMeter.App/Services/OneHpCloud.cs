using System.Collections.Concurrent;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using AionMeter.Core.Combat;

namespace AionMeter.App.Services;

public sealed record GearInfo(int ItemLevel, long CombatPower);

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
                    info = ParseGear(await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false), job.Server);
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

    /// <summary>
    /// aion-lookup answers { character, game } for a single match or { matches: [...] } for several:
    /// pick the one on the player's server (any, when the server is unknown and there is only one).
    /// </summary>
    public static GearInfo? ParseGear(string json, int serverId)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var candidates = new List<JsonElement>();
        if (root.TryGetProperty("character", out var single) && single.ValueKind == JsonValueKind.Object) candidates.Add(single);
        if (root.TryGetProperty("matches", out var list) && list.ValueKind == JsonValueKind.Array) candidates.AddRange(list.EnumerateArray());
        static int Int(JsonElement e, string p) => e.TryGetProperty(p, out var v) && v.TryGetInt32(out var n) ? n : 0;
        static long Long(JsonElement e, string p) => e.TryGetProperty(p, out var v) && v.TryGetInt64(out var n) ? n : 0;
        var pick = serverId != 0
            ? candidates.Where(c => Int(c, "serverId") == serverId).Cast<JsonElement?>().FirstOrDefault()
            : candidates.Count == 1 ? candidates[0] : null;
        return pick is { } c2 && Int(c2, "itemLevel") > 0 ? new GearInfo(Int(c2, "itemLevel"), Long(c2, "combatPower")) : null;
    }

    public UploadResult? ResultFor(Guid encounterId) => _results.TryGetValue(encounterId, out var r) ? r : null;

    /// <summary>The 1 HP payload of a finished fight: only the local player's own numbers, only for a killed boss.</summary>
    public static object? BuildRecord(EncounterSnapshot s)
    {
        if (s.Reason != EncounterEndReason.Kill || s.Boss is not { NpcCode: > 0 } boss) return null;
        var players = s.Combatants.Where(c => !c.IsUnknownSummons).ToList();
        var self = players.FirstOrDefault(c => c.IsSelf);
        if (self is null || self.Damage <= 0 || self.Name.StartsWith('#') || s.CombatMs < 5_000) return null;
        return new
        {
            character = self.Name,
            @class = self.Class.ToString(),
            serverId = self.ServerId,
            bossCode = boss.NpcCode,
            bossName = string.IsNullOrWhiteSpace(boss.Name) ? s.Title : boss.Name,
            zone = s.Zone,
            dps = Math.Round(self.Dps, 1),
            damage = self.Damage,
            durationMs = s.CombatMs,
            partySize = players.Count,
            place = players.IndexOf(self) + 1,
            foughtAt = s.StartedAt.ToUniversalTime().ToString("o"),
        };
    }

    public async Task UploadAsync(FightRecord record)
    {
        var key = _settings.OneHpKey?.Trim();
        if (!_settings.OneHpUpload || string.IsNullOrEmpty(key) || BuildRecord(record.Summary) is not { } payload) return;
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
