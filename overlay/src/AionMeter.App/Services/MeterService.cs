using System.IO;
using AionMeter.Core.Capture;
using AionMeter.Core.Combat;
using AionMeter.Core.Demo;
using AionMeter.Core.Game;
using AionMeter.Core.Storage;

namespace AionMeter.App.Services;

/// <summary>Owns the capture → decoder → combat tracker pipeline and fight history for the whole app.</summary>
public sealed class MeterService : IDisposable
{
    private IEventSource? _capture;
    private IEventSource? _replay;
    private readonly Lazy<Core.OneHp.WorldMaps> _maps;

    /// <summary>1 HP: the world maps (data/world_maps.json), read when the map window first opens.</summary>
    public Core.OneHp.WorldMaps Maps => _maps.Value;

    private DemoFightSource? _demo;

    public MeterService(AppSettings settings, Func<GameData, IEventSource>? captureFactory)
    {
        Settings = settings;
        CaptureFactory = captureFactory;
        Data = GameData.Load(DataDirectory, UiText.Normalize(settings.Language));
        Icons = new SkillIcons(DataDirectory, settings.DownloadIcons);
        Portraits = new BossPortraits(DataDirectory, settings.DownloadIcons);
        Options = new MeterOptions();
        settings.ApplyTo(Options);
        Tracker = new CombatTracker(Data, Options);
        Pets = Core.OneHp.PetRadar.Load(Path.Combine(DataDirectory, "pet_souls.json"));
        _maps = new Lazy<Core.OneHp.WorldMaps>(() => Core.OneHp.WorldMaps.Load(Path.Combine(DataDirectory, "world_maps.json"), Path.Combine(DataDirectory, "map_calibration.json")));
        Tracker.Pets = Pets;
        Pets.SelfId = () => Tracker.SelfActorId;
        // 1 HP: monster skills worth a line on the card — not plain attacks, potions or anything of the players
        Casts = new Core.OneHp.BossCasts(code => GameData.IsNpcSkill(code) && code is < 2_000_000 or > 2_999_999 &&
                                                 !Data.SkillName(code).Equals("Attack", StringComparison.OrdinalIgnoreCase));
        Tracker.Casts = Casts;
        // 1 HP: Hidden Cube radar — "Cube detected" while a hidden cube is near; the direction arrow is a stub until
        // the cube's position packet is confirmed.
        Cubes = new Core.OneHp.CubeRadar { FadeAfterMs = Math.Clamp(settings.CubeRadarFadeMs, 0, 60_000) };
        Cubes.SelfId = () => Tracker.SelfActorId;
        Cubes.Changed += (cube, appeared) =>
        {
            if (_replaying || DemoRunning) return;
            Log.Info(appeared ? $"Hidden cube near: #{cube.CubeId}" : $"Hidden cube gone: #{cube.CubeId}");
        };
        Tracker.Cubes = settings.CubeRadar ? Cubes : null;
        Mechanics = LoadMechanics(Path.Combine(DataDirectory, "boss_mechanics.json"));
        History = new HistoryStore(Path.Combine(AppSettings.AppDataDir, "fights"));
        Timers = new BossTimers(Path.Combine(AppSettings.AppDataDir, "boss-timers.json"), Data, DataDirectory, persistent: !settings.Transient);
        Updates = new Updater(settings);
        // 1 HP: ping / packet-loss strip; reads the game stream counters of whatever capture is running
        Net = new NetMonitor(() => _capture is LiveCapture live ? live.Pipeline.Health : null,
            () => _capture is LiveCapture live ? live.Pipeline.LockedFlows.ToList() : []);
        Net.PingEnabled = settings.NetPing;
        Cloud = new OneHpCloud(settings);
        Dummies = new Core.OneHp.DummyRuns(settings.Transient ? null : Path.Combine(AppSettings.AppDataDir, "dummy-runs.json"));
        Stream = new StreamServer(this);
        if (!settings.Transient) Stream.Apply(settings.StreamEnabled, settings.StreamPort);
        Tracker.EncounterFinished += OnEncounterFinished;
        Tracker.PvpSessionEnded += SavePvp;
        Tracker.DungeonRunEnded += SaveRun;
        // A recording's or the demo's bosses must not move the live respawn timers.
        Tracker.BossNoticed += n =>
        {
            if (!_replaying && !DemoRunning) Timers.OnNotice(n);
        };
        Tracker.FieldBossListed += list =>
        {
            if (_replaying) return;
            Log.FieldBossList(list);
            Timers.OnList(list);
            // 1 HP: the game's own times for this server, to the site and Discord
            var server = Timers.CurrentServer;
            Cloud.SyncBosses(server, Tracker.SelfName, Timers.ForServer(server));
        };

        // Names and bosses learned before a restart (same zone, recent) come back immediately: no "#id" players, and a boss
        // already in view keeps its name and real max HP.
        if (!settings.Transient && _names.Load() is { } cached)
        {
            Tracker.ImportCache(cached);
            Log.Info($"Restored {cached.Players.Count} cached player names, {cached.Npcs.Count} bosses");
        }
        _savedNamesVersion = Tracker.CacheVersion;
        if (!settings.Transient) LoadBuffSkills();
        if (!settings.Transient) LoadBossCasts();

        // Boss timers are per server: follow the character's (from the cache now, then on every login).
        Timers.SetCurrentServer(Tracker.SelfServerId);
        Tracker.KnownSelfName = settings.LastSelfName;
        Tracker.SelfIdentified += name =>
        {
            if (_replaying || DemoRunning) return;
            Timers.SetCurrentServer(Tracker.SelfServerId);
            if (!string.IsNullOrWhiteSpace(name) && name != Settings.LastSelfName)
            {
                Settings.LastSelfName = Tracker.KnownSelfName = name;
                Settings.Save();
            }
        };
    }

