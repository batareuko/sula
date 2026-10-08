using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using AionMeter.Core.Combat;

namespace AionMeter.App.Services;

public enum HistoryMode
{
    BossesOnly,
    BossesAndLong,
    All,
}

public sealed record WindowBounds(double Left, double Top, double Width, double Height);

public sealed class AppSettings
{
    public static string AppDataDir { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "1HP-Overlay");

    private static string FilePath => Path.Combine(AppDataDir, "settings.json");

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    // Overlay window
    public double OverlayLeft { get; set; } = 80;
    public double OverlayTop { get; set; } = 160;
    public double OverlayWidth { get; set; } = 460;
    public double OverlayHeight { get; set; } = 330;
    public double BackgroundOpacity { get; set; } = 0.9;
    public bool Locked { get; set; }
    public bool ClickThrough { get; set; }
    public bool OverlayVisible { get; set; } = true;
    /// <summary>Bring the overlay up by itself when a boss is fought nearby or you hit anything.</summary>
    public bool AutoShow { get; set; } = true;
    /// <summary>An overlay that came up by itself hides again after this long out of combat; 0 = stays up.</summary>
    public int AutoHideSeconds { get; set; } = 60;
    /// <summary>Bring the overlay up when the meter starts and when it finds the game, so a player sees it is running.</summary>
    public bool ShowOnStart { get; set; } = true;

    // Boss respawn timers
    /// <summary>Tray notification this many minutes before a tracked boss respawns; 0 = off.</summary>
    public int BossAlertMinutes { get; set; } = 2;
    /// <summary>1 HP: notify about priority field bosses (★, data/priority_bosses.json) without a bell, this early.</summary>
    public bool PriorityBossAlerts { get; set; } = true;
    public const int PriorityAlertMinutes = 10;

    // Meter
    public TargetMode TargetMode { get; set; } = TargetMode.BossOnly;
    /// <summary>1 HP: outside instances list only you and your party (MeterOptions.PartyOnly).</summary>
    public bool PartyOnly { get; set; } = true;
    /// <summary>True: the leader's bar is full and the rest are sized against it. False: bars show share of party damage.</summary>
    public bool BarsRelativeToTop { get; set; } = true;
    public int MaxRows { get; set; } = 10;
    public const int MinRowSize = 50, MaxRowSize = 100;
    /// <summary>Overlay row size in percent of the original (Settings → Overlay, or Ctrl + wheel over the rows).</summary>
    public int RowSize { get; set; } = 100;
    public bool ShowBossPanel { get; set; } = true;
    public int IdleTimeoutSec { get; set; } = 10;
    public int BossIdleTimeoutSec { get; set; } = 30;

    /// <summary>Interface and skill / NPC name language: "en" (default) or "ru".</summary>
    public string Language { get; set; } = "uk"; // 1 HP: Ukrainian by default

    /// <summary>Fetch skill icons from the official AION 2 game-data CDN (cached locally).</summary>
    public bool DownloadIcons { get; set; } = true;

    // Updates
    /// <summary>Ask GitHub for a newer version shortly after start and every few hours.</summary>
    public bool CheckUpdates { get; set; } = true;
    /// <summary>Installed copies download a new version by themselves and install it at a quiet moment.</summary>
    public bool AutoInstallUpdates { get; set; } = true;
    /// <summary>A version the user chose to skip ("0.2.0"): not announced again, a newer one is.</summary>
    public string? SkippedUpdate { get; set; }

    // Capture / storage
    public bool SaveHistory { get; set; } = true;
    public HistoryMode HistoryMode { get; set; } = HistoryMode.BossesAndLong;
    /// <summary>For <see cref="HistoryMode.BossesAndLong"/>: non-boss fights shorter than this are not saved.</summary>
    public int HistoryMinSeconds { get; set; } = 20;
    public bool RecordPackets { get; set; }
    public string? CaptureDevice { get; set; }

    // Hotkeys (modifier+key, parsed by HotkeyManager)
    public string HotkeyToggleOverlay { get; set; } = "Ctrl+Shift+D";
    public string HotkeyReset { get; set; } = "Ctrl+Shift+R";
    public string HotkeyClickThrough { get; set; } = "Ctrl+Shift+L";
    public string HotkeyTimers { get; set; } = "Ctrl+Shift+T";

    // 1 HP: link to guide.sulaslova.com (see OneHpCloud)
    public string OneHpKey { get; set; } = "";
    public bool OneHpUpload { get; set; } = true;
    public bool OneHpGear { get; set; } = true;
    /// <summary>Started by the watcher when AION 2 starts (Services/GameAutostart.cs) and closed after the game.</summary>
    public bool WithGame { get; set; } = true;
    /// <summary>Smaller header, rows and strip (Settings → 1 HP).</summary>
    public bool Compact { get; set; } = true;
    /// <summary>Last place of the other windows (history, breakdown, settings …) by window kind.</summary>
    public Dictionary<string, WindowBounds> WindowPlaces { get; set; } = new();

    /// <summary>Bumped when the overlay layout changes enough that saved sizes no longer fit.</summary>
    public int LayoutVersion { get; set; } // 0 when missing from an older settings file; new installs get it in Load()
    private const int CurrentLayout = 5;

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Json) ?? new AppSettings();
                if (s.LayoutVersion < 3)
                {
                    // v3 = card layout (portrait header, two-line rows): the old compact size is too small for it.
                    s.OverlayWidth = Math.Max(s.OverlayWidth, 470);
                    s.OverlayHeight = Math.Max(s.OverlayHeight, 540);
                }
                if (s.LayoutVersion < 4)
                {
                    // v4 = English / Russian switch. Earlier builds picked the language from Windows on their own;
                    // the default is now English until the user chooses.
                    s.Language = "en";
                }
                if (s.LayoutVersion < 5 && s.Compact)
                {
                    // v5 = 1 HP compact layout, on by default: the card keeps its place and gets shorter by what it saves.
                    s.OverlayHeight = Math.Max(220, s.OverlayHeight - AionMeter.App.Windows.OverlayWindow.CompactSaves);
                }
                s.Language = UiText.Normalize(s.Language);
                s.LayoutVersion = CurrentLayout;
                return s;
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
        }
        return new AppSettings { LayoutVersion = CurrentLayout };
    }

    /// <summary>Throw-away settings (render tool): never written to disk.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool Transient { get; init; }

    public void Save()
    {
        if (Transient) return;
        Directory.CreateDirectory(AppDataDir);
        // Written aside, then swapped in: Windows shutting the app down mid-write must not leave an empty settings.json.
        var tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, Json));
        File.Move(tmp, FilePath, overwrite: true);
    }

    public void ApplyTo(MeterOptions options)
    {
        options.TargetMode = TargetMode;
        options.PartyOnly = PartyOnly;
        options.IdleTimeoutMs = Math.Clamp(IdleTimeoutSec, 3, 120) * 1000;
        options.BossIdleTimeoutMs = Math.Clamp(BossIdleTimeoutSec, 5, 300) * 1000;
    }
}
