using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using AionMeter.App.Services;
using AionMeter.App.ViewModels;
using AionMeter.App.Windows;
using AionMeter.Core.Capture;

namespace AionMeter.App;

public static class AppHost
{
    public static App Current => (App)Application.Current;

    /// <summary>Clipboard writes fail while another process holds the clipboard; retry briefly.</summary>
    public static void CopyText(string text)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                Clipboard.SetText(text);
                return;
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                Thread.Sleep(30);
            }
        }
    }
}

public partial class App : Application
{
    private const string ShowRequestName = "AionMeter.ShowOverlay"; // set by a second start of the app
    private Mutex? _singleInstance;
    private EventWaitHandle? _showRequest;
    private RegisteredWaitHandle? _showWait;
    private MeterService _meter = null!;
    private OverlayWindow _overlay = null!;
    private BossBarWindow? _bossBar;
    private TrayIcon? _tray;
    private HotkeyManager? _hotkeys;
    private DispatcherTimer? _timer;
    private HistoryWindow? _history;
    private SettingsWindow? _settingsWindow;
    private BossTimersWindow? _timersWindow;
    private readonly Dictionary<object, BreakdownWindow> _breakdowns = new();
    private int _ticks;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (ArgValue(e.Args, "--render-sample") is { } samplePath)
        {
            // --render-sample <out.png | folder with --animate> [--lang ru] [--window …] [--width N] [--height N] [--tab …]
            // [--backdrop game|dark|none] [--scale 1.5] [--row-size 50..100] [--animate <seconds> [--fps 10]]
            var options = new SampleRenderer.Options(
                ArgValue(e.Args, "--window") ?? "overlay",
                int.TryParse(ArgValue(e.Args, "--width"), out var width) ? width : null,
                int.TryParse(ArgValue(e.Args, "--height"), out var height) ? height : null,
                ArgValue(e.Args, "--tab"),
                ArgValue(e.Args, "--backdrop") ?? "game",
                double.TryParse(ArgValue(e.Args, "--scale"), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var scale) ? scale : 1.5,
                int.TryParse(ArgValue(e.Args, "--row-size"), out var rowSize) ? rowSize : 100);
            var language = ArgValue(e.Args, "--lang") ?? "en";
            if (double.TryParse(ArgValue(e.Args, "--animate"), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var seconds))
                SampleRenderer.Animate(samplePath, language, options, seconds, int.TryParse(ArgValue(e.Args, "--fps"), out var fps) ? fps : 10);
            else
                SampleRenderer.Render(samplePath, language, options);
            Shutdown();
            return;
        }
        _singleInstance = new Mutex(true, "AionMeter.SingleInstance", out var first);
        if (!first)
        {
            // Started again (the shortcut clicked twice): the running meter shows its overlay, this copy leaves quietly.
            if (EventWaitHandle.TryOpenExisting(ShowRequestName, out var running))
                using (running) running.Set();
            else // a version from before 0.2.2 is running
                MessageBox.Show($"{AppInfo.Name} is already running — look for the gold \"A\" in the notification area.", AppInfo.Name);
            Shutdown();
            return;
        }

        DispatcherUnhandledException += (_, ex) =>
        {
            Log.Error("UI exception", ex.Exception);
            ex.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, ex) => Log.Error("Fatal exception", ex.ExceptionObject as Exception);

        var settings = AppSettings.Load();
        settings.Save(); // persist any migration done while loading
        WindowMemory.Use(settings);
        UiText.Use(settings.Language);
        var withGame = settings.WithGame;
        if (!settings.DiagSilent) ThreadPool.QueueUserWorkItem(_ => GameAutostart.Apply(withGame));
        else Log.Info("Disconnect test: silent — tray only (no window, capture, game search, hotkeys, internet, start-with-game)"); // may wait a moment for an old watcher to stop
        Func<Core.Game.GameData, IEventSource> factory = data => new LiveCapture(data, new LiveCaptureOptions
        {
            DeviceName = settings.CaptureDevice,
            RecordDirectory = settings.RecordPackets ? Path.Combine(AppSettings.AppDataDir, "captures") : null,
        });

        _meter = new MeterService(settings, factory);
        Log.Info($"Started. Data: {_meter.Data.Skills.Count} skills, {_meter.Data.Npcs.Count} npcs.");
        _meter.StartCapture();

        _overlay = new OverlayWindow(_meter);
        if (settings.OverlayVisible || e.Args.Contains("--show")) _overlay.Show(); // --show: this run only, not saved
        else
        {
            new WindowInteropHelper(_overlay).EnsureHandle();
            // Hidden by choice: still up for a moment, so it is plain that the meter started.
            if (settings.ShowOnStart) ShowByItself();
        }