    public static string DataDirectory { get; } = Path.Combine(AppContext.BaseDirectory, "data");

    public AppSettings Settings { get; }
    public GameData Data { get; }
    public SkillIcons Icons { get; }
    public BossPortraits Portraits { get; }
    public MeterOptions Options { get; }
    public CombatTracker Tracker { get; }
    /// <summary>1 HP: monsters in view that drop a pet's soul.</summary>
    public Core.OneHp.PetRadar Pets { get; }
    /// <summary>1 HP: monster casts in the fight and the gaps learnt between them (boss-casts.json).</summary>
    public Core.OneHp.BossCasts Casts { get; }
    /// <summary>1 HP: hidden cubes near you (Core/OneHp/CubeRadar.cs); hooked to the tracker when <see cref="AppSettings.CubeRadar"/> is on.</summary>
    public Core.OneHp.CubeRadar Cubes { get; }
    /// <summary>1 HP: tips per boss NPC code, from the site's mechanics guide (data/boss_mechanics.json, by English name).</summary>
    public IReadOnlyDictionary<int, string[]> Mechanics { get; }

    private static IReadOnlyDictionary<int, string[]> LoadMechanics(string path)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
            var byName = doc.RootElement.GetProperty("uk").EnumerateObject()
                .ToDictionary(p => p.Name, p => p.Value.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => x.Length > 0).ToArray(),
                    StringComparer.OrdinalIgnoreCase);
            // the tips are keyed by the English name: every NPC code with that name gets them, whatever the UI language
            using var npcs = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(Path.GetDirectoryName(path)!, "npcs", "en.json")));
            var byCode = new Dictionary<int, string[]>();
            foreach (var n in npcs.RootElement.EnumerateObject())
                if (int.TryParse(n.Name, out var code) && n.Value.TryGetProperty("name", out var name) &&
                    name.GetString() is { } en && byName.TryGetValue(en, out var tips))
                    byCode[code] = tips;
            return byCode;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or KeyNotFoundException)
        {
            return new Dictionary<int, string[]>();
        }
    }

    private static string BossCastsPath => Path.Combine(AppSettings.AppDataDir, "boss-casts.json");

    private void LoadBossCasts()
    {
        try
        {
            if (!File.Exists(BossCastsPath)) return;
            var saved = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, Core.OneHp.BossCasts.Timing>>(File.ReadAllText(BossCastsPath));
            if (saved is not null) Casts.Import(saved);
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or UnauthorizedAccessException)
        {
            Log.Info("boss-casts.json not read: " + ex.Message);
        }
    }

    private void SaveBossCasts()
    {
        if (!Casts.Changed || Settings.Transient) return;
        try
        {
            var tmp = BossCastsPath + ".tmp";
            File.WriteAllText(tmp, System.Text.Json.JsonSerializer.Serialize(Casts.Export()));
            File.Move(tmp, BossCastsPath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Info("boss-casts.json not saved: " + ex.Message);
        }
    }
    public HistoryStore History { get; }
    public BossTimers Timers { get; }
    /// <summary>New versions on GitHub; checks only run once the app calls <see cref="Updater.Start"/>.</summary>
    public Updater Updates { get; }
    /// <summary>1 HP: live ping to the game server and router, packet loss of the game stream.</summary>
    public NetMonitor Net { get; }
    /// <summary>1 HP: party gear lookups and the guild DPS rating (guide.sulaslova.com).</summary>
    public OneHpCloud Cloud { get; }
    /// <summary>1 HP: training dummy runs (number, against your best and your last run).</summary>
    public Core.OneHp.DummyRuns Dummies { get; }
    /// <summary>1 HP: the meter as a page for OBS (http://localhost:7799/).</summary>
    public StreamServer Stream { get; }

    /// <summary>A training dummy per the data tables.</summary>
    public bool IsDummy(int npcCode) => Data.Npcs.TryGetValue(npcCode, out var def) && def.IsDummy;
    public Func<GameData, IEventSource>? CaptureFactory { get; }

    public CaptureStatus CaptureStatus =>
        _replay is { Status.State: CaptureState.Capturing } r ? r.Status
        : _restarting ? new CaptureStatus(CaptureState.Starting, "Starting capture…")
        : _capture?.Status ?? new CaptureStatus(CaptureState.Stopped, "Capture not available");

    public bool DemoRunning => _demo?.IsRunning == true;

    /// <summary>Raised (on a worker thread) after a fight was written to disk.</summary>
    public event Action<string>? FightSaved;

    /// <summary>
    /// Plays a recorded capture through the meter as fast as it parses. Live events are detached meanwhile (their
    /// timestamps would interleave with the recording's) and replayed fights are not saved to history again.
    /// </summary>
    public void Replay(string path)
    {
        _replay?.Dispose();
        Tracker.Reset();
        _replaying = true;
        if (_capture is not null) _capture.EventDecoded -= Tracker.Process;

        var replay = new PcapReplaySource(Data, path, realtime: false);
        replay.EventDecoded += Tracker.Process;
        replay.StatusChanged += s =>
        {
            if (s.State != CaptureState.Stopped) return;
            Tracker.Tick(Tracker.LastEventMs + 3_600_000); // close the recording's last fight
            _savedNamesVersion = Tracker.CacheVersion; // names from a recording must not overwrite the live cache
            _replaying = false;
            if (_capture is not null) _capture.EventDecoded += Tracker.Process;
        };
        _replay = replay;
        replay.Start();
    }

    private volatile bool _replaying;

    private int _partyLogged;

    /// <summary>1 HP: the party packets in the log (raw and read) — what the PARTY filter is built from.</summary>
    private void LogParty(Core.Events.GameEvent e)
    {
        if (_partyLogged > 400) return; // enough to see what the game sends
        switch (e)
        {
            case Core.Events.PartyPacketEvent p:
                _partyLogged++;
                Log.Info($"Party packet {p.Opcode:X4} {(p.Read ? "read" : "NOT READ")}: {p.Hex}");
                break;
            case Core.Events.PartyListEvent l:
                _partyLogged++;
                Log.Info("Party list: " + string.Join(", ", l.Members.Select(m => $"{m.Name}#{m.ActorId}")));
                break;
            case Core.Events.PartyJoinEvent j:
                _partyLogged++;
                Log.Info($"Party join: {j.Member.Name}#{j.Member.ActorId}");
                break;
            case Core.Events.PartyLeftEvent:
                _partyLogged++;
                Log.Info("Party left");
                break;
            case Core.Events.PartyRosterEvent r:
                _partyLogged++;
                Log.Info($"Party roster ({(r.Complete ? "complete" : "partial")}): " + string.Join(", ", r.Names));
                break;
            case Core.Events.PartyMemberEvent m:
                _partyLogged++;
                Log.Info("Party loot share: " + m.Name);
                break;
        }
    }

    /// <summary>1 HP: "record for analysis" — packets and decoded events into one zip to send for decoding.</summary>
    public AnalysisRecorder Analysis { get; } = new();

    public void StartAnalysis()
    {
        lock (_captureGate) Analysis.Start(_capture as LiveCapture);
    }

    /// <summary>Stops the analysis recording and writes its zip (null when none was running).</summary>
    public string? StopAnalysis()
    {
        var t = Tracker;
        var party = t.PartyStatus();
        var summary = new
        {
            self = new { name = t.SelfName, server = t.SelfServerId },
            capture = CaptureStatus.Message,
            party = new { state = party.State.ToString(), source = party.Source.ToString(), members = party.Party },
            bossKills = BossKills,
            language = Settings.Language,
        };
        lock (_captureGate) return Analysis.Stop(_capture as LiveCapture, summary);
    }

    private readonly object _captureGate = new();
    private volatile bool _restarting;

    public void StartCapture()
    {
        lock (_captureGate)
        {
            if (CaptureFactory is null || _capture is not null) return;
            if (Settings.NoCapture)
            {
                Log.Info("Disconnect test: packet capture is off");
                _capture = new FailedSource(UiText.Current.DiagCaptureOff);
                return;
            }
            try
            {
                _capture = CaptureFactory(Data);
                _capture.EventDecoded += Tracker.Process;
                _capture.EventDecoded += LogParty;
                _capture.EventDecoded += Analysis.Write;
                Analysis.Attach(_capture as LiveCapture);
                _capture.Start();
                if (Settings.NoGameScan) Log.Info("Disconnect test: no game search and no pings");
                else Net.Start();
            }
            catch (Exception ex)
            {
                Log.Error("Capture start failed", ex);
                _capture?.Dispose();
                _capture = new FailedSource(ex.Message);
            }
        }
    }

    public void RestartCapture()
    {
        lock (_captureGate)
        {
            _restarting = true;
            try
            {
                _capture?.Dispose();
                _capture = null;
                StartCapture();
            }
            finally
            {
                _restarting = false;
            }
        }
    }

    /// <summary>
    /// The "restart meter" button: the fight so far is closed and saved, capture starts over (adapter lookup, stream
    /// sync) and the live view stays empty until the next hit. Names and bosses the meter already knows stay known —
    /// unlike closing and starting the app in the middle of a fight.
    /// </summary>
    public void Restart()
    {
        _demo?.Dispose();
        Tracker.Reset();
        if (_replaying) return;
        _restarting = true;
        Task.Run(RestartCapture); // closing the adapter can take a moment: not on the UI thread
    }

    public void StartDemo(int seconds = 45)
    {
        _demo?.Dispose();
        _demo = new DemoFightSource(Tracker.Process);
        _demo.Start(TimeSpan.FromSeconds(seconds));
    }

    public void Tick()
    {
        // The wall clock means nothing to a recording's timestamps: idle checks wait until the replay is done.
        if (!_replaying) Tracker.Tick(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

        // Persist the name map a few seconds after it changes (not on every packet).
        if (Settings.Transient || _replaying || DemoRunning) return;
        var now = Environment.TickCount64;
        if (now - _lastNamesCheck < 5_000) return;
        _lastNamesCheck = now;
        SaveNames();
    }

    private readonly NameCache _names = new(Path.Combine(AppSettings.AppDataDir, "names-cache.json"));
    private int _savedNamesVersion;
    private long _lastNamesCheck;

    // 1 HP: buff → the skill that puts it on, learnt in play (the card shows that skill's icon for the buff)
    private static string BuffSkillsPath => Path.Combine(AppSettings.AppDataDir, "buff-skills.json");
    private int _savedBuffSkills;

    private void LoadBuffSkills()
    {
        try
        {
            if (!File.Exists(BuffSkillsPath)) return;
            var known = System.Text.Json.JsonSerializer.Deserialize<Dictionary<int, int>>(File.ReadAllText(BuffSkillsPath));
            if (known is not null) Tracker.ImportBuffSkills(known);
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or UnauthorizedAccessException)
        {
            Log.Info("buff-skills.json not read: " + ex.Message);
        }
    }

    private void SaveBuffSkills()
    {
        var version = Tracker.BuffSkillsVersion;
        if (version == _savedBuffSkills) return;
        _savedBuffSkills = version;
        try
        {
            var tmp = BuffSkillsPath + ".tmp";
            File.WriteAllText(tmp, System.Text.Json.JsonSerializer.Serialize(Tracker.ExportBuffSkills()));
            File.Move(tmp, BuffSkillsPath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Info("buff-skills.json not saved: " + ex.Message);
        }
    }

    private void SaveNames()
    {
        SaveBuffSkills();
        SaveBossCasts();
        var version = Tracker.CacheVersion;
        if (version == _savedNamesVersion) return;
        _savedNamesVersion = version;
        _names.Save(Tracker.ExportCache());
    }

    /// <summary>English / Russian for the interface and for skill / NPC names, applied without a restart.</summary>
    public void SetLanguage(string language)
    {
        language = UiText.Normalize(language);
        Settings.Language = language;
        Data.ReloadNames(DataDirectory, language);
        _dungeonNames = null;
        UiText.Use(language);
        Settings.Save();
    }

    public void ApplySettings()
    {
        Settings.ApplyTo(Options);
        Cubes.FadeAfterMs = Math.Clamp(Settings.CubeRadarFadeMs, 0, 60_000);
        Tracker.Cubes = Settings.CubeRadar ? Cubes : null;
        Icons.Enabled = Settings.DownloadIcons;
        Portraits.Enabled = Settings.DownloadIcons;
        if (!Settings.Transient) Stream.Apply(Settings.StreamEnabled, Settings.StreamPort);
        Settings.Save();
    }

    /// <summary>Boss fights are always kept; trash only when long enough (or everything, per settings).</summary>
    private bool ShouldSave(EncounterSnapshot s)
    {
        if (_replaying || DemoRunning || !Settings.SaveHistory || s.CombatMs < Options.MinSavedFightMs) return false;
        var boss = s.Boss is not null;
        return Settings.HistoryMode switch
        {
            HistoryMode.BossesOnly => boss,
            HistoryMode.BossesAndLong => boss || s.CombatMs >= Settings.HistoryMinSeconds * 1000L,
            _ => true,
        };
    }

    /// <summary>Fight title in the current language (fights saved earlier may have used another one, or none).</summary>
    public string DisplayTitle(string savedTitle, int bossCode, int targetCode)
    {
        var code = bossCode != 0 ? bossCode : targetCode;
        if (code == 0 || !Data.Npcs.ContainsKey(code)) return savedTitle;
        var name = Data.NpcName(code);
        var plus = bossCode == 0 ? savedTitle.LastIndexOf(" +", StringComparison.Ordinal) : -1;
        return plus > 0 ? name + savedTitle[plus..] : name;
    }

    public string DisplayTitle(HistoryEntry e) => DisplayTitle(e.Title, e.BossCode, e.TargetCode);

    /// <summary>Portrait of the fight's boss, or of its main target when there was none (null until downloaded).</summary>
    public System.Windows.Media.ImageSource? PortraitOf(EncounterSnapshot s) => Portraits.Of(s);

    public System.Windows.Media.ImageSource? PortraitOf(HistoryEntry e) => Portraits.Of(e.BossCode, e.TargetCode);

    /// <summary>Fights saved before map names were known carry "Map 1110": resolve those now.</summary>
    public string? DisplayZone(string? zone) =>
        zone is not null && zone.StartsWith("Map ", StringComparison.Ordinal) && int.TryParse(zone.AsSpan(4), out var id)
            ? Data.MapName(id)
            : zone;

    private bool _exiting;

    /// <summary>1 HP: dungeon runs — clear time, bosses, deaths, members — in %AppData%\1HP-Overlay\dungeon-runs.json (last 200).</summary>
    private void SaveRun(Core.Combat.DungeonRun run)
    {
        if (_replaying || DemoRunning || Settings.Transient || run.Fights.Count == 0) return;
        AppendJson("dungeon-runs.json", run);
        Log.Info($"Dungeon run saved: {run.Zone}, {run.Fights.Count} fights, {run.BossesKilled}/{run.BossFights} bosses, {run.Deaths} deaths");
    }

    private static void AppendJson<T>(string file, T item)
    {
        try
        {
            var path = Path.Combine(AppSettings.AppDataDir, file);
            var list = File.Exists(path) ? System.Text.Json.JsonSerializer.Deserialize<List<T>>(File.ReadAllText(path)) ?? new() : new List<T>();
            list.Insert(0, item);
            if (list.Count > 200) list.RemoveRange(200, list.Count - 200);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, System.Text.Json.JsonSerializer.Serialize(list));
            File.Move(tmp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or UnauthorizedAccessException)
        {
            Log.Info(file + " not saved: " + ex.Message);
        }
    }

    /// <summary>1 HP: PvP sessions — opponents, kills, deaths, burst — kept in %AppData%\1HP-Overlay\pvp-sessions.json (last 200).</summary>
    private void SavePvp(Core.Combat.PvpSession session)
    {
        if (_replaying || DemoRunning || Settings.Transient) return;
        try
        {
            var path = Path.Combine(AppSettings.AppDataDir, "pvp-sessions.json");
            var list = File.Exists(path)
                ? System.Text.Json.JsonSerializer.Deserialize<List<Core.Combat.PvpSession>>(File.ReadAllText(path)) ?? new()
                : new List<Core.Combat.PvpSession>();
            list.Insert(0, session);
            if (list.Count > 200) list.RemoveRange(200, list.Count - 200);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, System.Text.Json.JsonSerializer.Serialize(list));
            File.Move(tmp, path, overwrite: true);
            Log.Info($"PvP session saved: {session.Opponents.Count} opponents, {session.Kills} kills, {session.Deaths} deaths");
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or UnauthorizedAccessException)
        {
            Log.Info("PvP session not saved: " + ex.Message);
        }
    }

    /// <summary>1 HP: saved dungeon runs, newest first (the history window's «Dungeons»).</summary>
    public IReadOnlyList<Core.Combat.DungeonRun> DungeonRuns() => ReadJson<Core.Combat.DungeonRun>("dungeon-runs.json");

    /// <summary>1 HP: saved PvP sessions, newest first (the history window's «PvP»).</summary>
    public IReadOnlyList<Core.Combat.PvpSession> PvpSessions() => ReadJson<Core.Combat.PvpSession>("pvp-sessions.json");

    private static List<T> ReadJson<T>(string file)
    {
        try
        {
            var path = Path.Combine(AppSettings.AppDataDir, file);
            return File.Exists(path) ? System.Text.Json.JsonSerializer.Deserialize<List<T>>(File.ReadAllText(path)) ?? new() : new();
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or UnauthorizedAccessException)
        {
            Log.Info(file + " not read: " + ex.Message);
            return new();
        }
    }

    /// <summary>1 HP: a saved fight was in a dungeon — by its flag, or (fights saved before 1.10.2) by its zone's name.</summary>
    public bool InDungeon(HistoryEntry e)
    {
        if (e.InDungeon) return true;
        if (string.IsNullOrEmpty(e.Zone)) return false;
        if (e.Zone.StartsWith("Dungeon ", StringComparison.Ordinal)) return true;
        _dungeonNames ??= Data.Maps.Where(kv => GameData.IsDungeonMap(kv.Key)).Select(kv => kv.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return _dungeonNames.Contains(e.Zone) || (DisplayZone(e.Zone) is { } z && _dungeonNames.Contains(z));
    }

    private HashSet<string>? _dungeonNames;

    /// <summary>1 HP: bosses killed since the meter started (or since the counter was reset) — the "bus" count.</summary>
    public int BossKills { get; private set; }

    /// <summary>Raised (on the capture thread) when a boss falls: the session's kill count.</summary>
    public event Action<int, string>? BossKilled;

    public void ResetBossKills() => BossKills = 0;

    private void OnEncounterFinished(FightRecord record)
    {
        if (!_replaying && !DemoRunning && record.Summary is { Reason: EncounterEndReason.Kill, Boss: { } boss })
        {
            BossKills++;
            BossKilled?.Invoke(BossKills, boss.Name);
        }
        // 1 HP: your own result of a boss kill goes to the guild rating (only with a key from the site)
        if (!_replaying && !DemoRunning) _ = Cloud.UploadAsync(record);
        if (!_replaying && !DemoRunning) Dummies.Finish(record.Summary, IsDummy);
        if (!ShouldSave(record.Summary)) return;
        if (_exiting) Save(); // the process is about to end: a worker thread would not get to it
        else ThreadPool.QueueUserWorkItem(_ => Save());

        void Save()
        {
            try
            {
                var path = History.Save(record);
                FightSaved?.Invoke(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Error("Saving fight failed", ex);
            }
        }
    }

    public void Dispose()
    {
        var live = !Settings.Transient && !_replaying && !DemoRunning;
        Stream.Dispose();
        lock (_captureGate) _capture?.Dispose(); // no more packets: the running fight is as complete as it gets
        _replay?.Dispose();
        _demo?.Dispose();
        if (live)
        {
            // Closing the meter mid-fight keeps the fight so far, like the restart button does.
            _exiting = true;
            Tracker.Reset();
            SaveNames();
        }
        Updates.Dispose();
        Net.Dispose();
        Cloud.Dispose();
    }

    /// <summary>Placeholder source that only reports why capture could not start.</summary>
    private sealed class FailedSource(string message) : IEventSource
    {
        public event Action<Core.Events.GameEvent>? EventDecoded { add { } remove { } }
        public event Action<CaptureStatus>? StatusChanged { add { } remove { } }
        public CaptureStatus Status { get; } = new(CaptureState.Error, message);
        public void Start() { }
        public void Stop() { }
        public void Dispose() { }
    }
}
