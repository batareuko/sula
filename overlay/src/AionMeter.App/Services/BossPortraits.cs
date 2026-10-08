using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AionMeter.Core.Combat;
using AionMeter.Core.OneHp;

namespace AionMeter.App.Services;

/// <summary>
/// Boss portraits (game art © NCSOFT) from MetaBot.GG's public AION 2 database, cached in
/// %AppData%\AionMeter\portraits. Field bosses are stored there under their NPC code; dungeon bosses under a
/// model name, listed in data/boss_portraits.json. Every file is requested once: a portrait that does not exist is
/// remembered for a week, so the site is not asked again on every fight. Lookups never block —
/// <see cref="PortraitReady"/> fires when a download lands.
/// 1 HP: an NPC with no file under its code (most ordinary monsters) is looked up by its English name: MetaBot.GG's
/// monster page (metabot.gg/en/aion-2/monsters/&lt;name&gt;) names its portrait in the page head, so only the first
/// 48 KB of the page is read, once per monster; found names are kept in portraits\names.json.
/// </summary>
public sealed class BossPortraits
{
    private const string BaseUrl = "https://metabot.gg/web/aion2/npcs/";
    private static readonly TimeSpan RetryMissesAfter = TimeSpan.FromDays(7);

    private readonly string _cacheDir = Path.Combine(AppSettings.AppDataDir, "portraits");
    private readonly string _missesFile;
    private readonly Dictionary<int, string> _files = new();               // NPC code → file on the site
    private readonly Dictionary<int, string> _englishNames = new();        // NPC code → English name (page address)
    private readonly ConcurrentDictionary<string, string> _bySlug = new(); // monster page → file on the site
    private readonly string _namesFile;
    private readonly ConcurrentDictionary<string, ImageSource?> _loaded = new();
    private readonly ConcurrentDictionary<string, DateTimeOffset> _misses = new();
    private readonly ConcurrentDictionary<string, byte> _pending = new();
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly object _missesLock = new();

    public BossPortraits(string dataDirectory, bool enabled)
    {
        Enabled = enabled;
        Directory.CreateDirectory(_cacheDir);
        _missesFile = Path.Combine(_cacheDir, "missing.json");
        _namesFile = Path.Combine(_cacheDir, "names.json");
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("AION2DpsMeter/0.1 (portraits credited to MetaBot.GG)");
        LoadMap(Path.Combine(dataDirectory, "boss_portraits.json"));
        LoadMisses();
        LoadEnglishNames(Path.Combine(dataDirectory, "npcs", "en.json"));
        LoadNames();
        RemoveStaleDownloads();
    }

    /// <summary>Downloads cut off by an exit leave *.tmp files behind; old ones are safe to delete.</summary>
    private void RemoveStaleDownloads()
    {
        try
        {
            foreach (var tmp in Directory.EnumerateFiles(_cacheDir, "*.tmp"))
                if (DateTime.UtcNow - File.GetLastWriteTimeUtc(tmp) > TimeSpan.FromMinutes(5))
                    File.Delete(tmp);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    public bool Enabled { get; set; }

    /// <summary>True while downloads are still running (the offscreen render tool waits for them).</summary>
    public bool Busy => !_pending.IsEmpty;

    /// <summary>Raised on a worker thread when a downloaded portrait becomes available.</summary>
    public event Action? PortraitReady;

    /// <summary>Portrait of a fight's boss, or of its main target when there was none.</summary>
    public ImageSource? Of(int bossCode, int targetCode) => Get(bossCode != 0 ? bossCode : targetCode);

    public ImageSource? Of(EncounterSnapshot s) => Of(s.Boss?.NpcCode ?? 0, s.TargetCode);

    /// <summary>The portrait of an NPC, or null while it downloads / when it has none.</summary>
    public ImageSource? Get(int npcCode)
    {
        if (npcCode <= 0) return null;
        var mapped = _files.TryGetValue(npcCode, out var file);
        var name = mapped ? file! : npcCode.ToString();
        var img = Fetch(name);
        if (img is not null || mapped) return img;

        // Not under its code: by English name, once the file under the code is known to be missing.
        if (!_englishNames.TryGetValue(npcCode, out var english) || MonsterPages.Slug(english) is not { } slug) return null;
        if (_bySlug.TryGetValue(slug, out var found)) return Fetch(found);
        if (!Enabled || !Missed(name) || Missed("page:" + slug)) return null;
        if (_pending.TryAdd("page:" + slug, 0)) _ = ResolveAsync(slug);
        return null;
    }

    private bool Missed(string name) => _misses.TryGetValue(name, out var when) && DateTimeOffset.UtcNow - when < RetryMissesAfter;

    private async Task ResolveAsync(string slug)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, MonsterPages.PageUrl + slug);
            request.Headers.Range = new RangeHeaderValue(0, 49_151); // the portrait is named in the page head
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
            string? file = null;
            if (response.IsSuccessStatusCode)
            {
                await using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                var buffer = new byte[49_152];
                var read = 0;
                int n;
                while (read < buffer.Length && (n = await stream.ReadAsync(buffer.AsMemory(read)).ConfigureAwait(false)) > 0) read += n;
                file = MonsterPages.PortraitFile(Encoding.UTF8.GetString(buffer, 0, read));
            }
            if (file is null)
            {
                _misses["page:" + slug] = DateTimeOffset.UtcNow; // no page (404) or no portrait on it
                SaveMisses();
                return;
            }
            _bySlug[slug] = file;
            SaveNames();
            PortraitReady?.Invoke(); // asked again, the portrait itself downloads now
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
        {
            Log.Info($"Monster page {slug} not read: {ex.Message}");
        }
        finally
        {
            _pending.TryRemove("page:" + slug, out _);
        }
    }