        // 1 HP: a party member died; a boss fell (the bus count)
        _meter.Tracker.PartyMemberDied += name => Dispatcher.BeginInvoke(() => OnPartyDeath(name));
        _meter.BossKilled += (count, boss) => Dispatcher.BeginInvoke(() => OnBossKilled(count, boss));
        _meter.Tracker.PartyRequested += r => Dispatcher.BeginInvoke(() => OnPartyRequest(r));

        _hotkeys = new HotkeyManager(new WindowInteropHelper(_overlay).Handle);
        RegisterHotkeys();

        _tray = new TrayIcon(new TrayActions(
            ToggleOverlay: () => SetOverlayVisible(!_overlay.IsVisible),
            ToggleClickThrough: ToggleClickThrough,
            Reset: RestartMeter,
            History: ShowHistory,
            BossTimers: ShowBossTimers,
            Settings: ShowSettings,
            Demo: () => _meter.StartDemo(),
            Replay: PickReplay,
            ToggleLanguage: ToggleLanguage,
            Update: () => ShowUpdate(),
            CheckUpdates: () => _ = CheckUpdatesFromTrayAsync(),
            Analysis: ToggleAnalysis,
            SendLog: () => _ = SendLogsAsync(),
            Exit: () =>
            {
                Log.Info("Exit chosen in the tray menu");
                Shutdown();
            }));
        SessionEnding += (_, end) => Log.Info($"Windows {end.ReasonSessionEnding}: closing");

        // A new player's first start: without Npcap nothing can be measured — say so and offer the download.
        if (!LiveCapture.IsNpcapInstalled())
            Dispatcher.BeginInvoke(ShowNpcapHelp, DispatcherPriority.ApplicationIdle);

        // Another start of the app asks this one to show the overlay.
        _showRequest = new EventWaitHandle(false, EventResetMode.AutoReset, ShowRequestName);
        _showWait = ThreadPool.RegisterWaitForSingleObject(_showRequest,
            (_, _) => Dispatcher.BeginInvoke(() => SetOverlayVisible(true)), null, Timeout.Infinite, executeOnlyOnce: false);

