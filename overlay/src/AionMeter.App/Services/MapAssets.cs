using System.IO;
using System.Net.Http;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AionMeter.Core.OneHp;

namespace AionMeter.App.Services;

/// <summary>
/// 1 HP: what the world map and the mini-map draw — the map pictures (Aion2Data, WebP) and the game's icons (the game's
/// CDN, PNG), downloaded once into %AppData%\1HP-Overlay\maps and shared by every map window. Used on the UI thread.
/// </summary>
public static class MapAssets
{
    private const string GameResources = "https://assets.playnccdn.com/static-aion2-gamedata/resources/";
    private static readonly HttpClient Http = CreateHttp();
    private static readonly string CacheDir = Path.Combine(AppSettings.AppDataDir, "maps");
    private static readonly Dictionary<string, ImageSource?> Icons = new();    // null: downloading or missing
    private static readonly Dictionary<string, ImageSource?> Pictures = new(); // null: downloading
    private static readonly Dictionary<string, string> Failed = new();         // slug → why the picture is missing

    /// <summary>A picture or an icon arrived: redraw.</summary>
    public static event Action? Changed;

    /// <summary>A game icon by its resource name; null while it downloads (start the download on first ask).</summary>
    public static ImageSource? Icon(string name)
    {
        if (Icons.TryGetValue(name, out var icon)) return icon;
        Icons[name] = null;
        _ = LoadIconAsync(name);
        return null;
    }

    /// <summary>The map's picture; null while it downloads or when it cannot be shown (<see cref="Problem"/> says why).</summary>
    public static ImageSource? Picture(WorldMaps.Map map)
    {
        if (Pictures.TryGetValue(map.Slug, out var pic)) return pic;
        Pictures[map.Slug] = null;
        _ = LoadPictureAsync(map);
        return null;
    }

    /// <summary>Why the picture of <paramref name="slug"/> is missing: "codec", "net", or null (fine or still coming).</summary>
    public static string? Problem(string slug) => Failed.GetValueOrDefault(slug);

    /// <summary>Downloading the picture of a map not cached yet.</summary>
    public static bool Downloading(string slug) => Pictures.TryGetValue(slug, out var p) && p is null && !Failed.ContainsKey(slug);

    private static async Task LoadIconAsync(string name)
    {
        var file = Path.Combine(CacheDir, "icons", name + ".png");
        try
        {
            if (!File.Exists(file))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                await SaveAsync(GameResources + name + ".png", file);
            }
            Icons[name] = Decode(file, 64);
            Raise();
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException or NotSupportedException or FileFormatException or UnauthorizedAccessException)
        {
            Log.Info($"Map icon {name} not loaded: {ex.Message}");
        }
    }

    private static async Task LoadPictureAsync(WorldMaps.Map map)
    {
        var file = Path.Combine(CacheDir, map.Slug + ".webp");
        try
        {
            if (!File.Exists(file))
            {
                Directory.CreateDirectory(CacheDir);
                await SaveAsync(map.ImageUrl, file);
            }
            Pictures[map.Slug] = Decode(file, 0);
            Failed.Remove(map.Slug);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException or NotSupportedException or FileFormatException or UnauthorizedAccessException)
        {
            Log.Info($"Map picture {map.Slug} not loaded: {ex.Message}");
            Pictures.Remove(map.Slug); // try again next time
            Failed[map.Slug] = ex is NotSupportedException or FileFormatException ? "codec" : "net";
        }
        Raise();
    }

    /// <summary>The preview renderer: a map's picture and the icons of some layers, ready before the window is drawn.</summary>
    public static void LoadNow(WorldMaps.Map map, IEnumerable<string> layers)
    {
        try
        {
            var file = Path.Combine(CacheDir, map.Slug + ".webp");
            Directory.CreateDirectory(CacheDir);
            if (!File.Exists(file)) SaveAsync(map.ImageUrl, file).GetAwaiter().GetResult();
            Pictures[map.Slug] = Decode(file, 0);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException or NotSupportedException or FileFormatException)
        {
            Log.Info($"Map picture {map.Slug} not loaded: {ex.Message}");
        }
        try
        {
            foreach (var key in layers)
                if (map.Layers.TryGetValue(key, out var places))
                    foreach (var name in places.Select(p => WorldMaps.IconOf(key, p)).Append(WorldMaps.LayerIcon(key)).Distinct())
                    {
                        var file = Path.Combine(CacheDir, "icons", name + ".png");
                        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                        if (!File.Exists(file)) SaveAsync(GameResources + name + ".png", file).GetAwaiter().GetResult();
                        Icons[name] = Decode(file, 64);
                    }
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException or NotSupportedException or FileFormatException)
        {
            Log.Info($"Map icons not loaded: {ex.Message}");
        }
    }

    private static async Task SaveAsync(string url, string file)
    {
        var bytes = await Http.GetByteArrayAsync(url).ConfigureAwait(false);
        var tmp = $"{file}.{Guid.NewGuid():N}.tmp"; // a second meter may fetch the same file
        await File.WriteAllBytesAsync(tmp, bytes).ConfigureAwait(false);
        File.Move(tmp, file, overwrite: true);
    }

    /// <summary>WebP through the Windows imaging codec (built into Windows 11; "WebP Image Extensions" on 10).</summary>
    private static BitmapImage Decode(string file, int width)
    {
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        if (width > 0) bmp.DecodePixelWidth = width;
        bmp.UriSource = new Uri(file);
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }

    private static bool _raising;

    /// <summary>One redraw for a burst of arrivals (cached files arrive while a window is still building).</summary>
    private static void Raise()
    {
        if (_raising) return;
        _raising = true;
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            _raising = false;
            Changed?.Invoke();
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    private static HttpClient CreateHttp()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("1HP-Overlay (world map, credited to Aion2Data)");
        return http;
    }
}