    /// <summary>A cached or downloading portrait file; null while it downloads or when the site has none.</summary>
    private ImageSource? Fetch(string name)
    {
        if (_loaded.TryGetValue(name, out var img)) return img;

        var path = Path.Combine(_cacheDir, name.Replace('/', '_') + ".webp");
        if (File.Exists(path))
        {
            img = Load(path);
            _loaded[name] = img;
            return img;
        }
        if (!Enabled || Missed(name)) return null;
        if (_pending.TryAdd(name, 0)) _ = DownloadAsync(name, path);
        return null;
    }

    private async Task DownloadAsync(string name, string path)
    {
        try
        {
            using var response = await _http.GetAsync(BaseUrl + name + ".webp").ConfigureAwait(false);
            // The site answers 403 (not 404) for files it does not have.
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentType?.MediaType != "image/webp")
            {
                _misses[name] = DateTimeOffset.UtcNow;
                SaveMisses();
                return;
            }
            var bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            // A unique temp name: a second AionMeter process may be fetching the same picture.
            var tmp = $"{path}.{Guid.NewGuid():N}.tmp";
            await File.WriteAllBytesAsync(tmp, bytes).ConfigureAwait(false);
            try
            {
                File.Move(tmp, path, overwrite: true);
            }
            catch (IOException) when (File.Exists(path))
            {
                File.Delete(tmp); // the other process won
            }
            _loaded[name] = Load(path);
            PortraitReady?.Invoke();
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
        {
            Log.Info($"Portrait {name} not downloaded: {ex.Message}");
        }
        finally
        {
            _pending.TryRemove(name, out _);
        }
    }

    private void LoadMap(string path)
    {
        if (!File.Exists(path)) return;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (!doc.RootElement.TryGetProperty("portraits", out var map)) return;
            foreach (var p in map.EnumerateObject())
                if (int.TryParse(p.Name, out var code) && p.Value.GetString() is { Length: > 0 } file)
                    _files[code] = file;
        }
        catch (JsonException ex)
        {
            Log.Info($"boss_portraits.json ignored: {ex.Message}");
        }
    }

    private void LoadEnglishNames(string path)
    {
        if (!File.Exists(path)) return;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            foreach (var p in doc.RootElement.EnumerateObject())
            {
                if (!int.TryParse(p.Name, out var code) || p.Value.ValueKind != JsonValueKind.Object) continue;
                if (p.Value.TryGetProperty("isDummy", out var dummy) && dummy.ValueKind == JsonValueKind.True) continue;
                if (p.Value.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String && n.GetString() is { Length: > 0 } name)
                    _englishNames[code] = name;
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            Log.Info($"npcs/en.json ignored for portraits: {ex.Message}");
        }
    }

    private void LoadNames()
    {
        try
        {
            if (!File.Exists(_namesFile)) return;
            var saved = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(_namesFile));
            if (saved is null) return;
            foreach (var (slug, file) in saved) _bySlug[slug] = file;
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
        }
    }

    private void SaveNames()
    {
        try
        {
            lock (_missesLock)
                File.WriteAllText(_namesFile, JsonSerializer.Serialize(_bySlug.ToDictionary(m => m.Key, m => m.Value)));
        }
        catch (IOException)
        {
        }
    }

    private void LoadMisses()
    {
        try
        {
            if (!File.Exists(_missesFile)) return;
            var saved = JsonSerializer.Deserialize<Dictionary<string, DateTimeOffset>>(File.ReadAllText(_missesFile));
            if (saved is null) return;
            foreach (var (name, when) in saved) _misses[name] = when;
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
        }
    }

    private void SaveMisses()
    {
        try
        {
            lock (_missesLock)
                File.WriteAllText(_missesFile, JsonSerializer.Serialize(_misses.ToDictionary(m => m.Key, m => m.Value)));
        }
        catch (IOException)
        {
        }
    }

    /// <summary>WebP goes through the Windows imaging codec (built into Windows 11; "WebP Image Extensions" on 10).</summary>
    private static ImageSource? Load(string file)
    {
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(file);
            bmp.DecodePixelWidth = 160;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or FileFormatException or ArgumentException)
        {
            Log.Info($"Portrait {Path.GetFileName(file)} could not be decoded: {ex.Message}");
            return null;
        }
    }
}