        // New versions on GitHub: tray item, the overlay's update button and one notification per version.
        _meter.Updates.Changed += OnUpdatesChanged;
        if (!_meter.Settings.DiagSilent) _meter.Updates.Start();
        Updater.CleanDownloads();
        Updater.CleanOldExe();
        if (e.Args.Contains("--updated")) // started by the installer after an in-app update
            Dispatcher.BeginInvoke(() => _tray?.ShowBalloon(AppInfo.Name,
                string.Format(UiText.Current.UpdatedBalloon, _meter.Updates.Current.ToString(3))), DispatcherPriority.ApplicationIdle);

        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(200) };
        _timer.Tick += (_, _) => OnTick();
        _timer.Start();

        // Developer switches: --demo, --replay <file.pcap> (instant), --open-last (breakdown of the newest fight).
        if (e.Args.Contains("--demo")) _meter.StartDemo();
        if (ArgValue(e.Args, "--replay") is { } replay) _meter.Replay(replay);
        if (e.Args.Contains("--open-last"))
        {
            var once = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
            once.Tick += (_, _) =>
            {
                once.Stop();
                var seg = _meter.Tracker.Segments().OrderByDescending(s => s.TotalDamage).FirstOrDefault();
                if (seg is null || _meter.Tracker.Snapshot(seg.Id, 0) is not { } snap || snap.Combatants.Count == 0) return;
                var who = snap.Combatants.FirstOrDefault(c => c.IsSelf) ?? snap.Combatants[0];
                ShowBreakdown(seg.Id, who.ActorId);
                if (ArgValue(e.Args, "--tab") is { } tab && _breakdowns.TryGetValue(seg.Id, out var w)) w.ShowTab(tab);
            };
            once.Start();
        }
    }

    private static void ShowNpcapHelp()
    {
        var answer = MessageBox.Show(UiText.Current.NpcapNeeded, AppInfo.Name, MessageBoxButton.YesNo, MessageBoxImage.Information);
        if (answer != MessageBoxResult.Yes) return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://npcap.com/#download") { UseShellExecute = true });
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            Log.Error("Could not open the Npcap page", ex);
        }
    }

    private static string? ArgValue(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    private void OnTick()
    {
        if (_meter.Settings.DiagSilent) return; // disconnect test: nothing at all
        _meter.Tick();
        UpdateAutoVisibility();
        if (_overlay.IsVisible) _overlay.Refresh();
        UpdateBossBar();
        if (++_ticks % 5 == 0)
        {
            _tray?.Update(_overlay.IsVisible, _meter.Settings.ClickThrough, _meter.DemoRunning ? "demo" : _meter.CaptureStatus.Message);
            CheckBossAlerts();
            CheckBossListReminder();
            CheckEventAlerts();
            CheckBuffAlerts();
            CheckGameStart();
            CheckPendingUpdate();
            if (_meter.Analysis.Running && !_analysisBusy)
            {
                UpdateAnalysisView();
                if (_meter.Analysis.ShouldAutoStop()) _ = FinishAnalysisAsync();
            }
        }
        if (_ticks % 25 == 0) CheckGameExit();
        if (_ticks % 900 == 30) _ = PullSharedBossesAsync(); // every 3 min (5 ticks a second)
    }

    // ------------------------------------------------------------ automatic show / hide

    private bool _autoShown;           // the overlay is up because a fight brought it up, not the user
    private Guid? _suppressedFight;    // hidden by hand during this fight: do not pop it back until the next one
    private long _lastEngagedTick;

    /// <summary>
    /// A boss being fought nearby, or any fight you take part in, brings the overlay up. One that came up by itself
    /// goes away again after <see cref="AppSettings.AutoHideSeconds"/> out of combat (never while the mouse is on it).
    /// </summary>
    private void UpdateAutoVisibility()
    {
        var s = _meter.Settings;
        var fight = _meter.Tracker.LiveFight();
        // Only your fights reach here (you or your party): a healer who does not hit sees the party's fight too.
        var engaged = fight is not null;
        var now = Environment.TickCount64;
        if (engaged) _lastEngagedTick = now;
        if (!s.AutoShow) return;

        if (engaged && !_overlay.IsVisible && fight!.Value.Id != _suppressedFight)
        {
            _overlay.Show();
            _autoShown = true;
        }
        // The dynamic card folds to one line by itself instead of going away.
        else if (!engaged && _autoShown && _overlay.IsVisible && s.AutoHideSeconds > 0 && !s.DynamicOverlay &&
                 now - _lastEngagedTick > s.AutoHideSeconds * 1000L && !_overlay.IsMouseOver)
        {
            _overlay.Hide();
            _autoShown = false;
        }
    }

    /// <summary>Up without the user asking: goes away again after the usual idle time (Settings → Overlay).</summary>
    private void ShowByItself()
    {
        if (_overlay.IsVisible) return;
        _overlay.Show();
        _autoShown = true;
        _lastEngagedTick = Environment.TickCount64;
    }

    private CaptureState _lastCaptureState = CaptureState.Stopped;
    private int[] _gameShownFor = [];

    /// <summary>
    /// The overlay comes up when the meter starts seeing the game's traffic, once per launch of the game: a reconnect
    /// after a loading screen is the same game process and does not count.
    /// </summary>
    private void CheckGameStart()
    {
        var state = _meter.CaptureStatus.State;
        var connected = state == CaptureState.Capturing && _lastCaptureState != CaptureState.Capturing;
        _lastCaptureState = state;
        if (!connected || !_meter.Settings.ShowOnStart || _meter.DemoRunning) return;
        var game = GameProcessLocator.FindGameProcessIds().Order().ToArray();
        if (game.Length == 0 || game.SequenceEqual(_gameShownFor)) return;
        _gameShownFor = game;
        ShowByItself();
    }

    private bool _gameSeen;
    private long _gameGoneSince;

    /// <summary>
    /// 1 HP: with "start with AION 2" on, the meter leaves once the game has been closed for 15 s (a client restart or
    /// the launcher swapping processes takes less). Only after the game was seen in this run, so a meter started by hand
    /// before the game waits for it.
    /// </summary>
    private void CheckGameExit()
    {
        if (!_meter.Settings.WithGame || _meter.DemoRunning || _installing || _meter.Settings.NoGameScan) return;
        var now = Environment.TickCount64;
        if (GameProcessLocator.FindGameProcessIds().Length > 0)
        {
            _gameSeen = true;
            _gameGoneSince = 0;
            return;
        }
        if (!_gameSeen) return;
        if (_gameGoneSince == 0) _gameGoneSince = now;
        else if (now - _gameGoneSince >= 15_000)
        {
            Log.Info("The game was closed: exiting with it");
            Shutdown();
        }
    }

    private void RegisterHotkeys()
    {
        if (_hotkeys is null) return;
        _hotkeys.UnregisterAll();
        if (_meter.Settings.DiagSilent) return;
        var s = _meter.Settings;
        _hotkeys.Register(s.HotkeyToggleOverlay, () => SetOverlayVisible(!_overlay.IsVisible));
        _hotkeys.Register(s.HotkeyReset, RestartMeter);
        _overlay.ApplyHotkeyTips();
        _hotkeys.Register(s.HotkeyClickThrough, ToggleClickThrough);
        _hotkeys.Register(s.HotkeyTimers, ToggleBossTimers);
    }

    /// <summary>
    /// The overlay's restart button, the tray item and its hotkey: when the numbers look wrong, start over without
    /// closing the app — the fight so far is saved, capture restarts, and the players and bosses the meter knows stay
    /// known (a restart of the whole app mid-fight could not learn them again until the next loading screen).
    /// </summary>
    public void RestartMeter()
    {
        Log.Info("Meter restarted by the user");
        _meter.Restart();
        _meter.ResetBossKills(); // the bus count starts again with the meter
        _overlay.ShowLive();
    }

    // ------------------------------------------------------------ 1 HP: record for analysis

    private bool _analysisBusy;

    /// <summary>
    /// The record button (overlay, tray): the first press starts writing packets and events, the second packs them,
    /// sends them and puts the link in the clipboard — one paste in the chat and the recording can be decoded.
    /// </summary>
    public void ToggleAnalysis()
    {
        var t = UiText.Current;
        if (_analysisBusy) return;
        if (!_meter.Analysis.Running)
        {
            _meter.StartAnalysis();
            _overlay.ShowNotice("● " + t.AnalysisStarted, TimeSpan.FromSeconds(12));
            _tray?.ShowBalloon(AppInfo.Name, t.AnalysisStarted);
            UpdateAnalysisView();
            return;
        }
        _ = FinishAnalysisAsync();
    }

    /// <summary>
    /// 1 HP: the tray's "send the log" — the last day's logs (with the disconnect notes of the net watch) zipped and
    /// uploaded like an analysis recording; the link goes to the clipboard, the zip to the Desktop when it cannot go up.
    /// </summary>
    private async Task SendLogsAsync()
    {
        var t = UiText.Current;
        if (_analysisBusy) return;
        _analysisBusy = true;
        try
        {
            _overlay.ShowNotice("⏳ " + t.AnalysisSending, TimeSpan.FromMinutes(2));
            var zip = await Task.Run(AnalysisRecorder.PackLogs);
            var (link, error) = await _meter.Cloud.UploadAnalysisAsync(zip);
            if (link is not null)
            {
                AppHost.CopyText(link);
                Log.Info("Logs sent: " + link);
                _overlay.ShowNotice("✔ " + t.LogSent, TimeSpan.FromSeconds(30));
                _tray?.ShowBalloon(AppInfo.Name, t.LogSent, () => AppHost.CopyText(link));
                return;
            }
            var local = AnalysisRecorder.ToDesktop(zip);
            var why = error == "no_key" ? t.AnalysisNoKey : string.Format(t.AnalysisFailed, error);
            _overlay.ShowNotice("📁 " + why, TimeSpan.FromSeconds(30));
            _tray?.ShowBalloon(AppInfo.Name, why + "\n" + Path.GetFileName(local));
        }
        catch (Exception ex)
        {
            Log.Error("Sending the logs failed", ex);
            _overlay.ShowNotice("⚠ " + string.Format(t.AnalysisFailed, ex.Message), TimeSpan.FromSeconds(20));
        }
        finally
        {
            _analysisBusy = false;
        }
    }

    private async Task FinishAnalysisAsync()
    {
        var t = UiText.Current;
        _analysisBusy = true;
        _overlay.ShowNotice("⏳ " + t.AnalysisPacking, TimeSpan.FromMinutes(5));
        try
        {
            var zip = await Task.Run(_meter.StopAnalysis);
            UpdateAnalysisView();
            if (zip is null) return;
            var mb = new FileInfo(zip).Length / 1024.0 / 1024.0;
            _overlay.ShowNotice($"⏳ {t.AnalysisSending} ({mb:0.#} MB)", TimeSpan.FromMinutes(10));
            var (link, error) = await _meter.Cloud.UploadAnalysisAsync(zip);
            if (link is not null)
            {
                AppHost.CopyText(link);
                Log.Info("Analysis sent: " + link);
                _overlay.ShowNotice("✔ " + t.AnalysisSent, TimeSpan.FromSeconds(30));
                _tray?.ShowBalloon(AppInfo.Name, t.AnalysisSent, () => AppHost.CopyText(link));
                return;
            }
            Log.Info("Analysis not sent: " + error);
            var local = AnalysisRecorder.ToDesktop(zip);
            var why = error == "no_key" ? t.AnalysisNoKey : string.Format(t.AnalysisFailed, error);
            _overlay.ShowNotice("📁 " + why, TimeSpan.FromSeconds(30));
            _tray?.ShowBalloon(AppInfo.Name, why + "\n" + Path.GetFileName(local));
        }
        catch (Exception ex)
        {
            Log.Error("Analysis recording failed", ex);
            _overlay.ShowNotice("⚠ " + string.Format(t.AnalysisFailed, ex.Message), TimeSpan.FromSeconds(20));
        }
        finally
        {
            _analysisBusy = false;
            UpdateAnalysisView();
        }
    }

    private void UpdateAnalysisView()
    {
        var a = _meter.Analysis;
        _overlay.ShowRecording(a.Running, a.Running ? DateTime.Now - a.StartedAt : TimeSpan.Zero);
        _tray?.SetAnalysis(a.Running);
    }

    // ------------------------------------------------------------ window management (used by windows via AppHost)

    public void SetOverlayVisible(bool visible)
    {
        _autoShown = false; // the user decided: automatic hiding no longer applies
        if (visible)
        {
            _overlay.Show();
            _overlay.Refresh();
        }
        else
        {
            _overlay.Hide();
            _suppressedFight = _meter.Tracker.LiveFight()?.Id;
        }
        _meter.Settings.OverlayVisible = visible;
        _meter.Settings.Save();
    }

    private void ToggleClickThrough()
    {
        var on = !_meter.Settings.ClickThrough;
        _overlay.SetClickThrough(on);
        _tray?.ShowBalloon(AppInfo.Name, on
            ? string.Format(UiText.Current.ClickThroughOn, _meter.Settings.HotkeyClickThrough)
            : UiText.Current.ClickThroughOff);
    }

    public void ShowBreakdown(Guid segmentId, uint actorId) =>
        ShowBreakdown(segmentId, new LiveFightView(_meter.Tracker, segmentId), actorId);

    public void ShowBreakdown(IFightView view, uint actorId) => ShowBreakdown(view, view, actorId);

    private void ShowBreakdown(object key, IFightView view, uint actorId)
    {
        if (_breakdowns.TryGetValue(key, out var existing))
        {
            existing.Select(actorId);
            Activate(existing);
            return;
        }
        var w = new BreakdownWindow(view, actorId, _meter.Data, _meter.Icons, _meter.Portraits);
        _breakdowns[key] = w;
        w.Closed += (_, _) => _breakdowns.Remove(key);
        w.Show();
    }

    /// <summary>English ⇄ Russian, live: names reload, the overlay re-binds, other windows reopen in the new language.</summary>
    public void SetLanguage(string language)
    {
        _meter.SetLanguage(language);
        foreach (var w in _breakdowns.Values.ToList()) w.Close();
        _history?.Close();
        _timersWindow?.Close();
        _updateWindow?.Close();
        _tray?.ApplyTexts();
        _overlay.OnLanguageChanged();
    }

    // 1 HP: Українська → English → Русский → Українська
    public void ToggleLanguage() => SetLanguage(UiText.Languages[(Array.IndexOf(UiText.Languages, UiText.Current.Code) + 1) % UiText.Languages.Length]);

    public void ShowSavedInOverlay(Core.Storage.HistoryEntry entry)
    {
        if (!_overlay.IsVisible) SetOverlayVisible(true);
        _overlay.ShowSaved(entry);
    }

    public void ShowHistory()
    {
        if (_history is { IsLoaded: true })
        {
            _history.Reload();
            Activate(_history);
            return;
        }
        _history = new HistoryWindow(_meter);
        _history.Closed += (_, _) => _history = null;
        _history.Show();
    }

    /// <summary>Hotkey: open the boss timers, or close them when they are already up.</summary>
    private void ToggleBossTimers()
    {
        if (_timersWindow is { IsLoaded: true }) _timersWindow.Close();
        else ShowBossTimers();
    }

    public void ShowBossTimers()
    {
        if (_timersWindow is { IsLoaded: true })
        {
            Activate(_timersWindow);
            return;
        }
        _timersWindow = new BossTimersWindow(_meter);
        _timersWindow.Closed += (_, _) => _timersWindow = null;
        _timersWindow.Show();
    }

    /// <summary>Tray notification (and a chime) shortly before a tracked boss respawns.</summary>
    private long _listReminderAt = long.MinValue / 2;
    private long _loginSeenAt;

    /// <summary>
    /// 1 HP: after entering the game, when the boss times of this server are older than 2 hours, ask (once per half
    /// hour at most) to open the in-game field boss list: the overlay reads the exact times from it.
    /// </summary>
    private void CheckBossListReminder()
    {
        var now = Environment.TickCount64;
        var server = _meter.Tracker.SelfServerId;
        if (_meter.DemoRunning || server == 0 || _meter.CaptureStatus.State != CaptureState.Capturing) return;
        if (_meter.Tracker.SelfName is null) return;
        if (_loginSeenAt == 0) _loginSeenAt = now;
        if (now - _loginSeenAt < 30_000 || now - _listReminderAt < 30 * 60_000) return; // let the loading screens pass
        if (!_meter.Timers.ListStale(server, BossListMaxAge)) return;
        _listReminderAt = now;
        var t = UiText.Current;
        _tray?.ShowBalloon(t.TimersTitle, t.OpenBossListHint);
    }

    public static readonly TimeSpan BossListMaxAge = TimeSpan.FromHours(2);

    /// <summary>1 HP shared timers: what other guild overlays read from the in-game boss list on this server.</summary>
    private async Task PullSharedBossesAsync()
    {
        var server = _meter.Timers.CurrentServer;
        if (server == 0 || _meter.DemoRunning) return;
        var shared = await _meter.Cloud.FetchBossesAsync(server);
        if (shared is null || shared.Count == 0) return;
        var changed = _meter.Timers.ApplyShared(server, shared.Select(b => (b.Code, b.Alive, b.At, b.RecordedAt)));
        if (changed > 0) Log.Info($"Shared boss times: {changed} timers updated from the guild");
    }

    /// <summary>1 HP: said by the Windows voice too, when the alerts are spoken (Settings → Overlay).</summary>
    private void Say(string text)
    {
        if (_meter.Settings.VoiceAlerts) Voice.Speak(text, UiText.Current.Code);
    }

    private readonly HashSet<(int, long)> _buffsAnnounced = new();

    /// <summary>1 HP: a buff of a minute or more on you runs out in 10 s — a notice (and the voice), once per buff.</summary>
    private void CheckBuffAlerts()
    {
        if (!_meter.Settings.BuffEndAlert) return;
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        foreach (var b in _meter.Tracker.SelfBuffs(now))
        {
            if (b.Debuff || b.EndMs == 0 || b.DurationMs < 60_000) continue;
            var left = b.LeftMs(now);
            if (left is <= 0 or > 10_000 || !_buffsAnnounced.Add((b.Code, b.StartMs))) continue;
            var text = string.Format(UiText.Current.BuffEnding, b.Name, (int)Math.Ceiling(left / 1000.0));
            _overlay.ShowNotice("✦ " + text, TimeSpan.FromSeconds(8));
            System.Media.SystemSounds.Asterisk.Play();
            Say(text);
        }
        if (_buffsAnnounced.Count > 500) _buffsAnnounced.Clear();
    }

    private void OnPartyDeath(string name)
    {
        if (!_meter.Settings.PartyDeathAlert) return;
        var text = string.Format(UiText.Current.PartyDied, name);
        _overlay.ShowNotice(text, TimeSpan.FromSeconds(6));
        System.Media.SystemSounds.Hand.Play();
        Say(text);
    }

    /// <summary>1 HP: someone asks to join — who they are: class, level, combat power, gear score when inspected before.</summary>
    private void OnPartyRequest(Core.Events.PartyRequestEvent r)
    {
        if (!_meter.Settings.PartyRequestAlert) return;
        var t = UiText.Current;
        var parts = new List<string> { r.Name };
        if (r.ServerId > 0) parts.Add(_meter.Data.ServerName(r.ServerId));
        if (r.Class != Core.Events.GameClass.Unknown) parts.Add(t.ClassName(r.Class));
        parts.Add(string.Format(t.LevelShort, r.Level));
        if (r.Power > 0) parts.Add("⚡ " + Core.Format.Power(r.Power));
        if (_meter.Tracker.GearOf(r.Name) is > 0 and var gs) parts.Add(string.Format(t.GearScoreShort, gs));
        var text = "📨 " + string.Format(t.PartyRequest, string.Join(" · ", parts));
        _overlay.ShowNotice(text, TimeSpan.FromSeconds(20));
        _tray?.ShowBalloon(AppInfo.Name, text);
        System.Media.SystemSounds.Asterisk.Play();
        Say(string.Format(t.PartyRequest, r.Name));
    }

    private void OnBossKilled(int count, string boss)
    {
        var text = string.Format(UiText.Current.BusCount, boss, count);
        _overlay.ShowNotice(text, TimeSpan.FromSeconds(8));
    }

    /// <summary>1 HP: the big boss HP bar (Settings → Overlay), made on first use.</summary>
    private void UpdateBossBar()
    {
        if ((!_meter.Settings.BossBar || _meter.Settings.NoOverlay) && _bossBar is null) return;
        var snap = _meter.Settings.BossBar && !_meter.Settings.NoOverlay ? _meter.Tracker.Snapshot(null, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) : null;
        if (snap is not null && _bossBar is null) _bossBar = new BossBarWindow();
        _bossBar?.Update(snap, _meter.Settings.BossBar && !_meter.Settings.NoOverlay);
    }

    private readonly Dictionary<string, DateTimeOffset> _eventsAnnounced = new();

    /// <summary>1 HP: Rift, Shugo Festival, sieges, resets — a notice with a sound before they start (Settings → events).</summary>
    private void CheckEventAlerts()
    {
        var s = _meter.Settings;
        if (s.EventAlerts.Count == 0) return;
        var now = DateTimeOffset.UtcNow;
        var t = UiText.Current;
        foreach (var (ev, start) in Core.OneHp.EventSchedule.Due(now, s.EventAlerts, _ => s.EventAlertMinutes, _eventsAnnounced, _meter.Timers.CurrentServer))
        {
            var minutes = (int)Math.Ceiling((start - now).TotalMinutes);
            var at = start.ToLocalTime().ToString("HH:mm");
            var text = minutes > 0 ? string.Format(t.EventSoon, t.EventName(ev.Id), minutes, at) : string.Format(t.EventNow, t.EventName(ev.Id), at);
            _tray?.ShowBalloon(t.EventsTitle, text);
            _overlay.ShowNotice("⏰ " + text, TimeSpan.FromMinutes(Math.Max(1, minutes)));
            System.Media.SystemSounds.Exclamation.Play();
            Say(text);
        }
    }

    private void CheckBossAlerts()
    {
        var t = UiText.Current;
        var s = _meter.Settings;
        foreach (var timer in _meter.Timers.TakeDueAlerts(DateTimeOffset.Now, s.BossAlertMinutes,
                     s.PriorityBossAlerts ? AppSettings.PriorityAlertMinutes : 0))
        {
            var next = timer.NextSpawn!.Value;
            var minutes = (int)Math.Ceiling((next - DateTimeOffset.Now).TotalMinutes);
            var priority = _meter.Timers.IsPriority(timer.NpcCode);
            var name = (priority ? "★ " : "") + _meter.Data.NpcName(timer.NpcCode);
            var text = minutes > 0
                ? string.Format(t.AlertSoon, name, minutes, next.ToString("HH:mm"))
                : string.Format(t.AlertNow, name, next.ToString("HH:mm"));
            if (priority) text += Environment.NewLine + t.PriorityLoot;
            _tray?.ShowBalloon(t.TimersTitle, text);
            System.Media.SystemSounds.Asterisk.Play();
            Say(text.Split(Environment.NewLine)[0]);
        }
    }

    public void ShowSettings()
    {
        if (_settingsWindow is { IsLoaded: true })
        {
            Activate(_settingsWindow);
            return;
        }
        _settingsWindow = new SettingsWindow(_meter);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
    }

    // ------------------------------------------------------------ updates

    private UpdateWindow? _updateWindow;
    private Version? _announcedUpdate;
    private bool _checkingByHand;

    /// <summary>A check finished or a version was skipped: the tray item follows; a new version is announced once.</summary>
    private void OnUpdatesChanged()
    {
        var updates = _meter.Updates;
        var available = updates.Available;
        _tray?.SetUpdate(available?.Version.ToString(3));
        if (available is null || available.Version == _announcedUpdate) return;
        var t = UiText.Current;
        if (updates.AutoInstall && !updates.DownloadFailed)
        {
            // It installs itself: one notice once it is downloaded, nothing to click.
            if (updates.Downloaded?.Version != available.Version) return; // still downloading
            _announcedUpdate = available.Version;
            if (!_checkingByHand)
                _tray?.ShowBalloon(t.UpdateTitle, string.Format(t.UpdateReadyBalloon, available.Version.ToString(3)), InstallUpdateNow);
            return;
        }
        _announcedUpdate = available.Version;
        if (_checkingByHand) return; // the one who asked gets the update window instead
        _tray?.ShowBalloon(t.UpdateTitle, string.Format(t.UpdateBalloon, available.Version.ToString(3)), () => ShowUpdate());
    }

    private readonly long _startedAt = Environment.TickCount64;
    private bool _installing;
    private Version? _installFailed; // not retried by itself: the tray item and the update window remain

    /// <summary>
    /// A downloaded update goes in at a quiet moment: when the game is not running, at once (but not in the meter's first
    /// minute); while it runs, after ten minutes without a fight. The installer has no window; the meter closes and the
    /// new version starts by itself.
    /// </summary>
    private void CheckPendingUpdate()
    {
        var updates = _meter.Updates;
        if (_installing || !updates.AutoInstall || updates.Downloaded is not { } ready || updates.Available?.Version != ready.Version ||
            ready.Version == _installFailed || _meter.Tracker.LiveFight() is not null) return; // turned off, skipped, failed, or a fight is on
        var quiet = Environment.TickCount64 - Math.Max(_lastEngagedTick, _startedAt);
        if (quiet < 60_000) return;
        if (quiet < 10 * 60_000 && GameProcessLocator.FindGameProcessIds().Length > 0) return;
        InstallUpdateNow();
    }

    /// <summary>Installs the downloaded update right away (also the overlay banner's and the update window's choice).</summary>
    public void InstallUpdateNow()
    {
        if (_installing || _meter.Updates.Downloaded is not { } update) return;
        _installing = true;
        try
        {
            Log.Info($"Installing update {update.Tag}");
            // 1 HP: with the game closed and "start with AION 2" on, the updated meter waits for the next game to start it.
            _meter.Updates.InstallDownloaded(restart: !_meter.Settings.WithGame || GameProcessLocator.FindGameProcessIds().Length > 0);
            Shutdown(); // the installer replaces our files and starts the new version
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException
                                       or System.ComponentModel.Win32Exception)
        {
            _installing = false;
            _installFailed = update.Version;
            Log.Error("Update could not start", ex);
        }
    }

    /// <summary>The update window for <paramref name="release"/>, by default the available update.</summary>
    public void ShowUpdate(Core.Updates.ReleaseInfo? release = null)
    {
        release ??= _meter.Updates.Available;
        if (release is null) return;
        if (_updateWindow is { IsLoaded: true } open && open.Release.Version == release.Version)
        {
            Activate(open);
            return;
        }
        _updateWindow?.Close();
        var w = new UpdateWindow(_meter.Updates, release, Updater.CanSelfUpdate);
        w.Closed += (_, _) =>
        {
            if (_updateWindow == w) _updateWindow = null;
        };
        _updateWindow = w;
        w.Show();
    }

    /// <summary>"Check for updates" by hand (tray or settings): asks GitHub now; a newer version opens the update window.</summary>
    public async Task<UpdateCheckResult> CheckUpdatesNowAsync()
    {
        UpdateCheckResult result;
        _checkingByHand = true;
        try
        {
            result = await _meter.Updates.CheckAsync();
        }
        finally
        {
            _checkingByHand = false;
        }
        if (result == UpdateCheckResult.Available) ShowUpdate(_meter.Updates.Latest);
        return result;
    }

    private async Task CheckUpdatesFromTrayAsync()
    {
        var t = UiText.Current;
        switch (await CheckUpdatesNowAsync())
        {
            case UpdateCheckResult.UpToDate:
                _tray?.ShowBalloon(AppInfo.Name, t.UpToDate);
                break;
            case UpdateCheckResult.Failed:
                _tray?.ShowBalloon(AppInfo.Name, string.Format(t.UpdateCheckFailed, _meter.Updates.LastError));
                break;
        }
    }

    public void OnSettingsChanged()
    {
        RegisterHotkeys();
        _overlay.ApplyAppearance();
        _overlay.Refresh();
    }

    /// <summary>The Settings window's row size slider, shown on the overlay before it is saved.</summary>
    public void PreviewRowSize(int percent) => _overlay?.ApplyRowSize(percent);

    private void PickReplay()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Replay an AION 2 capture",
            Filter = "Packet captures (*.pcap;*.pcapng)|*.pcap;*.pcapng|All files|*.*",
            InitialDirectory = Path.Combine(AppSettings.AppDataDir, "captures"),
        };
        if (dialog.ShowDialog() == true) _meter.Replay(dialog.FileName);
    }

    private static void Activate(Window w)
    {
        if (w.WindowState == WindowState.Minimized) w.WindowState = WindowState.Normal;
        w.Activate();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_meter is not null) Log.Info("Exiting");
        _timer?.Stop();
        _showWait?.Unregister(null);
        _showRequest?.Dispose();
        _hotkeys?.Dispose();
        _tray?.Dispose();
        if (_meter is not null)
        {
            _overlay?.SavePlacement();
            _meter.Settings.Save();
            _meter.Dispose();
        }
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
