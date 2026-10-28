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
    /// <summary>Send the in-game field boss list's times for your server to the site (OneHpCloud.SyncBosses).</summary>
    public bool OneHpBossSync { get; set; } = true;
    /// <summary>Started by the watcher when AION 2 starts (Services/GameAutostart.cs) and closed after the game.</summary>
    public bool WithGame { get; set; } = true;
    /// <summary>Smaller header, rows and strip (Settings → 1 HP).</summary>
    public bool Compact { get; set; } = true;
    /// <summary>One line (name, combat power, ping) out of combat; a row per fighter in a fight (OverlayWindow.ApplyDynamic).</summary>
    public bool DynamicOverlay { get; set; } = true;
    /// <summary>1 HP: events to announce before they start (Core.OneHp.EventSchedule ids) and how early.</summary>
    public List<string> EventAlerts { get; set; } = ["rift", "kaira", "siege", "siege-bosses", "nahma"];
    public int EventAlertMinutes { get; set; } = 5;
    /// <summary>1 HP: the PvP card (your opponents) while you trade hits with players.</summary>
    public bool PvpOverlay { get; set; } = true;
    /// <summary>
    /// 1 HP: PvP colours — red (run) when an opponent is this far above you, orange from the second gap, green when
    /// this far below; by gear score when both are inspected, else by combat power. A red one hitting you gets a notice.
    /// </summary>
    public int PvpDangerGear { get; set; } = 1000;
    public int PvpCautionGear { get; set; } = 750;
    public int PvpWeakGear { get; set; } = 1000;
    public int PvpDangerPower { get; set; } = 10_000;
    public int PvpCautionPower { get; set; } = 7_500;
    public int PvpWeakPower { get; set; } = 10_000;
    public bool PvpThreatAlert { get; set; } = true;
    /// <summary>1 HP: the boss's HP as a wide bar at the top of the screen in boss fights (Windows/BossBarWindow.cs).</summary>
    public bool BossBar { get; set; }
    /// <summary>1 HP: a sound and a notice when a party member dies.</summary>
    public bool PartyDeathAlert { get; set; } = true;
    /// <summary>1 HP: alerts spoken by the Windows voice as well (events, bosses, deaths).</summary>
    public bool VoiceAlerts { get; set; }
    /// <summary>1 HP: your lasting buffs on the card, and a notice 10 s before one of a minute or more runs out.</summary>
    public bool ShowBuffs { get; set; } = true;
    /// <summary>1 HP: no card around the meter — a dark header and rows straight over the game (like the stream meters).</summary>
    public bool Frameless { get; set; } = true;
    /// <summary>1 HP: names shimmer in the class colours (yours gold-pink).</summary>
    public bool AnimatedNames { get; set; } = true;
    /// <summary>1 HP: boss mechanics on the card in boss fights — the last cast, what comes next, the guide's tips.</summary>
    public bool BossMechanics { get; set; } = true;
    /// <summary>
    /// 1 HP disconnect test, the strictest: the meter only sits in the tray — no window, no capture, no game search,
    /// no hotkeys, no internet (updates, guild timers), no start-with-game. Implies the three below.
    /// </summary>
    public bool DiagSilent { get; set; }
    [System.Text.Json.Serialization.JsonIgnore] public bool NoOverlay => DiagSilent || DiagNoOverlay;
    [System.Text.Json.Serialization.JsonIgnore] public bool NoCapture => DiagSilent || DiagNoCapture;
    [System.Text.Json.Serialization.JsonIgnore] public bool NoGameScan => DiagSilent || DiagNoGameScan;
    /// <summary>1 HP disconnect test: no window over the game (the overlay and the boss bar stay hidden; tray, history work).</summary>
    public bool DiagNoOverlay { get; set; }
    /// <summary>1 HP disconnect test: no packet capture at all (Npcap is not opened).</summary>
    public bool DiagNoCapture { get; set; }
    /// <summary>1 HP disconnect test: no looking for the game (process list, TCP table) and no pings.</summary>
    public bool DiagNoGameScan { get; set; }
    /// <summary>1 HP: ping the game server and the router once a second for the network strip (off: stream counters only).</summary>
    public bool NetPing { get; set; } = true;
    /// <summary>1 HP: rows in two lines — name, deaths and trend over damage, crit, gear and server; tiles above the rows.</summary>
    public bool TwoLineRows { get; set; } = true;
    /// <summary>1 HP: what the rows rank by: "dps", "hps" (healing) or "taken" (damage taken).</summary>
    public string MeterMetric { get; set; } = "dps";
    /// <summary>1 HP: the card's colours: slate, crimson, emerald, violet, amber.</summary>
    public string ColorTheme { get; set; } = "slate";
    /// <summary>1 HP: pet radar — monsters in view that drop a pet's soul (Core/OneHp/PetRadar.cs).</summary>
    public bool PetRadar { get; set; }
    /// <summary>1 HP: pets you no longer need (pet ids from data/pet_souls.json), hidden from the pet radar.</summary>
    public HashSet<int> PetsDone { get; set; } = [];
    /// <summary>1 HP: leave the pets you have out of the radar (off: they show dimmed with a tick).</summary>
    public bool PetHideOwned { get; set; }
    /// <summary>1 HP: the pet radar window (Windows/PetRadarWindow.cs) is open; its range in metres and quarter turns.</summary>
    public bool PetRadarWindow { get; set; }
    public double PetRadarRange { get; set; } = 100;
    public int PetRadarRotation { get; set; }
    public string HotkeyPetRadar { get; set; } = "Ctrl+Shift+P";
    public bool BuffEndAlert { get; set; } = true;
    /// <summary>1 HP: your party's cooldowns of a minute and more on the card.</summary>
    public bool ShowPartyCooldowns { get; set; } = true;
    /// <summary>1 HP: a notice when someone asks to join your party.</summary>
    public bool PartyRequestAlert { get; set; } = true;
    /// <summary>1 HP: your tickets and energies on the card out of combat.</summary>
    public bool ShowResources { get; set; } = true;
    /// <summary>1 HP: tickets and energies last seen, per character (name → id → count, bonus).</summary>
    public Dictionary<string, Dictionary<int, int[]>> Resources { get; set; } = new();
    /// <summary>The meter as a web page for OBS (Services/StreamServer.cs); only this computer can open it.</summary>
    public bool StreamEnabled { get; set; }
    public int StreamPort { get; set; } = StreamServer.DefaultPort;
    /// <summary>Your characters' last known combat power by name: the one-line card shows it before the game sends it again.</summary>
    public Dictionary<string, long> Powers { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Your character's name last seen: a meter started mid-session recognises you by it (CombatTracker.KnownSelfName).</summary>
    public string? LastSelfName { get; set; }
    /// <summary>Last place of the other windows (history, breakdown, settings …) by window kind.</summary>
    public Dictionary<string, WindowBounds> WindowPlaces { get; set; } = new();

    /// <summary>Bumped when the overlay layout changes enough that saved sizes no longer fit.</summary>
    public int LayoutVersion { get; set; } // 0 when missing from an older settings file; new installs get it in Load()
    private const int CurrentLayout = 7;

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
                else if (s.LayoutVersion == 5 && s.Compact)
                {
                    // v6 = slimmer compact rows (~26 px): a card already made compact in v5 gets shorter by what ten rows save.
                    s.OverlayHeight = Math.Max(220, s.OverlayHeight - 150);
                }
                if (s.LayoutVersion < 7)
                {
                    // v7 = the disconnects are over (game update 28.10): switches left on from the disconnect test kept
                    // the meter in the tray with no window, so they are cleared once and the overlay shown again.
                    s.DiagSilent = s.DiagNoOverlay = s.DiagNoCapture = s.DiagNoGameScan = false;
                    s.OverlayVisible = true;
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
